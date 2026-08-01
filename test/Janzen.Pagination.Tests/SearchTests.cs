namespace Janzen.Pagination.Tests;

/// <summary>Free-text search, the <c>searchBy</c> narrowing, and the validation that runs even without a term.</summary>
public sealed class SearchTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private readonly static PaginateConfig<Product> NoSearchableFields = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id));

	private readonly static PaginateConfig<Product> SearchByIgnored = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.IgnoreSearchByInQueryParam()
		.Searchable("name", p => p.Name)
		.Searchable("description", p => p.Description));

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await fixture.Products(context).PageAsync<ProductDto>(request, config);
	}

	private async Task<string> Rejects(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => fixture.Products(context).PageAsync<ProductDto>(request, config));
	}

	[Fact]
	public async Task Search_spans_every_searchable_field() {
		// "old" appears only in a description, so a hit proves the OR reached past the name field.
		Assertions.HasIds(await this.Page(Query.Search("old")), 6);
	}

	[Fact]
	public async Task SearchBy_narrows_the_search_to_the_named_fields() {
		Assert.Empty((await this.Page(Query.Search("old", "name"))).Items);
	}

	[Fact]
	public async Task An_unsearchable_field_is_rejected() {
		Assert.Equal("Search for field 'nope' is not configured.", await this.Rejects(Query.Search("x", "nope")));
	}

	[Fact]
	public async Task A_repeated_searchBy_field_is_rejected() {
		Assert.Equal("Search field 'name' is specified more than once.", await this.Rejects(Query.Search("x", "name", "name")));
	}

	[Fact]
	public async Task SearchBy_is_validated_even_when_no_term_is_supplied() {
		// Otherwise a typo would silently do nothing, which is the hard version of this bug to find.
		Assert.Equal("Search for field 'nope' is not configured.", await this.Rejects(Query.Search(null, "nope")));
	}

	[Fact]
	public async Task Searching_a_resource_with_no_searchable_fields_is_rejected() {
		Assert.Equal("Search is not configured for this resource.", await this.Rejects(Query.Search("x"), NoSearchableFields));
	}

	[Fact]
	public async Task SearchBy_is_ignored_when_the_config_says_so() {
		Assertions.HasIds(await this.Page(Query.Search("old", "name"), SearchByIgnored), 6);
	}

	[Fact]
	public async Task Wildcards_in_the_term_are_escaped() {
		Assert.Empty((await this.Page(Query.Search("a%c"))).Items);
	}

	[Fact]
	public async Task A_blank_term_searches_nothing_and_returns_everything() {
		Assert.Equal(8, (await this.Page(Query.Search("   "))).Meta.TotalItems);
	}

}
