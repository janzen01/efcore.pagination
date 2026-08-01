namespace Janzen.Pagination.Tests;

/// <summary>What each of the eleven operators actually matches, against real SQL.</summary>
public sealed class FilterOperatorTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>Grants pattern operators to a non-string field so the type guards are reachable.</summary>
	private readonly static PaginateConfig<Product> PatternsOnAnInt = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.Filterable("rank", p => p.Rank, PaginateFilterOperator.Contains, PaginateFilterOperator.StartsWith));

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await fixture.Products(context).PageAsync<ProductDto>(request, config);
	}

	private async Task<string> Rejects(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => fixture.Products(context).PageAsync<ProductDto>(request, config));
	}

	// --- $eq -------------------------------------------------------------------------------------------

	[Fact]
	public async Task Eq_matches_an_int() { Assertions.HasIds(await this.Page(Query.Filter("id", "$eq:3")), 3); }

	[Fact]
	public async Task Eq_matches_a_string() { Assertions.HasIds(await this.Page(Query.Filter("name", "$eq:Gizmo")), 3); }

	[Fact]
	public async Task Eq_matches_an_enum_by_name() { Assertions.HasIds(await this.Page(Query.Filter("status", "$eq:Draft")), 3, 5); }

	[Fact]
	public async Task Eq_matches_a_bool() { Assertions.HasIds(await this.Page(Query.Filter("isFeatured", "$eq:true")), 1, 7); }

	[Fact]
	public async Task Eq_matches_a_guid() {
		Assertions.HasIds(await this.Page(Query.Filter("externalId", $"$eq:{TestData.ExternalId(4)}")), 4);
	}

	[Fact]
	public async Task Eq_matches_a_decimal() { Assertions.HasIds(await this.Page(Query.Filter("price", "$eq:9.99")), 1); }

	[Fact]
	public async Task Eq_reaches_through_a_navigation() {
		Assertions.HasIds(await this.Page(Query.Filter("categoryName", "$eq:Food")), 7, 8);
	}

	// --- $in -------------------------------------------------------------------------------------------

	[Fact]
	public async Task In_matches_any_listed_value() { Assertions.HasIds(await this.Page(Query.Filter("id", "$in:2,4,6")), 2, 4, 6); }

	[Fact]
	public async Task In_trims_the_listed_values() { Assertions.HasIds(await this.Page(Query.Filter("id", "$in: 2 , 4 ")), 2, 4); }

	[Fact]
	public async Task In_needs_at_least_one_value() {
		Assert.Equal("Filter 'id' requires at least one '$in' value.", await this.Rejects(Query.Filter("id", "$in:")));
	}

	// --- $null -----------------------------------------------------------------------------------------

	[Fact]
	public async Task Null_matches_the_unset_rows() {
		Assertions.HasIds(await this.Page(Query.Filter("discontinuedAt", "$null")), 1, 2, 3, 4, 5, 7, 8);
	}

	[Fact]
	public async Task Not_null_matches_the_set_rows() {
		Assertions.HasIds(await this.Page(Query.Filter("discontinuedAt", "$not:$null")), 6);
	}

	[Fact]
	public async Task Null_on_a_non_nullable_value_type_matches_nothing() {
		Assert.Empty((await this.Page(Query.Filter("rank", "$null"))).Items);
	}

	[Fact]
	public async Task Not_null_on_a_non_nullable_value_type_matches_everything() {
		Assert.Equal(8, (await this.Page(Query.Filter("rank", "$not:$null"))).Meta.TotalItems);
	}

	// --- string patterns -------------------------------------------------------------------------------

	[Fact]
	public async Task StartsWith_anchors_at_the_beginning() { Assertions.HasIds(await this.Page(Query.Filter("name", "$sw:Wid")), 1, 2); }

	[Fact]
	public async Task Ilike_matches_a_substring() { Assertions.HasIds(await this.Page(Query.Filter("name", "$ilike:idget")), 1); }

	[Fact]
	public async Task Percent_in_the_value_is_escaped() {
		// Unescaped this would be "a<anything>c" and match "a_b_c"; escaped it is the literal text "a%c".
		Assert.Empty((await this.Page(Query.Filter("name", "$ilike:a%c"))).Items);
	}

	[Fact]
	public async Task Underscore_in_the_value_is_escaped() {
		// Unescaped "50_ off" would match "50% off bundle"; escaped it is a literal underscore.
		Assert.Empty((await this.Page(Query.Filter("name", "$ilike:50_ off"))).Items);
		Assertions.HasIds(await this.Page(Query.Filter("name", "$ilike:50% off")), 4);
	}

	[Fact]
	public async Task Contains_on_a_string_is_the_same_as_ilike() {

		var contains = await this.Page(Query.Filter("name", "$contains:idget"));
		var ilike = await this.Page(Query.Filter("name", "$ilike:idget"));

		Assert.Equal(ilike.Items.Select(i => i.Id), contains.Items.Select(i => i.Id));

	}

	[Fact]
	public async Task Pattern_operators_reject_a_non_string_field() {
		Assert.Equal("Filter 'rank' supports string pattern operators only for string fields.",
			await this.Rejects(Query.Filter("rank", "$sw:1"), PatternsOnAnInt));
	}

	// --- $contains over collections ---------------------------------------------------------------------

	[Fact]
	public async Task Contains_on_a_collection_requires_every_value() {
		Assertions.HasIds(await this.Page(Query.Filter("tags", "$contains:red,small")), 1);
	}

	[Fact]
	public async Task Contains_on_a_collection_matches_a_single_value() {
		Assertions.HasIds(await this.Page(Query.Filter("tags", "$contains:green")), 7, 8);
	}

	[Fact]
	public async Task Contains_needs_at_least_one_value() {
		Assert.Equal("Filter 'tags' requires at least one '$contains' value.", await this.Rejects(Query.Filter("tags", "$contains:")));
	}

	[Fact]
	public async Task Contains_rejects_a_scalar_field() {
		Assert.Equal("Filter 'rank' supports '$contains' only for string or collection fields.",
			await this.Rejects(Query.Filter("rank", "$contains:1"), PatternsOnAnInt));
	}

	// --- comparisons ------------------------------------------------------------------------------------

	[Theory]
	[InlineData("$lt:30", new[] { 1, 2 })]
	[InlineData("$lte:30", new[] { 1, 2, 3 })]
	[InlineData("$gt:60", new[] { 7, 8 })]
	[InlineData("$gte:60", new[] { 6, 7, 8 })]
	public async Task Comparison_operators_bound_the_range(string criterion, int[] expected) {
		Assertions.HasIds(await this.Page(Query.Filter("rank", criterion)), expected);
	}

	[Fact]
	public async Task Between_is_inclusive() { Assertions.HasIds(await this.Page(Query.Filter("rank", "$btw:20,40")), 2, 3, 4); }

	[Theory]
	[InlineData("$btw:20")]
	[InlineData("$btw:20,30,40")]
	public async Task Between_needs_exactly_two_values(string criterion) {
		Assert.Equal("Filter 'rank' requires exactly two '$btw' values.", await this.Rejects(Query.Filter("rank", criterion)));
	}

	// --- FilterableMany ---------------------------------------------------------------------------------

	[Fact]
	public async Task Collection_filter_matches_any_element() { Assertions.HasIds(await this.Page(Query.Filter("reviewer", "$eq:ann")), 1, 2); }

	[Fact]
	public async Task Collection_filter_in_matches_any_element_and_any_value() {
		Assertions.HasIds(await this.Page(Query.Filter("reviewer", "$in:bob,cid")), 1);
	}

	[Fact]
	public async Task Repeating_a_collection_filter_ands_the_existence_checks() {
		// "has a review by ann" AND "has a review by bob" -- two EXISTS clauses, not one element matching both.
		Assertions.HasIds(await this.Page(Query.Filter("reviewer", "$eq:ann", "$eq:bob")), 1);
	}

	[Fact]
	public async Task Collection_filter_works_on_a_non_string_element_value() {
		Assertions.HasIds(await this.Page(Query.Filter("rating", "$gte:4")), 1);
	}

}
