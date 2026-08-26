using Microsoft.EntityFrameworkCore;

using System.Globalization;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The two public composers, which hand back the query the engine would run instead of running it. Their whole
///     value rests on one claim — that the SQL they show is the SQL the engine executes — so that is asserted
///     against a captured command rather than inferred from the rows coming back.
/// </summary>
public sealed class ComposerTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private readonly static PaginateQuery Request = new() {
		Page = 2,
		Limit = 3,
		SortBy = ["rank:DESC"],
		Search = "i",
		Filters = new Dictionary<string, IReadOnlyList<string>> { ["status"] = ["$eq:Active"] }
	};

	/// <summary>Orders only by the tie-breaker: nothing is ever *requested*, so the sort echo is empty rather than null.</summary>
	private readonly static PaginateConfig<Product> TieBreakerOnly = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id));

	private static string Rejects(Action act) { return Assert.Throws<PaginateQueryException>(act).Message; }

	[Fact]
	public async Task The_composed_page_query_is_the_one_the_engine_executes() {

		List<string> executedSql = [];
		await using var context = fixture.CreateLoggingContext(executedSql);

		string composed = SqliteFixture.Products(context).ApplyPagination(Request, TestData.Config).Query.ToQueryString();

		// PaginateMapAsync adds no SQL-side projection, so the engine's page command is the composed query verbatim.
		// Anything that makes the two drift apart — a stage reordered, a guard applied on one path only — fails here.
		await SqliteFixture.Products(context).PageMapAsync(Request, product => product.Id);

		// Two commands run: the count, then the page. Only the page carries a LIMIT.
		string executed = Assert.Single(executedSql.Select(CommandSql), sql => sql.Contains("LIMIT", StringComparison.Ordinal));

		Assert.Equal(Normalize(executed), Normalize(composed));

	}

	[Fact]
	public async Task The_composed_page_query_returns_the_same_rows_the_engine_returns() {

		await using var context = fixture.CreateContext();

		var composed = SqliteFixture.Products(context).ApplyPagination(Request, TestData.Config);
		int[] direct = await composed.Query.Select(product => product.Id).ToArrayAsync(TestContext.Current.CancellationToken);

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(Request);

		Assert.Equal(page.Items.Select(item => item.Id).ToArray(), direct);

	}

	[Fact]
	public void The_composed_state_reports_the_effective_request_not_the_raw_one() {

		using var context = fixture.CreateContext();

		var composed = SqliteFixture.Products(context).ApplyPagination(new PaginateQuery(), TestData.Config);

		Assert.Equal(1, composed.Page);
		Assert.Equal(3, composed.Limit);             // the configured default, not the omitted request value
		Assert.Equal(["rank:ASC"], composed.SortBy); // the configured DefaultSortBy, tie-breaker excluded
		Assert.Null(composed.Search);
		Assert.Empty(composed.SearchBy);
		Assert.Empty(composed.Filter);

	}

	[Fact]
	public async Task The_composed_state_is_the_meta_the_engine_reports() {

		await using var context = fixture.CreateContext();

		var composed = SqliteFixture.Products(context).ApplyPagination(Request, TestData.Config);
		var meta = (await SqliteFixture.Products(context).PageAsync<ProductDto>(Request)).Meta;

		Assert.Equal(meta.SortBy, composed.SortBy);
		Assert.Equal(meta.Search, composed.Search);
		Assert.Equal(meta.SearchBy, composed.SearchBy);
		Assert.Equal(meta.Filter, composed.Filter);
		Assert.Equal(meta.ItemsPerPage, composed.Limit);
		Assert.Equal(meta.CurrentPage, composed.Page);

	}

	[Fact]
	public async Task The_filtered_composer_matches_without_paging_or_ordering() {

		await using var context = fixture.CreateContext();

		int matched = await SqliteFixture.Products(context)
			.ApplyPaginateFilters(Query.Filter("status", "$eq:Active"), TestData.Config)
			.Query.CountAsync(TestContext.Current.CancellationToken);

		// Five active products — more than the default page holds, which is the point: no Take was applied.
		Assert.Equal(5, matched);

	}

	[Fact]
	public async Task The_filtered_composer_aggregates_over_the_whole_match_set() {

		await using var context = fixture.CreateContext();

		// The facet recipe, as living documentation: group the filtered set, not the page.
		var facets = await SqliteFixture.Products(context)
			.ApplyPaginateFilters(Query.Filter("rank", "$gte:30"), TestData.Config)
			.Query.GroupBy(product => product.Status)
			.Select(group => new { Status = group.Key, Count = group.Count() })
			.ToDictionaryAsync(row => row.Status, row => row.Count, TestContext.Current.CancellationToken);

		Assert.Equal(2, facets[ProductStatus.Draft]);
		Assert.Equal(3, facets[ProductStatus.Active]);
		Assert.Equal(1, facets[ProductStatus.Discontinued]);

	}

	[Fact]
	public void The_filtered_composer_does_not_validate_sortBy() {

		using var context = fixture.CreateContext();

		// Ordering never runs on this path, so rejecting a sort it will not apply would refuse a usable request.
		var composed = SqliteFixture.Products(context).ApplyPaginateFilters(Query.Sort("nonexistent:ASC"), TestData.Config);

		// null, not empty: the difference between "never resolved" and "resolved to no ordering".
		Assert.Null(composed.SortBy);
		Assert.NotNull(composed.Query);

	}

	[Fact]
	public void The_filtered_composer_still_reports_every_other_effective_value() {

		using var context = fixture.CreateContext();

		// Only the ordering is unknowable on this path; page, limit, search and filters are resolved as usual.
		var filtered = SqliteFixture.Products(context).ApplyPaginateFilters(Request, TestData.Config);
		var paged = SqliteFixture.Products(context).ApplyPagination(Request, TestData.Config);

		Assert.Null(filtered.SortBy);
		Assert.Equal(paged.Page, filtered.Page);
		Assert.Equal(paged.Limit, filtered.Limit);
		Assert.Equal(paged.Search, filtered.Search);
		Assert.Equal(paged.SearchBy, filtered.SearchBy);
		Assert.Equal(paged.Filter, filtered.Filter);

	}

	[Fact]
	public void The_paged_composer_resolves_an_absent_sort_to_an_empty_list_not_null() {

		using var context = fixture.CreateContext();

		// A config with no DefaultSortBy and no request sort still has the tie-breaker, so it orders - but nothing
		// was *requested*, and that is an empty echo rather than the null the filtered composer returns.
		var composed = SqliteFixture.Products(context).ApplyPagination(new PaginateQuery(), TieBreakerOnly);

		Assert.NotNull(composed.SortBy);
		Assert.Empty(composed.SortBy);

	}

	[Fact]
	public async Task A_page_past_the_end_composes_a_query_that_returns_nothing() {

		await using var context = fixture.CreateContext();

		var request = new PaginateQuery { Page = 4, Limit = 3 };

		// This is the one place the composed query is not the executed one: PaginateAsync's count already said
		// there is nothing to fetch, so it runs no page query at all. The composer has no count, composes the
		// real query, and arrives at the same answer by paying for it. The cost is bounded by the match set —
		// OFFSET cannot skip rows that do not exist — so it is the eight rows here, not the offset.
		var composed = SqliteFixture.Products(context).ApplyPagination(request, TestData.Config);
		int[] rows = await composed.Query.Select(product => product.Id).ToArrayAsync(TestContext.Current.CancellationToken);

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(request);

		Assert.Empty(rows);
		Assert.Empty(page.Items);

	}

	[Fact]
	public async Task An_absurd_page_saturates_the_offset_rather_than_overflowing() {

		await using var context = fixture.CreateContext();

		// page × limit is 4_999_999_950, which Skip(int) cannot express. Saturating is the deliberate ceiling:
		// int.MaxValue is past the end of any real table, so the rows are the same and nothing wraps negative.
		var composed = SqliteFixture.Products(context)
			.ApplyPagination(new PaginateQuery { Page = 100_000_000, Limit = 50 }, TestData.Config);

		Assert.Contains(int.MaxValue.ToString(CultureInfo.InvariantCulture), composed.Query.ToQueryString(), StringComparison.Ordinal);
		Assert.Empty(await composed.Query.ToArrayAsync(TestContext.Current.CancellationToken));

	}

	[Fact]
	public void The_paged_composer_validates_sortBy() {

		using var context = fixture.CreateContext();

		Assert.Equal("Sort for field 'nonexistent' is not configured.",
			Rejects(() => SqliteFixture.Products(context).ApplyPagination(Query.Sort("nonexistent:ASC"), TestData.Config)));

	}

	[Fact]
	public void Both_composers_reject_an_unknown_filter_field() {

		using var context = fixture.CreateContext();
		var request = Query.Filter("unknown", "$eq:1");
		const string Expected = "Filter for field 'unknown' is not configured.";

		Assert.Equal(Expected, Rejects(() => SqliteFixture.Products(context).ApplyPaginateFilters(request, TestData.Config)));
		Assert.Equal(Expected, Rejects(() => SqliteFixture.Products(context).ApplyPagination(request, TestData.Config)));

	}

	[Fact]
	public void Both_composers_reject_an_out_of_range_page_and_limit() {

		using var context = fixture.CreateContext();

		Assert.Equal("Query parameter 'page' must be a positive integer.",
			Rejects(() => SqliteFixture.Products(context).ApplyPaginateFilters(new PaginateQuery { Page = 0 }, TestData.Config)));

		Assert.Equal("Query parameter 'limit' must be between 1 and 50.",
			Rejects(() => SqliteFixture.Products(context).ApplyPagination(new PaginateQuery { Limit = 999 }, TestData.Config)));

	}

	[Fact]
	public async Task The_in_memory_leg_composes_a_working_queryable() {

		var products = TestData.Products().AsQueryable();

		// No IAsyncQueryProvider here, so the engine takes its string.IndexOf branch — the composer has to work anyway.
		var composed = products.ApplyPagination(Request, TestData.Config);
		var page = await products.PageAsync<ProductDto>(Request);

		Assert.Equal(page.Items.Select(item => item.Id).ToArray(), composed.Query.Select(product => product.Id).ToArray());

	}

	/// <summary>Takes the SQL out of a <c>CommandExecuted</c> log entry — everything past the "Executed DbCommand" header line.</summary>
	private static string CommandSql(string logEntry) {
		int header = logEntry.IndexOf("Executed DbCommand", StringComparison.Ordinal);
		return header < 0 ? "" : logEntry[(logEntry.IndexOf('\n', header) + 1)..];
	}

	/// <summary>
	///     Collapses whitespace and drops <c>ToQueryString</c>'s <c>.param set</c> preamble, so the comparison is of
	///     the statement rather than of two tools' formatting.
	/// </summary>
	private static string Normalize(string sql) {
		return string.Join(' ', sql
			.Split('\n')
			.Where(line => !line.TrimStart().StartsWith(".param", StringComparison.Ordinal))
			.SelectMany(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
	}

}
