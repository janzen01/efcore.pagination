namespace Janzen.Pagination.Tests;

/// <summary>
///     What the engine does with caller-supplied text that is hostile rather than merely wrong. Both legs,
///     because the guard has to sit above the provider.
/// </summary>
public sealed class InputSanitisingTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private static IQueryable<Product> InMemory() { return TestData.Products().AsQueryable(); }

	private async Task<string> RejectsOnSqlite(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request));
	}

	private static Task<string> RejectsInMemory(PaginateQuery request) {
		return Assertions.RejectsAsync(() => InMemory().PageAsync<ProductDto>(request));
	}

	// PG-09 — a nul byte reaches the provider today. PostgreSQL answers 22021 and the caller gets a 500.

	[Theory]
	[InlineData("$eq:a\0b")]
	[InlineData("$ilike:a\0b")]
	[InlineData("$sw:a\0b")]
	[InlineData("$contains:a\0b")]
	[InlineData("$in:ok,a\0b")]
	public async Task A_filter_value_carrying_a_nul_byte_is_refused_on_sqlite(string criterion) {
		Assert.Equal("Filter 'name' must not contain a null character.", await this.RejectsOnSqlite(Query.Filter("name", criterion)));
	}

	[Theory]
	[InlineData("$eq:a\0b")]
	[InlineData("$ilike:a\0b")]
	[InlineData("$sw:a\0b")]
	[InlineData("$contains:a\0b")]
	[InlineData("$in:ok,a\0b")]
	public async Task A_filter_value_carrying_a_nul_byte_is_refused_in_memory(string criterion) {
		Assert.Equal("Filter 'name' must not contain a null character.", await RejectsInMemory(Query.Filter("name", criterion)));
	}

	[Fact]
	public async Task A_nul_byte_is_refused_on_a_non_string_field_too() {
		Assert.Equal("Filter 'rank' must not contain a null character.", await this.RejectsOnSqlite(Query.Filter("rank", "$eq:1\0")));
	}

	[Fact]
	public async Task A_search_term_carrying_a_nul_byte_is_refused_on_sqlite() {
		Assert.Equal("Search term must not contain a null character.", await this.RejectsOnSqlite(Query.Search("wid\0get")));
	}

	[Fact]
	public async Task A_search_term_carrying_a_nul_byte_is_refused_in_memory() {
		Assert.Equal("Search term must not contain a null character.", await RejectsInMemory(Query.Search("wid\0get")));
	}

	/// <summary>
	///     Only the nul byte is refused. Tab, newline and the rest of C0 are legitimate text that every provider
	///     this library targets stores and compares, so a value carrying one still runs and simply matches nothing.
	/// </summary>
	[Theory]
	[InlineData("\t")]
	[InlineData("\n")]
	[InlineData("\r")]
	public async Task Other_control_characters_still_reach_the_provider(string control) {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Filter("name", $"$ilike:wid{control}get"));

		Assert.Empty(page.Items);

	}

}
