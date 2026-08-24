namespace Janzen.Pagination.Tests;

/// <summary>
///     Range operators on the types that carry no relational operator of their own. Each of these used to throw
///     <see cref="InvalidOperationException" /> while the expression tree was being built — a 500 for a request the
///     field's own allow-list had granted.
/// </summary>
public sealed class ComparisonOperatorTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private readonly static PaginateFilterOperator[] Ranges = [
		PaginateFilterOperator.GreaterThan, PaginateFilterOperator.GreaterThanOrEqual,
		PaginateFilterOperator.LessThan, PaginateFilterOperator.LessThanOrEqual,
		PaginateFilterOperator.Between
	];

	/// <summary>Ranges granted on the field types the canonical config deliberately leaves to numbers and dates.</summary>
	private readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.Filterable("name", p => p.Name, Ranges)
		.Filterable("description", p => p.Description, Ranges)
		.Filterable("externalId", p => p.ExternalId, Ranges)
		.Filterable("status", p => p.Status, Ranges)
		.Filterable("isFeatured", p => p.IsFeatured, Ranges));

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request, Config);
	}

	[Fact]
	public async Task Strings_compare_by_the_database_ordering() {
		// SQLite's default BINARY collation sorts every lowercase initial after every uppercase one.
		Assertions.HasIdsInAnyOrder(await this.Page(Query.Filter("name", "$gt:a")), 5, 8);
	}

	[Fact]
	public async Task Strings_support_an_inclusive_range() {
		// "50% off bundle" starts below "A"; the five remaining uppercase names fall inside.
		Assertions.HasIdsInAnyOrder(await this.Page(Query.Filter("name", "$btw:A,Z")), 1, 2, 3, 6, 7);
	}

	[Fact]
	public async Task Guids_compare_as_the_database_stores_them() {
		Assertions.HasIdsInAnyOrder(await this.Page(Query.Filter("externalId", $"$gt:{TestData.ExternalId(6)}")), 7, 8);
	}

	[Fact]
	public async Task Enums_compare_on_their_underlying_value() {
		// Draft(0) < Active(1) < Discontinued(2).
		Assertions.HasIdsInAnyOrder(await this.Page(Query.Filter("status", "$gt:Draft")), 1, 2, 4, 6, 7, 8);
	}

	[Fact]
	public async Task Booleans_are_rejected_rather_than_failing_the_request() {

		await using var context = fixture.CreateContext();

		Assert.Equal(
			"Filter 'isFeatured' does not support comparison operators for type 'Boolean'.",
			await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Filter("isFeatured", "$gt:false"), Config)));

	}

	[Fact]
	public async Task A_null_column_does_not_match_a_range_in_memory() {
		// Products 2 and 5 have no description, and the in-memory leg calls CompareTo for real — without the null
		// guard this is a NullReferenceException rather than a page.
		var page = await TestData.Products().AsQueryable().PageAsync<ProductDto>(Query.Filter("description", "$gt:a"), Config);

		Assertions.HasIdsInAnyOrder(page, 1, 3, 4, 6, 7, 8);
	}

}
