using Janzen.Pagination.EntityFrameworkCore.Links;

namespace Janzen.Pagination.Tests;

/// <summary>Navigation links, which appear only when a <see cref="PaginateLinkContext" /> is supplied.</summary>
public sealed class LinkTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private readonly static PaginateLinkContext Context = new("/products", [
		new KeyValuePair<string, string>("limit", "3"),
		new KeyValuePair<string, string>("filter.status", "$eq:Active"),
		new KeyValuePair<string, string>("page", "2")
	]);

	private async Task<PaginatedLinks> LinksFor(int page, PaginateLinkContext? context) {
		await using var dbContext = fixture.CreateContext();
		var result = await fixture.Products(dbContext).PageAsync<ProductDto>(new PaginateQuery { Page = page }, linkContext: context);
		return result.Links;
	}

	[Fact]
	public async Task Without_a_context_every_link_is_null() {

		var links = await this.LinksFor(1, null);

		Assert.Null(links.First);
		Assert.Null(links.Previous);
		Assert.Null(links.Next);
		Assert.Null(links.Last);

	}

	[Fact]
	public async Task Other_query_parameters_are_carried_over_and_escaped_while_page_is_replaced() {
		Assert.Equal("/products?limit=3&filter.status=%24eq%3AActive&page=1", (await this.LinksFor(2, Context)).First);
	}

	[Fact]
	public async Task A_middle_page_links_in_both_directions() {

		var links = await this.LinksFor(2, Context);

		Assert.EndsWith("page=1", links.Previous);
		Assert.EndsWith("page=3", links.Next);
		Assert.EndsWith("page=3", links.Last);

	}

	[Fact]
	public async Task The_first_page_has_no_previous() {

		var links = await this.LinksFor(1, Context);

		Assert.Null(links.Previous);
		Assert.EndsWith("page=2", links.Next);

	}

	[Fact]
	public async Task The_last_page_has_no_next() {

		var links = await this.LinksFor(3, Context);

		Assert.EndsWith("page=2", links.Previous);
		Assert.Null(links.Next);

	}

	[Fact]
	public async Task Links_are_relative_to_the_path_with_no_scheme_or_host() {
		Assert.StartsWith("/products?", (await this.LinksFor(1, Context)).First);
	}

}
