using Janzen.Pagination.EntityFrameworkCore.Links;

using System.Text.Json;

namespace Janzen.Pagination.Tests;

/// <summary>Navigation links, which appear only when a <see cref="PaginateLinkContext" /> is supplied.</summary>
public sealed class LinkTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private readonly static PaginateLinkContext Context = new("/products", [
		new KeyValuePair<string, string>("limit", "3"),
		new KeyValuePair<string, string>("filter.status", "$eq:Active"),
		new KeyValuePair<string, string>("page", "2")
	]);

	/// <summary>The serializer defaults ASP.NET Core applies, so the asserted JSON is the one clients see.</summary>
	private readonly static JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

	private async Task<PaginatedResponse<ProductDto>> PageFor(int page, PaginateLinkContext? context) {
		await using var dbContext = fixture.CreateContext();
		return await SqliteFixture.Products(dbContext).PageAsync<ProductDto>(new PaginateQuery { Page = page }, linkContext: context);
	}

	private async Task<PaginatedLinks> LinksFor(int page, PaginateLinkContext context) {

		var result = await this.PageFor(page, context);

		Assert.NotNull(result.Links);

		return result.Links;

	}

	[Fact]
	public async Task Without_a_context_there_are_no_links() {

		var page = await this.PageFor(1, null);

		Assert.Null(page.Links);

		// Meta stays truthful: it is what a caller outside ASP.NET Core navigates by, via PaginateQuery.WithPage.
		Assert.Equal(1, page.Meta.CurrentPage);
		Assert.Equal(3, page.Meta.TotalPages);

	}

	[Fact]
	public async Task Without_a_context_links_is_serialized_as_null() {
		Assert.Contains("\"links\":null", JsonSerializer.Serialize(await this.PageFor(1, null), WebJson));
	}

	[Fact]
	public async Task An_absent_link_is_serialized_as_null_rather_than_dropped() {

		// The last page has no next. That null is the answer the client asked for, so the key has to carry it —
		// a missing key would make "no next page" indistinguishable from "this API has no next link".
		string json = JsonSerializer.Serialize(await this.PageFor(3, Context), WebJson);

		Assert.Contains("\"next\":null", json);

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
