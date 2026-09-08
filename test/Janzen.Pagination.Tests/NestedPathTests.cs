namespace Janzen.Pagination.Tests;

/// <summary>
///     Fields whose selector crosses a navigation (<c>p =&gt; p.Category!.Name</c>). Product 5 has no category, so
///     every case here has a row on which the in-memory leg used to throw a <see cref="NullReferenceException" />
///     while the database answered normally.
///     <para>
///         The SQLite counterparts live in <c>FilterOperatorTests</c> / <c>SortingTests</c>; what is asserted here
///         is that the other leg now gives the <b>same</b> answer, which is the whole point of the rewrite.
///     </para>
/// </summary>
public sealed class NestedPathTests {

	private static IQueryable<Product> Products() { return TestData.Products().AsQueryable(); }

	private readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
		.WithLimits(10, Query.All)
		.Sortable("id", p => p.Id)
		.Sortable("category.name", p => p.Category!.Name)
		.Sortable("category.id", p => p.Category!.Id)
		.DefaultSortBy("id")
		.WithTieBreaker(p => p.Id)
		.Searchable("category.name", p => p.Category!.Name)
		.Filterable("category.name", p => p.Category!.Name)
		.Filterable("category.id", p => p.Category!.Id)
		.FilterableMany("review.rating", p => p.Reviews, r => r.Rating));

	[Fact]
	public async Task A_filter_across_a_navigation_excludes_the_row_with_no_parent() {

		// 1, 2 and 6 are Electronics; 5 has no category and must not throw on the way past.
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("category.name", "$eq:Electronics"), Config), 1, 2, 6);

	}

	[Fact]
	public async Task A_pattern_filter_across_a_navigation_excludes_it_too() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("category.name", "$ilike:oy"), Config), 3, 4);
	}

	[Fact]
	public async Task A_value_typed_member_across_a_navigation_lifts_rather_than_throwing() {

		// Category.Id is a non-nullable int, so the rewrite has to lift it to int? for the missing row to have
		// any value at all. $gte then matches the two Food rows and skips product 5 rather than exploding.
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("category.id", "$gte:3"), Config), 7, 8);

	}

	[Fact]
	public async Task Null_across_a_navigation_matches_the_row_with_no_parent() {

		// This is the case a predicate-level null guard would have got wrong. A relational provider LEFT JOINs
		// and reports the joined column as NULL, so `category.name IS NULL` is true for a product with no
		// category; the in-memory leg now agrees instead of answering "no".
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("category.name", "$null:"), Config), 5);

	}

	[Fact]
	public async Task Search_across_a_navigation_skips_the_row_with_no_parent() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Search("food"), Config), 7, 8);
	}

	[Fact]
	public async Task Sorting_across_a_navigation_orders_the_missing_parent_as_null() {

		var request = new PaginateQuery { Limit = Query.All, SortBy = ["category.name:ASC"] };

		// LINQ puts nulls first ascending, and the id tie-breaker settles the rest: 5 (none), then
		// Electronics 1/2/6, Food 7/8, Toys 3/4.
		Assertions.HasIds(await Products().PageAsync<ProductDto>(request, Config), 5, 1, 2, 6, 7, 8, 3, 4);

	}

	[Fact]
	public async Task Sorting_a_value_typed_member_across_a_navigation_lifts_it() {

		var request = new PaginateQuery { Limit = Query.All, SortBy = ["category.id:DESC"] };
		var page = await Products().PageAsync<ProductDto>(request, Config);

		// Descending puts the nulls last, so the row with no category ends the page rather than throwing.
		Assert.Equal(5, page.Items[^1].Id);

	}

	[Fact]
	public async Task A_collection_filter_still_matches_by_any_element() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("review.rating", "$gte:5"), Config), 1);
	}

	[Fact]
	public async Task The_dotted_name_is_just_a_name() {

		IPaginateConfig meta = Config;

		Assert.Contains(meta.FilterableFields, field => field.Name == "category.name");
		Assert.Contains(meta.SortableFields, field => field.Name == "category.name");

	}

}
