namespace Janzen.Pagination.Tests;

/// <summary>
///     The <c>filter.&lt;field&gt;=[$not:][$and:|$or:]$op[:value]</c> grammar: what parses, what is rejected,
///     and how repeated criteria on one field combine.
/// </summary>
public sealed class FilterGrammarTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private async Task<string> Rejects(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request));
	}

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request);
	}

	[Fact]
	public async Task Unknown_operator_is_rejected() {
		Assert.Equal("Filter 'name' uses unknown operator '$foo'.", await this.Rejects(Query.Filter("name", "$foo:x")));
	}

	[Fact]
	public async Task Value_without_an_operator_is_rejected() {
		Assert.Equal("Filter 'name' must use the format '$operator:value'.", await this.Rejects(Query.Filter("name", "Widget")));
	}

	[Fact]
	public async Task Operator_without_a_value_is_rejected() {
		Assert.Equal("Filter 'name' must use the format '$operator:value'.", await this.Rejects(Query.Filter("name", "$eq")));
	}

	[Fact]
	public async Task Empty_criterion_is_rejected() {
		Assert.Equal("Filter 'name' must not be empty.", await this.Rejects(Query.Filter("name", "")));
	}

	[Fact]
	public async Task Unknown_field_is_rejected() {
		Assert.Equal("Filter for field 'nope' is not configured.", await this.Rejects(Query.Filter("nope", "$eq:x")));
	}

	[Fact]
	public async Task Operator_not_granted_for_the_field_is_rejected() {
		Assert.Equal("Filter 'rank' does not support operator '$ilike'.", await this.Rejects(Query.Filter("rank", "$ilike:x")));
	}

	[Fact]
	public async Task Null_is_the_one_operator_allowed_without_a_value() {
		Assertions.HasIds(await this.Page(Query.Filter("discontinuedAt", "$null")), 1, 2, 3, 4, 5, 7, 8);
	}

	[Fact]
	public async Task Everything_after_the_operator_colon_is_the_value() {
		// The value itself contains a colon; parsing stops at the first operator token and takes the rest.
		Assertions.HasIds(await this.Page(Query.Filter("name", "$eq:Doohickey: legacy")), 6);
	}

	[Fact]
	public async Task Operator_tokens_are_case_insensitive() {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$EQ:Draft")), 3, 5);
	}

	[Fact]
	public async Task Field_names_are_case_insensitive() {
		Assertions.HasIds(await this.Page(Query.Filter("STATUS", "$eq:Draft")), 3, 5);
	}

	[Fact]
	public async Task Not_negates_the_criterion() {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$not:$eq:Active")), 3, 5, 6);
	}

	[Fact]
	public async Task Criteria_on_one_field_default_to_and() {
		Assertions.HasIds(await this.Page(Query.Filter("rank", "$gte:20", "$lte:40")), 2, 3, 4);
	}

	[Fact]
	public async Task Or_joins_criteria_on_one_field() {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$eq:Draft", "$or:$eq:Discontinued")), 3, 5, 6);
	}

	[Fact]
	public async Task And_can_be_written_explicitly() {
		Assertions.HasIds(await this.Page(Query.Filter("rank", "$gte:20", "$and:$lte:40")), 2, 3, 4);
	}

	[Theory]
	[InlineData("$or:$not:$eq:Active")]
	[InlineData("$not:$or:$eq:Active")]
	public async Task Prefixes_may_come_in_either_order(string second) {
		Assertions.HasIds(await this.Page(Query.Filter("status", "$eq:Draft", second)), 3, 5, 6);
	}

	[Fact]
	public async Task Different_fields_are_always_joined_with_and() {
		Assertions.HasIds(await this.Page(Query.Filters(("status", "$eq:Active"), ("rank", "$gt:50"))), 7, 8);
	}

}
