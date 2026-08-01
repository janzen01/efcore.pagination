namespace Janzen.Pagination.Tests;

/// <summary>
///     The engine over a plain <see cref="IQueryable" />, which is a supported scenario in its own right and the
///     one the guide recommends for testing a configuration without a database. It takes the other branch of the
///     engine — <c>string.IndexOf</c> instead of <c>EF.Functions.Like</c>, no <c>EF.Parameter</c>, synchronous
///     terminal operators — so it is not redundant with the SQLite leg.
///     <para>
///         It is also where the date filters live: EF Core's SQLite provider cannot translate
///         <see cref="DateTimeOffset" /> comparisons at all.
///     </para>
/// </summary>
public sealed class InMemoryTests {

	private static IQueryable<Product> Products() { return TestData.Products().AsQueryable(); }

	[Fact]
	public async Task Whole_pipeline_runs_without_a_database() {

		var request = new PaginateQuery {
			Page = 2,
			Limit = 2,
			SortBy = ["rank:ASC"],
			Search = "e",
			Filters = new Dictionary<string, IReadOnlyList<string>> { ["status"] = ["$eq:Active"] }
		};

		var page = await Products().PageAsync<ProductDto>(request);

		// Active rows whose name or description contains "e": 1, 2, 4, 7, 8 -- second page of two.
		Assertions.HasIds(page, 4, 7);
		Assert.Equal(5, page.Meta.TotalItems);
		Assert.Equal(3, page.Meta.TotalPages);

	}

	[Fact]
	public async Task Search_is_case_insensitive_in_memory() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Search("WIDGET")), 1);
	}

	[Fact]
	public async Task Ilike_is_case_insensitive_in_memory() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("name", "$ilike:IDGET")), 1);
	}

	[Fact]
	public async Task Wildcards_in_the_value_stay_literal_in_memory() {
		Assert.Empty((await Products().PageAsync<ProductDto>(Query.Filter("name", "$ilike:a%c"))).Items);
	}

	[Fact]
	public async Task Date_comparison_bounds_the_range() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("createdAt", "$gt:2026-01-03T00:00:00Z")), 3, 4, 5, 6, 7, 8);
	}

	[Fact]
	public async Task Date_between_is_inclusive() {
		Assertions.HasIds(
			await Products().PageAsync<ProductDto>(Query.Filter("createdAt", "$btw:2026-01-03T00:00:00Z,2026-01-05T00:00:00Z")),
			2, 3, 4);
	}

	[Fact]
	public async Task Date_without_an_offset_is_read_as_utc() {
		Assertions.HasIds(await Products().PageAsync<ProductDto>(Query.Filter("createdAt", "$eq:2026-01-02T00:00:00")), 1);
	}

	[Fact]
	public async Task Invalid_input_is_still_rejected_without_a_database() {
		Assert.Equal("Filter 'rank' does not support operator '$ilike'.",
			await Assertions.RejectsAsync(() => Products().PageAsync<ProductDto>(Query.Filter("rank", "$ilike:x"))));
	}

	[Fact]
	public async Task Projection_runs_without_a_database() {

		var page = await Products().PageAsync<ProductWithCategoryDto>(Query.Filter("id", "$in:1,5"));

		Assert.Equal("Electronics", page.Items[0].Category?.Name);
		Assert.Null(page.Items[1].Category);

	}

}
