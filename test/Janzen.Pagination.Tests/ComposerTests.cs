using Microsoft.EntityFrameworkCore;

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
			.CountAsync(TestContext.Current.CancellationToken);

		// Five active products — more than the default page holds, which is the point: no Take was applied.
		Assert.Equal(5, matched);

	}

	[Fact]
	public async Task The_filtered_composer_aggregates_over_the_whole_match_set() {

		await using var context = fixture.CreateContext();

		// The facet recipe, as living documentation: group the filtered set, not the page.
		var facets = await SqliteFixture.Products(context)
			.ApplyPaginateFilters(Query.Filter("rank", "$gte:30"), TestData.Config)
			.GroupBy(product => product.Status)
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
		Assert.NotNull(SqliteFixture.Products(context).ApplyPaginateFilters(Query.Sort("nonexistent:ASC"), TestData.Config));

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
