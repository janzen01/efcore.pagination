namespace Janzen.Pagination.Tests;

/// <summary>
///     Proves the harness itself works: the fixture seeds, the canonical config binds, and the shapes most
///     likely to defeat SQLite translation (a primitive-collection filter, a collection navigation filter, a
///     selector with a sub-collection) actually run.
/// </summary>
public sealed class SmokeTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	[Fact]
	public async Task Default_page_uses_the_configured_limit_and_default_sort() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(new PaginateQuery());

		Assertions.HasIds(page, 1, 2, 3);
		Assert.Equal(8, page.Meta.TotalItems);
		Assert.Equal(3, page.Meta.ItemsPerPage);
		Assert.Equal(3, page.Meta.TotalPages);
		Assert.Equal(1, page.Meta.CurrentPage);

	}

	[Fact]
	public async Task Filters_translate_to_sql() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Filter("status", "$eq:Draft"));

		Assertions.HasIds(page, 3, 5);

	}

	[Fact]
	public async Task Search_translates_to_sql() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Search("gizmo"));

		Assertions.HasIds(page, 3);

	}

	[Fact]
	public async Task Primitive_collection_filter_translates() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Filter("tags", "$contains:red"));

		Assertions.HasIds(page, 1, 2);

	}

	[Fact]
	public async Task Collection_navigation_filter_translates() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Filter("reviewer", "$eq:ann"));

		Assertions.HasIds(page, 1, 2);

	}

	[Fact]
	public async Task Selector_with_a_sub_collection_translates() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageSelectAsync(
			Query.Filter("id", "$eq:1"),
			p => new ProductSummary(p.Id, p.Name, p.Reviews.Count,
				p.Reviews.Select(r => new ReviewDto(r.Id, r.Reviewer, r.Rating)).ToList()));

		var summary = Assert.Single(page.Items);
		Assert.Equal(3, summary.ReviewCount);
		Assert.Equal(["ann", "bob", "cid"], summary.Reviews.Select(r => r.Reviewer).Order().ToArray());

	}

}
