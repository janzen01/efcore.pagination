namespace Janzen.Pagination.Tests;

/// <summary>Ordering: the wire format, the defaults, and the tie-breaker every configuration must declare.</summary>
public sealed class SortingTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>
	///     No sortable field and no default sort — the tie-breaker alone is the ordering. This used to be the
	///     config the engine refused to page; it is now the minimum a valid one can be.
	/// </summary>
	private readonly static PaginateConfig<Product> Unordered = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
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
	public void A_config_that_cannot_order_is_refused_at_build_time() {

		// This used to be a runtime 400 on every request the config could not order -- a configuration defect
		// reported as a client error, and one that hid for as long as every caller happened to send sortBy.
		var exception = Assert.Throws<InvalidOperationException>(() => PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.Sortable("rank", p => p.Rank)));

		Assert.StartsWith("A pagination configuration requires WithTieBreaker(...):", exception.Message);

	}

	[Fact]
	public async Task A_tie_breaker_alone_is_enough_to_page() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.WithTieBreaker(p => p.Id, PaginateSortDirection.Desc));

		Assertions.HasIds(await this.Page(new PaginateQuery { Limit = Query.All }, config), 8, 7, 6, 5, 4, 3, 2, 1);

	}

	/// <summary>
	///     Sort resolution runs before the count, so the three tests below hold for requests that never reach the
	///     row-fetching query at all. It used to run inside the non-empty branch, which meant a filter matching
	///     nothing — or simply a page past the end — answered 200 to a sort the config does not have.
	/// </summary>
	private static PaginateQuery MatchingNothing(params string[] sortBy) {
		return new PaginateQuery {
			Limit = Query.All,
			SortBy = sortBy,
			Filters = new Dictionary<string, IReadOnlyList<string>> { ["id"] = ["$eq:999"] }
		};
	}

	[Fact]
	public async Task An_unsortable_field_is_rejected_even_when_the_filter_matches_nothing() {
		Assert.Equal("Sort for field 'nope' is not configured.", await this.Rejects(MatchingNothing("nope:ASC")));
	}

	[Fact]
	public async Task An_unsortable_field_is_rejected_even_past_the_last_page() {
		Assert.Equal("Sort for field 'nope' is not configured.", await this.Rejects(new PaginateQuery { Page = 999, SortBy = ["nope:ASC"] }));
	}

	[Theory]
	[InlineData("rank:ASC", "rank:DESC")]
	[InlineData("rank:ASC", "RANK:ASC")]
	public async Task A_repeated_sort_field_is_rejected(string first, string second) {

		// Symmetric with searchBy, whose published reason is "so a client cannot ship a typo that silently
		// does nothing": the second key was dead, consumed a MaxSortFields slot and was echoed in meta.sortBy.
		Assert.Equal($"Sort field '{second.Split(':')[0]}' is specified more than once.",
			await this.Rejects(Query.Sort(first, second)));

	}

	[Fact]
	public async Task A_repeated_sort_field_is_rejected_before_the_query_runs() {
		// Resolution happens before the count, so the refusal does not depend on rows matching.
		Assert.Equal("Sort field 'rank' is specified more than once.", await this.Rejects(MatchingNothing("rank:ASC", "rank:DESC")));
	}

	[Fact]
	public async Task A_config_with_only_a_tie_breaker_orders_by_it_even_when_nothing_matches() {

		// The counterpart of the build-time refusal above: a config that declares no sortable field at all is
		// still perfectly valid, because the tie-breaker is the ordering.
		var page = await this.Page(MatchingNothing(), Unordered);

		Assert.Empty(page.Items);
		Assert.Empty(page.Meta.SortBy);

	}

}
