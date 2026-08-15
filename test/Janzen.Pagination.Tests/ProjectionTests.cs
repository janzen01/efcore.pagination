namespace Janzen.Pagination.Tests;

/// <summary>The four entry points, and the rules the automatic projection follows when it builds a DTO.</summary>
public sealed class ProjectionTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>
	///     A DTO the builder rejects must reach the caller as the engine's own exception. The projection is
	///     cached, and caching it in a plain static field initializer would have wrapped this in
	///     <see cref="TypeInitializationException" /> instead.
	/// </summary>
	private static async Task<string> RejectionMessage(Func<Task> act) {
		return (await Assert.ThrowsAsync<InvalidOperationException>(act)).Message;
	}

	[Fact]
	public async Task Auto_projection_maps_constructor_parameters_by_name() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Filter("id", "$eq:1"));

		var dto = Assert.Single(page.Items);
		Assert.Equal(1, dto.Id);
		Assert.Equal("Widget", dto.Name);
		Assert.Equal(ProductStatus.Active, dto.Status);
		Assert.Equal(10, dto.Rank);

	}

	[Fact]
	public async Task Auto_projection_recurses_into_a_nested_dto_and_propagates_null() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageAsync<ProductWithCategoryDto>(Query.Filter("id", "$in:1,5"));

		Assert.Equal("Electronics", page.Items[0].Category?.Name);
		Assert.Null(page.Items[1].Category);

	}

	[Fact]
	public async Task Auto_projection_rejects_an_untranslatable_member_pair() {

		await using var context = fixture.CreateContext();

		string message = await RejectionMessage(() => SqliteFixture.Products(context).PageAsync<UnprojectableDto>(new PaginateQuery()));

		Assert.Equal("Cannot automatically project 'Product.Name' from 'String' to 'Int32'.", message);

	}

	[Fact]
	public async Task Auto_projection_rejects_a_parameter_with_no_matching_member() {

		await using var context = fixture.CreateContext();

		string message = await RejectionMessage(() => SqliteFixture.Products(context).PageAsync<MissingMemberDto>(new PaginateQuery()));

		Assert.Equal("Cannot automatically project 'Product' because source type 'Product' has no public member named 'Nonexistent'.", message);

	}

	[Fact]
	public async Task Auto_projection_rejects_a_nullable_source_for_a_non_nullable_parameter() {

		await using var context = fixture.CreateContext();

		string message = await RejectionMessage(() => SqliteFixture.Products(context).PageAsync<NonNullableCategoryDto>(new PaginateQuery()));

		Assert.Equal("Cannot automatically project nullable source 'Product.Category' into non-nullable target parameter 'Category'.", message);

	}

	[Fact]
	public async Task A_selector_may_aggregate_and_pull_a_sub_collection() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageSelectAsync(
			Query.Filter("id", "$eq:1"),
			p => new ProductSummary(p.Id, p.Name, p.Reviews.Count,
				p.Reviews.Select(r => new ReviewDto(r.Id, r.Reviewer, r.Rating)).ToList()));

		var summary = Assert.Single(page.Items);
		Assert.Equal(3, summary.ReviewCount);
		Assert.Equal(3, summary.Reviews.Count);

	}

	[Fact]
	public async Task A_post_map_finishes_the_page_in_memory() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageSelectMapAsync(
			Query.Filter("id", "$in:1,3"),
			p => new { p.Id, Sum = p.Reviews.Sum(r => r.Rating), Count = p.Reviews.Count },
			// The guard is why this cannot be a selector: EF has nothing to translate a divide-by-zero
			// check plus rounding into.
			row => new { row.Id, Average = row.Count == 0 ? (double?)null : Math.Round(row.Sum / (double)row.Count, 1) });

		Assert.Equal(4.0, page.Items[0].Average);
		Assert.Null(page.Items[1].Average);

	}

	[Fact]
	public async Task Map_materializes_columns_but_not_navigations() {

		await using var context = fixture.CreateContext();

		var page = await SqliteFixture.Products(context).PageMapAsync(
			Query.Filter("id", "$eq:1"),
			p => new { p.Name, ReviewCount = p.Reviews.Count });

		var row = Assert.Single(page.Items);
		Assert.Equal("Widget", row.Name);
		// "Materializes the full entity" means its columns. Navigations are not included, so a mapper that
		// needs them has to Include them on the source query first.
		Assert.Equal(0, row.ReviewCount);

	}

}
