namespace Janzen.Pagination.Tests;

/// <summary>How raw query-string text becomes a typed value, and what it says when it cannot.</summary>
public sealed class ValueConversionTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>A filterable whose value type the engine has no parser for.</summary>
	private readonly static PaginateConfig<Product> UnsupportedValueType = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.Filterable("tagsEq", p => p.Tags, PaginateFilterOperator.Eq));

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request, config);
	}

	private async Task<string> Rejects(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request, config));
	}

	[Theory]
	[InlineData("Draft")]
	[InlineData("draft")]
	[InlineData("DRAFT")]
	public async Task Enums_are_matched_by_name_ignoring_case(string value) {
		Assertions.HasIds(await this.Page(Query.Filter("status", $"$eq:{value}")), 3, 5);
	}

	[Theory]
	[InlineData("1")]
	[InlineData("-1")]
	[InlineData("+1")]
	public async Task Enums_reject_numeric_values(string value) {
		Assert.Equal($"Value '{value}' is not valid for type 'ProductStatus'.", await this.Rejects(Query.Filter("status", $"$eq:{value}")));
	}

	[Fact]
	public async Task Enums_reject_an_undefined_name() {
		Assert.Equal("Value 'Nope' is not valid for type 'ProductStatus'.", await this.Rejects(Query.Filter("status", "$eq:Nope")));
	}

	[Theory]
	[InlineData("true", new[] { 1, 7 })]
	[InlineData("TRUE", new[] { 1, 7 })]
	[InlineData("false", new[] { 2, 3, 4, 5, 6, 8 })]
	public async Task Bools_accept_only_true_and_false(string value, int[] expected) {
		Assertions.HasIds(await this.Page(Query.Filter("isFeatured", $"$eq:{value}")), expected);
	}

	[Fact]
	public async Task Bools_reject_one_and_zero() {
		Assert.Equal("Value '1' is not a valid boolean.", await this.Rejects(Query.Filter("isFeatured", "$eq:1")));
	}

	[Fact]
	public async Task Guids_report_their_own_message() {
		Assert.Equal("Value 'nope' is not a valid GUID.", await this.Rejects(Query.Filter("externalId", "$eq:nope")));
	}

	[Fact]
	public async Task Integers_report_the_target_type() {
		Assert.Equal("Value 'abc' is not valid for type 'Int32'.", await this.Rejects(Query.Filter("id", "$eq:abc")));
	}

	[Fact]
	public async Task Decimals_report_the_target_type() {
		Assert.Equal("Value 'abc' is not valid for type 'Decimal'.", await this.Rejects(Query.Filter("price", "$eq:abc")));
	}

	[Fact]
	public async Task An_empty_value_is_rejected_for_a_non_nullable_target() {
		Assert.Equal("Value for type 'Int32' must not be empty.", await this.Rejects(Query.Filter("rank", "$eq:")));
	}

	[Fact]
	public async Task An_empty_value_becomes_null_for_a_nullable_target() {
		// EF rewrites a comparison against a null parameter into IS NULL, so this lands on the unset rows
		// rather than on nothing. $null is still the operator to reach for: it says so at the call site and
		// does not depend on the provider's null semantics.
		Assertions.HasIds(await this.Page(Query.Filter("discontinuedAt", "$eq:")), 1, 2, 3, 4, 5, 7, 8);
	}

	[Fact]
	public async Task An_unparseable_target_type_is_reported_as_unsupported() {
		Assert.Equal("Filtering values of type 'List`1' is not supported.",
			await this.Rejects(Query.Filter("tagsEq", "$eq:red"), UnsupportedValueType));
	}

}
