namespace Janzen.Pagination.Tests;

/// <summary>
///     Fields whose selector crosses a navigation (<c>p =&gt; p.Category!.Name</c>). Product 5 has no category, so
///     every case here has a row on which the in-memory leg used to throw a <see cref="NullReferenceException" />
///     while the database answered normally.
///     <para>
///         Every case runs on <b>both</b> legs against one expected result, which is the contract:
///         <c>PaginateNullSafeRewriter</c> exists so the plain-<see cref="IQueryable" /> leg answers what a
///         relational provider answers, and half of that claim used to be untested — breaking the
///         LEFT-JOIN-dependent side made nothing fail at all. The configuration below is this class's own; the
///         nested members are deliberately <b>not</b> added to <c>TestData.Config</c>, whose searchable set and
///         row counts fourteen other files assert against.
///     </para>
/// </summary>
public sealed class NestedPathTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
		.WithLimits(10, Query.All)
		.Sortable("id", p => p.Id)
		.Sortable("category.name", p => p.Category!.Name)
		.Sortable("category.id", p => p.Category!.Id)
		.DefaultSortBy("id")
		.WithTieBreaker(p => p.Id)
		.Searchable("category.name", p => p.Category!.Name)
		.Filterable("category.name", p => p.Category!.Name)
		.Filterable("category.id", p => p.Category!.Id, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThanOrEqual, PaginateFilterOperator.Null)
		.FilterableMany("review.rating", p => p.Reviews, r => r.Rating));

	private static async Task<int[]> Ids(IQueryable<Product> source, PaginateQuery request) {
		return [.. (await source.PageAsync<ProductDto>(request, Config)).Items.Select(item => item.Id)];
	}

	private static Task<int[]> InMemory(PaginateQuery request) { return Ids(TestData.Products().AsQueryable(), request); }

	private async Task<int[]> Sqlite(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Ids(SqliteFixture.Products(context), request);
	}

	/// <summary>Runs one request on both legs and asserts each returns <paramref name="expected" /> — the same ids in the same order.</summary>
	private async Task BothLegs(PaginateQuery request, params int[] expected) {

		Assert.Equal(expected, await InMemory(request));
		Assert.Equal(expected, await this.Sqlite(request));

	}

	[Fact]
	public async Task A_filter_across_a_navigation_excludes_the_row_with_no_parent() {

		// 1, 2 and 6 are Electronics; 5 has no category and must not throw on the way past.
		await this.BothLegs(Query.Filter("category.name", "$eq:Electronics"), 1, 2, 6);

	}

	[Fact]
	public async Task A_pattern_filter_across_a_navigation_excludes_it_too() {
		await this.BothLegs(Query.Filter("category.name", "$ilike:oy"), 3, 4);
	}

	[Fact]
	public async Task A_value_typed_member_across_a_navigation_lifts_rather_than_throwing() {

		// Category.Id is a non-nullable int, so the rewrite has to lift it to int? for the missing row to have
		// any value at all. $gte then matches the two Food rows and skips product 5 rather than exploding — and
		// the relational leg reaches the same answer through its LEFT JOIN, without lifting anything.
		await this.BothLegs(Query.Filter("category.id", "$gte:3"), 7, 8);

	}

	[Fact]
	public async Task Null_across_a_navigation_matches_the_row_with_no_parent() {

		// This is the case a predicate-level null guard would have got wrong, and the one the parity claim rests
		// on: a relational provider LEFT JOINs and reports the joined column as NULL, so `category.name IS NULL`
		// is true for a product with no category. Asserting it on the database leg is what makes "the in-memory
		// leg now agrees" a measurement rather than a belief.
		await this.BothLegs(Query.Filter("category.name", "$null"), 5);

	}

	[Fact]
	public async Task Search_across_a_navigation_skips_the_row_with_no_parent() {
		await this.BothLegs(Query.Search("food"), 7, 8);
	}

	[Fact]
	public async Task Sorting_across_a_navigation_orders_the_missing_parent_as_null() {

		var request = new PaginateQuery { Limit = Query.All, SortBy = ["category.name:ASC"] };

		int[] inMemory = await InMemory(request);
		int[] sqlite = await this.Sqlite(request);

		// LINQ puts nulls first ascending, and the id tie-breaker settles the rest: 5 (none), then
		// Electronics 1/2/6, Food 7/8, Toys 3/4.
		Assert.Equal([5, 1, 2, 6, 7, 8, 3, 4], inMemory);

		// The database leg is asserted as "the same rows, with the parentless one at the null end" rather than
		// as the same literal sequence: NULL ordering is the provider's to choose and SQLite happens to agree
		// with LINQ, so pointing this at a provider that orders nulls last stays a one-line change.
		Assert.Equal(inMemory.Order(), sqlite.Order());
		Assert.Equal(5, sqlite[0]);

	}

	[Fact]
	public async Task Sorting_a_value_typed_member_across_a_navigation_lifts_it() {

		var request = new PaginateQuery { Limit = Query.All, SortBy = ["category.id:DESC"] };

		int[] inMemory = await InMemory(request);
		int[] sqlite = await this.Sqlite(request);

		// Descending puts the nulls last, so the row with no category ends the page rather than throwing.
		Assert.Equal(5, inMemory[^1]);
		Assert.Equal(5, sqlite[^1]);
		Assert.Equal(inMemory.Order(), sqlite.Order());

	}

	[Fact]
	public async Task A_collection_filter_still_matches_by_any_element() {
		await this.BothLegs(Query.Filter("review.rating", "$gte:5"), 1);
	}

	[Fact]
	public void The_dotted_name_is_just_a_name() {

		IPaginateConfig meta = Config;

		Assert.Contains(meta.FilterableFields, field => field.Name == "category.name");
		Assert.Contains(meta.SortableFields, field => field.Name == "category.name");

	}

	[Fact]
	public async Task Null_on_a_value_typed_nested_member_matches_nothing_on_either_leg() {

		// The lift to int? exists so the expression has somewhere to put "absent", not so $null changes meaning.
		// A relational provider decides this from the declared type and matches no row; reading the lifted type
		// here would have matched product 5 in memory and nothing at all against a database.
		await this.BothLegs(Query.Filter("category.id", "$null"));

	}

}
