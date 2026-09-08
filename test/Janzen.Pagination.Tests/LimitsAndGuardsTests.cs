using Janzen.Pagination.EntityFrameworkCore.Links;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The offset ceiling, the opt-in unlimited read and the minimum search length — the three guards added in
///     `10.1.0` — plus the trimming that the minimum forced a decision about.
/// </summary>
public sealed class LimitsAndGuardsTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private static IQueryable<Product> Products() { return TestData.Products().AsQueryable(); }

	private static PaginateConfig<Product> Config(Action<PaginateConfigBuilder<Product>> extra) {
		return PaginateConfig<Product>.Create(b => {
			b.WithLimits(3, Query.All)
				.Sortable("rank", p => p.Rank)
				.DefaultSortBy("rank")
				.WithTieBreaker(p => p.Id)
				.Searchable("name", p => p.Name)
				.Filterable("status", p => p.Status);

			extra(b);
		});
	}

	// ---- WithMaxOffset ------------------------------------------------------------------------------

	[Fact]
	public async Task An_offset_at_the_ceiling_is_allowed() {

		// maxOffset 4 with limit 2: page 3 skips exactly 4.
		var page = await Products().PageAsync<ProductDto>(new PaginateQuery { Page = 3, Limit = 2 }, Config(b => b.WithMaxOffset(4)));

		Assertions.HasIds(page, 5, 6);

	}

	[Fact]
	public async Task An_offset_past_the_ceiling_is_rejected() {

		string message = await Assertions.RejectsAsync(() =>
			Products().PageAsync<ProductDto>(new PaginateQuery { Page = 4, Limit = 2 }, Config(b => b.WithMaxOffset(4))));

		Assert.Equal("Query parameter 'page' exceeds the allowed offset for this resource: at most 4 rows may be skipped.", message);

	}

	[Fact]
	public async Task The_offset_guard_costs_no_query() {

		List<string> executedSql = [];
		await using var context = fixture.CreateLoggingContext(executedSql);

		await Assertions.RejectsAsync(() =>
			SqliteFixture.Products(context).PageAsync<ProductDto>(new PaginateQuery { Page = 500, Limit = 10 }, Config(b => b.WithMaxOffset(100))));

		// Not even the count: the guard is arithmetic, so a guarded deep page never reaches the database at all.
		Assert.Empty(executedSql);

	}

	[Fact]
	public async Task The_ceiling_is_on_the_offset_not_the_page() {

		var config = Config(b => b.WithMaxOffset(10));

		// Page 4 at limit 2 skips 6 and passes; page 4 at limit 5 skips 15 and does not. Same page number.
		Assert.Equal(2, (await Products().PageAsync<ProductDto>(new PaginateQuery { Page = 4, Limit = 2 }, config)).Items.Count);
		await Assertions.RejectsAsync(() => Products().PageAsync<ProductDto>(new PaginateQuery { Page = 4, Limit = 5 }, config));

	}

	[Fact]
	public async Task Without_the_guard_a_deep_page_is_an_empty_page_as_before() {

		var page = await Products().PageAsync<ProductDto>(new PaginateQuery { Page = 500, Limit = 10 }, Config(_ => { }));

		Assert.Empty(page.Items);
		Assert.Equal(8, page.Meta.TotalItems);

	}

	// ---- AllowUnlimited -----------------------------------------------------------------------------

	[Fact]
	public async Task Unlimited_returns_every_row_as_one_page() {

		var page = await Products().PageAsync<ProductDto>(new PaginateQuery { Limit = -1 }, Config(b => b.AllowUnlimited(100)));

		Assert.Equal(8, page.Items.Count);
		Assert.Equal(8, page.Meta.TotalItems);
		Assert.Equal(1, page.Meta.TotalPages);
		Assert.Equal(8, page.Meta.ItemsPerPage);   // the honest value, not the requested -1
		Assert.False(page.Meta.HasNextPage);
		Assert.False(page.Meta.HasPreviousPage);

	}

	[Fact]
	public async Task Unlimited_costs_one_query_because_the_fetched_set_is_the_count() {

		List<string> executedSql = [];
		await using var context = fixture.CreateLoggingContext(executedSql);

		await SqliteFixture.Products(context).PageAsync<ProductDto>(new PaginateQuery { Limit = -1 }, Config(b => b.AllowUnlimited(100)));

		// An ordinary page runs two commands, the count and the page. Here the fetched set is the count.
		Assert.Single(executedSql);

	}

	[Fact]
	public async Task Unlimited_past_the_ceiling_is_rejected() {

		string message = await Assertions.RejectsAsync(() =>
			Products().PageAsync<ProductDto>(new PaginateQuery { Limit = -1 }, Config(b => b.AllowUnlimited(3))));

		Assert.Equal("The unlimited read is too large: this resource returns at most 3 rows for 'limit=-1'.", message);

	}

	[Fact]
	public async Task Unlimited_exactly_at_the_ceiling_is_allowed() {

		// Eight rows against a ceiling of eight: the fetch asks for nine and gets eight, which is why the
		// engine can tell "at the limit" from "over it" at all.
		var page = await Products().PageAsync<ProductDto>(new PaginateQuery { Limit = -1 }, Config(b => b.AllowUnlimited(8)));

		Assert.Equal(8, page.Items.Count);

	}

	[Fact]
	public async Task Unlimited_needs_page_one() {

		string message = await Assertions.RejectsAsync(() =>
			Products().PageAsync<ProductDto>(new PaginateQuery { Page = 2, Limit = -1 }, Config(b => b.AllowUnlimited(100))));

		Assert.Equal("Query parameter 'page' must be 1 when 'limit' is -1.", message);

	}

	[Theory]
	[InlineData(-1)]
	[InlineData(-2)]
	[InlineData(0)]
	public async Task A_non_positive_limit_is_rejected_without_the_opt_in(int limit) {

		string message = await Assertions.RejectsAsync(() =>
			Products().PageAsync<ProductDto>(new PaginateQuery { Limit = limit }, Config(_ => { })));

		Assert.Equal($"Query parameter 'limit' must be between 1 and {Query.All}.", message);

	}

	[Theory]
	[InlineData(-2)]
	[InlineData(0)]
	public async Task Only_minus_one_is_the_unlimited_literal(int limit) {

		await Assertions.RejectsAsync(() =>
			Products().PageAsync<ProductDto>(new PaginateQuery { Limit = limit }, Config(b => b.AllowUnlimited(100))));

	}

	[Fact]
	public async Task Unlimited_links_degenerate_to_one_page() {

		var links = new PaginateLinkContext("/products", [new KeyValuePair<string, string>("limit", "-1")]);
		var page = await Products().PageAsync<ProductDto>(new PaginateQuery { Limit = -1 }, Config(b => b.AllowUnlimited(100)), links);

		Assert.NotNull(page.Links);
		Assert.Null(page.Links.Previous);
		Assert.Null(page.Links.Next);
		Assert.Equal(page.Links.First, page.Links.Last);

	}

	// ---- WithMinSearchLength ------------------------------------------------------------------------

	[Fact]
	public async Task A_search_term_below_the_minimum_is_rejected() {

		string message = await Assertions.RejectsAsync(() =>
			Products().PageAsync<ProductDto>(Query.Search("a"), Config(b => b.WithMinSearchLength(3))));

		Assert.Equal("Search term must be at least 3 characters.", message);

	}

	[Fact]
	public async Task A_search_term_at_the_minimum_runs() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Search("wid"), Config(b => b.WithMinSearchLength(3))), 1, 2);
	}

	[Fact]
	public async Task Padding_does_not_buy_a_term_past_the_minimum() {

		// Three characters of whitespace around one letter used to satisfy a minimum of 3 and then search for
		// the spaces. The term is measured after trimming, so it is now the 400 it should always have been.
		string message = await Assertions.RejectsAsync(() =>
			Products().PageAsync<ProductDto>(Query.Search("  a  "), Config(b => b.WithMinSearchLength(3))));

		Assert.Equal("Search term must be at least 3 characters.", message);

	}

	[Fact]
	public async Task A_padded_term_searches_for_the_trimmed_one() {

		var page = await Products().PageAsync<ProductDto>(Query.Search("  widget  "), Config(_ => { }));

		Assertions.HasIds(page, 1);
		Assert.Equal("widget", page.Meta.Search);   // the echo reports what ran

	}

	[Fact]
	public async Task Any_non_blank_term_runs_by_default() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Search("a"), Config(_ => { })), 2, 5, 6, 7, 8);
	}

	[Fact]
	public void A_minimum_above_the_maximum_is_a_configuration_error() {

		var exception = Assert.Throws<InvalidOperationException>(() => Config(b => b.WithMinSearchLength(300)));

		Assert.Equal("Min search length 300 must not be greater than max search length 256.", exception.Message);

	}

	[Fact]
	public async Task Navigation_stops_where_the_offset_guard_does() {

		// 8 rows at limit 2 is four pages, but maxOffset 4 makes page 3 the last one a caller may ask for.
		// Handing out next -> 4 would send a client that pages by following links into a 400.
		var links = new PaginateLinkContext("/products", []);
		var page = await Products().PageAsync<ProductDto>(new PaginateQuery { Page = 3, Limit = 2 }, Config(b => b.WithMaxOffset(4)), links);

		Assert.Equal(4, page.Meta.TotalPages);       // the data really does have four pages
		Assert.False(page.Meta.HasNextPage);          // but this is the last reachable one
		Assert.NotNull(page.Links);
		Assert.Null(page.Links.Next);
		Assert.Equal("/products?page=3", page.Links.Last);

	}

	[Fact]
	public async Task Without_the_guard_navigation_reaches_every_page() {

		var links = new PaginateLinkContext("/products", []);
		var page = await Products().PageAsync<ProductDto>(new PaginateQuery { Page = 3, Limit = 2 }, Config(_ => { }), links);

		Assert.True(page.Meta.HasNextPage);
		Assert.Equal("/products?page=4", page.Links!.Last);

	}

}
