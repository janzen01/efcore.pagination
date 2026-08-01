namespace Janzen.Pagination.Tests;

/// <summary>Page arithmetic and the two request values that are validated rather than clamped.</summary>
public sealed class PagingTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await fixture.Products(context).PageAsync<ProductDto>(request);
	}

	private async Task<string> Rejects(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => fixture.Products(context).PageAsync<ProductDto>(request));
	}

	[Fact]
	public async Task An_omitted_limit_uses_the_configured_default() {

		var page = await this.Page(new PaginateQuery());

		Assert.Equal(3, page.Meta.ItemsPerPage);
		Assertions.HasIds(page, 1, 2, 3);

	}

	[Fact]
	public async Task A_middle_page_returns_its_slice() { Assertions.HasIds(await this.Page(new PaginateQuery { Page = 2 }), 4, 5, 6); }

	[Fact]
	public async Task The_last_page_may_be_partial() {

		var page = await this.Page(new PaginateQuery { Page = 3 });

		Assertions.HasIds(page, 7, 8);
		Assert.Equal(2, page.Meta.ItemCount);
		Assert.Equal(3, page.Meta.ItemsPerPage);

	}

	[Fact]
	public async Task A_page_past_the_end_is_empty_but_the_metadata_stays_truthful() {

		var page = await this.Page(new PaginateQuery { Page = 4 });

		Assert.Empty(page.Items);
		Assert.Equal(0, page.Meta.ItemCount);
		Assert.Equal(8, page.Meta.TotalItems);
		Assert.Equal(3, page.Meta.TotalPages);
		Assert.Equal(4, page.Meta.CurrentPage);

	}

	[Fact]
	public async Task An_empty_result_set_reports_zero_pages() {

		var page = await this.Page(Query.Filter("id", "$eq:999"));

		Assert.Empty(page.Items);
		Assert.Equal(0, page.Meta.TotalItems);
		Assert.Equal(0, page.Meta.TotalPages);

	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task A_non_positive_page_is_rejected(int page) {
		Assert.Equal("Query parameter 'page' must be a positive integer.", await this.Rejects(new PaginateQuery { Page = page }));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(51)]
	public async Task A_limit_outside_the_configured_range_is_rejected(int limit) {
		// Rejected, not clamped: silently returning fewer rows than asked for is the harder bug to notice.
		Assert.Equal("Query parameter 'limit' must be between 1 and 50.", await this.Rejects(new PaginateQuery { Limit = limit }));
	}

	[Fact]
	public async Task The_maximum_limit_is_allowed() {
		Assert.Equal(8, (await this.Page(new PaginateQuery { Limit = 50 })).Meta.ItemCount);
	}

}
