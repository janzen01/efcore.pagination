using Janzen.Pagination.AspNetCore;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>
///     What the ASP.NET Core bridge makes of an <see cref="HttpRequest" />: the links it hands back, and the
///     opt-in RFC 8288 header built from them.
/// </summary>
public sealed class LinkContextTests {

	private static HttpRequest Request(string pathBase, string path, string queryString) {

		var context = new DefaultHttpContext();

		context.Request.PathBase = pathBase;
		context.Request.Path = path;
		context.Request.QueryString = new QueryString(queryString);

		return context.Request;

	}

	private static Task<PaginatedResponse<ProductDto>> PageAsync(HttpRequest request, int page) {
		return TestData.Products().AsQueryable().PaginateAsync<Product, ProductDto>(
			new PaginateQuery { Page = page, Limit = 3 }, TestData.Config, request, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Every_link_carries_the_path_base() {

		// An app mounted under UsePathBase("/api") used to be handed links that dropped it and therefore 404.
		var links = (await PageAsync(Request("/api", "/products", "?limit=3"), 2)).Links!;

		Assert.StartsWith("/api/products?", links.First);
		Assert.StartsWith("/api/products?", links.Previous);
		Assert.StartsWith("/api/products?", links.Next);
		Assert.StartsWith("/api/products?", links.Last);

	}

	[Fact]
	public async Task An_app_without_a_path_base_is_unaffected() {
		Assert.StartsWith("/products?", (await PageAsync(Request("", "/products", "?limit=3"), 1)).Links!.First);
	}

	[Fact]
	public async Task Current_is_the_request_that_was_made() {
		Assert.Equal("/api/products?limit=3&page=2", (await PageAsync(Request("/api", "/products", "?limit=3&page=2"), 2)).Links!.Current);
	}

	[Fact]
	public async Task Current_still_answers_past_the_last_page() {

		// It echoes the request rather than reporting navigability — that is what next and previous are for.
		var links = (await PageAsync(Request("", "/products", "?limit=3"), 999)).Links!;

		Assert.Equal("/products?limit=3&page=999", links.Current);
		Assert.Null(links.Next);

	}

	[Fact]
	public void The_link_header_names_every_rel_the_page_has() {

		var response = new DefaultHttpContext().Response;

		response.AddPaginationLinkHeader(new PaginatedLinks("/products?page=1", null, "/products?page=3", "/products?page=9"));

		// previous is absent on page 1, so its rel is skipped rather than written empty.
		Assert.Equal(
			"</products?page=1>; rel=\"first\", </products?page=3>; rel=\"next\", </products?page=9>; rel=\"last\"",
			response.Headers.Link.ToString());

	}

	[Fact]
	public void The_link_header_is_left_unwritten_when_there_is_nothing_to_say() {

		var response = new DefaultHttpContext().Response;

		response.AddPaginationLinkHeader(null);
		response.AddPaginationLinkHeader(new PaginatedLinks(null, null, null, null));

		Assert.False(response.Headers.ContainsKey("Link"));

	}

}
