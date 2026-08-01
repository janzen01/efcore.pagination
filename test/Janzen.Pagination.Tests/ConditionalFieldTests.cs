namespace Janzen.Pagination.Tests;

/// <summary>
///     <c>.When(false)</c> gates a field at query time while leaving it documented. The security property is
///     that a gated field is indistinguishable from one that does not exist.
/// </summary>
public sealed class ConditionalFieldTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private static PaginateConfig<Product> Gated(bool allowed) {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.Sortable("rank", p => p.Rank)
			// Not Price: SQLite's decimal collation parses the stored text with the current culture, so
			// ordering a decimal throws outright on a machine whose decimal separator is not a dot.
			.Sortable("status", p => p.Status).When(allowed).ShowBadge("Admin", "language-admin")
			.DefaultSortBy("status")
			.DefaultSortBy("rank")
			.WithTieBreaker(p => p.Id)
			.Filterable("rank", p => p.Rank, PaginateFilterOperator.Eq)
			.Filterable("isFeatured", p => p.IsFeatured, PaginateFilterOperator.Eq)
				.When(allowed).ShowBadge("Admin", "language-admin"));
	}

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request, PaginateConfig<Product> config) {
		await using var context = fixture.CreateContext();
		return await fixture.Products(context).PageAsync<ProductDto>(request, config);
	}

	private async Task<string> Rejects(PaginateQuery request, PaginateConfig<Product> config) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => fixture.Products(context).PageAsync<ProductDto>(request, config));
	}

	[Fact]
	public async Task A_gated_filter_is_rejected_exactly_like_an_unknown_one() {

		string gated = await this.Rejects(Query.Filter("isFeatured", "$eq:true"), Gated(allowed: false));
		string unknown = await this.Rejects(Query.Filter("noSuchField", "$eq:true"), Gated(allowed: false));

		// Same shape, different name -- so the error cannot be used to probe whether the field exists.
		Assert.Equal("Filter for field 'isFeatured' is not configured.", gated);
		Assert.Equal("Filter for field 'noSuchField' is not configured.", unknown);

	}

	[Fact]
	public async Task A_gated_filter_works_when_the_condition_holds() {
		Assertions.HasIds(await this.Page(Query.Filter("isFeatured", "$eq:true"), Gated(allowed: true)), 1, 7);
	}

	[Fact]
	public async Task A_gated_sort_is_rejected_when_the_condition_fails() {
		Assert.Equal("Sort for field 'status' is not configured.", await this.Rejects(Query.Sort("status:ASC"), Gated(allowed: false)));
	}

	[Fact]
	public async Task A_gated_default_sort_is_skipped_rather_than_fatal() {
		// status is a default sort and gated off; the resource must still page, falling through to rank.
		Assertions.HasIds(await this.Page(new PaginateQuery { Limit = 50 }, Gated(allowed: false)), 1, 2, 3, 4, 5, 6, 7, 8);
	}

	[Fact]
	public void A_gated_field_stays_in_the_documented_metadata() {

		var config = Gated(allowed: false);

		var field = Assert.Single(config.FilterableFields, f => f.Name == "isFeatured");
		Assert.Equal("Admin", field.Badge?.Name);
		Assert.Equal("language-admin", field.Badge?.CssClass);
		Assert.Contains(config.SortableFields, f => f.Name == "status");

	}

}
