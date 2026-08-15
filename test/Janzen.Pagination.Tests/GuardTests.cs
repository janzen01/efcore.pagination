namespace Janzen.Pagination.Tests;

/// <summary>The five ceilings that bound how expensive one request can be.</summary>
public sealed class GuardTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private static PaginateConfig<Product> WithGuards(
		int maxFilterValues = 100,
		int maxFilterConditions = 20,
		int maxSortFields = 5,
		int maxSearchLength = 256
	) {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.WithGuards(maxFilterValues, maxFilterConditions, maxSortFields, maxSearchLength)
			.Sortable("id", p => p.Id)
			.Sortable("rank", p => p.Rank)
			.WithTieBreaker(p => p.Id)
			.Searchable("name", p => p.Name)
			.Filterable("id", p => p.Id, PaginateFilterOperator.In, PaginateFilterOperator.Eq)
			.Filterable("rank", p => p.Rank, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThan));
	}

	private async Task<string> Rejects(PaginateQuery request, PaginateConfig<Product> config) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request, config));
	}

	[Fact]
	public async Task Too_many_values_in_one_filter_is_rejected() {
		Assert.Equal("Filter 'id' accepts at most 2 values.",
			await this.Rejects(Query.Filter("id", "$in:1,2,3"), WithGuards(maxFilterValues: 2)));
	}

	[Fact]
	public async Task Too_many_filter_conditions_is_rejected() {
		Assert.Equal("Too many filter conditions; at most 2 are allowed.",
			await this.Rejects(Query.Filter("id", "$eq:1", "$or:$eq:2", "$or:$eq:3"), WithGuards(maxFilterConditions: 2)));
	}

	[Fact]
	public async Task Filter_conditions_are_counted_across_fields_not_per_field() {

		var request = new PaginateQuery {
			Filters = new Dictionary<string, IReadOnlyList<string>> {
				["id"] = ["$eq:1", "$or:$eq:2"],
				["rank"] = ["$gt:0"]
			}
		};

		Assert.Equal("Too many filter conditions; at most 2 are allowed.", await this.Rejects(request, WithGuards(maxFilterConditions: 2)));

	}

	[Fact]
	public async Task Too_many_sort_fields_is_rejected() {
		Assert.Equal("Too many sort fields; at most 1 are allowed.",
			await this.Rejects(Query.Sort("rank:ASC", "id:DESC"), WithGuards(maxSortFields: 1)));
	}

	[Fact]
	public async Task Too_long_a_search_term_is_rejected() {
		Assert.Equal("Search term must not exceed 3 characters.",
			await this.Rejects(Query.Search("abcd"), WithGuards(maxSearchLength: 3)));
	}

	[Fact]
	public void Defaults_apply_when_guards_are_not_configured() {

		var config = TestData.Config;

		Assert.Equal(100, config.MaxFilterValues);
		Assert.Equal(20, config.MaxFilterConditions);
		Assert.Equal(5, config.MaxSortFields);
		Assert.Equal(256, config.MaxSearchLength);

	}

}
