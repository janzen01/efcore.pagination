namespace Janzen.Pagination.Tests;

/// <summary>
///     The wire grammar for an enum filter value. Every case here is one <c>reference/query-string/</c> already
///     calls invalid — "by name only … Numeric values are rejected" — and the parser accepted anyway, because
///     the numeric guard reads the first character while <see cref="Enum.Parse(Type, string, bool)" /> trims and
///     splits on commas before it looks at anything.
/// </summary>
public sealed class ValueWireGrammarEnumTests {

	private static IQueryable<Product> Products() { return TestData.Products().AsQueryable(); }

	private static Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request) {
		return Products().PageAsync<ProductDto>(request);
	}

	private static Task<string> Rejects(PaginateQuery request) {
		return Assertions.RejectsAsync(() => Products().PageAsync<ProductDto>(request));
	}

	/// <remarks>
	///     A bare <c>+</c> decodes to a space, so <c>?filter.status=$eq:+1</c> arrives here as <c>" 1"</c> — the
	///     shape the guard has to catch, and the one the existing <c>$eq:+1</c> case never exercised.
	/// </remarks>
	[Theory]
	[InlineData(" 1")]
	[InlineData(" +1")]
	[InlineData("  2")]
	[InlineData(" -1")]
	[InlineData("1 ")]
	public async Task An_enum_refuses_a_whitespace_padded_numeric_value(string value) {
		Assert.Equal($"Value '{value}' is not valid for 'status'.", await Rejects(Query.Filter("status", $"$eq:{value}")));
	}

	/// <remarks>
	///     <c>Enum.Parse</c> OR-combines a comma list arithmetically, so <c>$eq:Draft,Active</c> resolves to
	///     <c>0 | 1 = Active</c> and every Draft row disappears from a page the caller believes holds both.
	/// </remarks>
	[Theory]
	[InlineData("Draft,Active")]
	[InlineData("Draft, Active")]
	[InlineData("Active,Nope")]
	[InlineData("Draft,")]
	public async Task An_enum_refuses_a_comma_list_on_a_single_value_operator(string value) {
		Assert.Equal($"Value '{value}' is not valid for 'status'.", await Rejects(Query.Filter("status", $"$eq:{value}")));
	}

	[Fact]
	public async Task An_enum_list_is_still_expressed_with_the_in_operator() {
		Assertions.HasIds(await Page(Query.Filter("status", "$in:Draft,Active")), 1, 2, 3, 4, 5, 7, 8);
	}

	[Theory]
	[InlineData("Draft")]
	[InlineData("draft")]
	[InlineData(" Draft ")]
	public async Task An_enum_still_matches_a_declared_member_name(string value) {
		Assertions.HasIds(await Page(Query.Filter("status", $"$eq:{value}")), 3, 5);
	}

}
