namespace Janzen.Pagination.Tests;

/// <summary>Ordering: the wire format, the defaults, the tie-breaker, and the refusal to page unordered.</summary>
public sealed class SortingTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>No sortBy, no default, no tie-breaker — the one config the engine refuses to page.</summary>
	private readonly static PaginateConfig<Product> Unordered = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.Filterable("id", p => p.Id, PaginateFilterOperator.Eq));

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request, config);
	}

	private async Task<string> Rejects(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request, config));
	}

	[Theory]
	[InlineData("rank:DESC")]
	[InlineData("rank:desc")]
	[InlineData("rank:DeSc")]
	public async Task Direction_is_case_insensitive(string sort) {
		Assertions.HasIds(await this.Page(Query.Sort(sort)), 8, 7, 6, 5, 4, 3, 2, 1);
	}

	[Fact]
	public async Task A_missing_direction_is_rejected() {
		Assert.Equal("Sort value 'rank' must use the format 'field:ASC' or 'field:DESC'.", await this.Rejects(Query.Sort("rank")));
	}

	[Fact]
	public async Task An_unknown_direction_is_rejected() {
		Assert.Equal("Sort direction 'UP' is not supported.", await this.Rejects(Query.Sort("rank:UP")));
	}

	[Fact]
	public async Task An_unsortable_field_is_rejected() {
		Assert.Equal("Sort for field 'nope' is not configured.", await this.Rejects(Query.Sort("nope:ASC")));
	}

	[Fact]
	public async Task Sorts_are_applied_in_the_order_given() {
		// Draft(0) then Active(1) then Discontinued(2), each by descending rank.
		Assertions.HasIds(await this.Page(Query.Sort("status:ASC", "rank:DESC")), 5, 3, 8, 7, 4, 2, 1, 6);
	}

	[Fact]
	public async Task The_tie_breaker_orders_rows_the_primary_sort_ties() {
		// Sorting by status alone leaves five Active rows tied; the id tie-breaker decides among them.
		Assertions.HasIds(await this.Page(Query.Sort("status:ASC")), 3, 5, 1, 2, 4, 7, 8, 6);
	}

	[Fact]
	public async Task The_tie_breaker_direction_is_honoured() {

		// Descending, so the tied rows come back in the opposite order to the one the storage would
		// happen to hand back. Without the tie-breaker applied this assertion cannot pass by luck.
		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.Sortable("status", p => p.Status)
			.WithTieBreaker(p => p.Id, PaginateSortDirection.Desc));

		Assertions.HasIds(await this.Page(Query.Sort("status:ASC"), config), 5, 3, 8, 7, 4, 2, 1, 6);

	}

	[Fact]
	public async Task Defaults_apply_when_the_request_sorts_by_nothing() {
		Assertions.HasIds(await this.Page(new PaginateQuery { Limit = Query.All }), 1, 2, 3, 4, 5, 6, 7, 8);
	}

	[Fact]
	public async Task A_requested_sort_replaces_the_defaults_rather_than_extending_them() {
		// Products 1 and 2 are the only reviewed ones; the rest tie at zero and fall to the tie-breaker.
		// Under the default rank sort this would be 1..8, so the defaults demonstrably did not apply.
		Assertions.HasIds(await this.Page(Query.Sort("reviewCount:DESC")), 1, 2, 3, 4, 5, 6, 7, 8);
	}

	[Fact]
	public async Task Paging_without_any_ordering_is_refused() {
		Assert.Equal(
			"Pagination requires a deterministic sort order. Pass 'sortBy', configure DefaultSortBy(...), or add WithTieBreaker(...) to the pagination configuration.",
			await this.Rejects(new PaginateQuery(), Unordered));
	}

	[Fact]
	public async Task A_tie_breaker_alone_is_enough_to_page() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.WithTieBreaker(p => p.Id, PaginateSortDirection.Desc));

		Assertions.HasIds(await this.Page(new PaginateQuery { Limit = Query.All }, config), 8, 7, 6, 5, 4, 3, 2, 1);

	}

}
