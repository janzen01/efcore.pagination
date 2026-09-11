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

	[Fact]
	public void A_relation_the_response_already_carries_survives() {

		var response = new DefaultHttpContext().Response;

		// A handler that advertises its schema first, then adds pagination. Assigning Headers.Link replaces the
		// whole header, so describedby used to disappear without a trace.
		response.Headers.Link = "</schema.json>; rel=\"describedby\"";
		response.AddPaginationLinkHeader(new PaginatedLinks("/products?page=1", null, null, "/products?page=9"));

		string?[] values = [.. response.Headers.Link];

		Assert.Equal(2, values.Length);
		Assert.Equal("</schema.json>; rel=\"describedby\"", values[0]);
		Assert.Equal("</products?page=1>; rel=\"first\", </products?page=9>; rel=\"last\"", values[1]);

	}

	[Fact]
	public void A_null_request_names_the_argument_that_is_null() {

		var source = TestData.Products().AsQueryable();
		var request = new PaginateQuery();
		var ct = TestContext.Current.CancellationToken;

		// Typed, because `null!` alone cannot pick between the HttpRequest and PaginateLinkContext overloads.
		HttpRequest httpRequest = null!;

		// Every overload also has a parameter called `request` — the PaginateQuery — so reporting that name for a
		// null HttpRequest sent the reader to inspect the wrong argument. The throw is synchronous: these are
		// Task-returning wrappers, not async methods, so an argument error never reaches the returned task.
		Assert.Equal("httpRequest", Assert.Throws<ArgumentNullException>(
			() => { _ = source.PaginateAsync<Product, ProductDto>(request, TestData.Config, httpRequest, ct); }).ParamName);

		Assert.Equal("httpRequest", Assert.Throws<ArgumentNullException>(
			() => { _ = source.PaginateSelectAsync(request, TestData.Config, p => p.Name, httpRequest, ct); }).ParamName);

		Assert.Equal("httpRequest", Assert.Throws<ArgumentNullException>(
			() => { _ = source.PaginateSelectMapAsync(request, TestData.Config, p => p.Name, name => name.Length, httpRequest, ct); }).ParamName);

		Assert.Equal("httpRequest", Assert.Throws<ArgumentNullException>(
			() => { _ = source.PaginateMapAsync(request, TestData.Config, p => p.Name, httpRequest, ct); }).ParamName);

	}

	[Fact]
	public async Task A_repeated_query_parameter_is_carried_over_once_per_value_in_order() {

		// The only behaviour the allocation rewrite of the copy loop could plausibly disturb.
		var links = (await PageAsync(Request("", "/products", "?tag=a&tag=b&limit=3"), 1)).Links!;

		Assert.Equal("/products?tag=a&tag=b&limit=3&page=1", links.Current);

	}

	[Fact]
	public async Task A_path_needing_escaping_reaches_the_context_already_escaped() {

		// PathString.ToString() is ToUriComponent(), so the bridge never hands the context a raw path — which is
		// what keeps the context's new path validation off every real request.
		var links = (await PageAsync(Request("/api v2", "/a b/products", "?limit=3"), 1)).Links!;

		Assert.Equal("/api%20v2/a%20b/products?limit=3&page=1", links.Current);

	}

}

/// <summary>
///     The link context the ASP.NET Core overloads build for the caller, which is the only construction site a
///     consumer never sees. It has to satisfy the same guard as a hand-built one.
/// </summary>
public sealed class RequestLinkContextTests {

	private static Task<PaginatedResponse<ProductDto>> Page(string path) {

		var context = new DefaultHttpContext();
		context.Request.Path = new PathString(path);
		context.Request.QueryString = new QueryString("?page=1");

		return TestData.Products().AsQueryable()
			.PaginateAsync<Product, ProductDto>(context.Request.ToPaginateQuery(), TestData.Config, context.Request, TestContext.Current.CancellationToken);

	}

	/// <summary>
	///     A percent-encoded space is a perfectly ordinary path. <c>PathString</c> holds it decoded, so the escaped
	///     form has to be asked for by name — and the guard the context applies is written against that form.
	/// </summary>
	[Fact]
	public async Task An_escaped_path_segment_still_builds_a_link_context() {

		var page = await Page("/api/my products");

		Assert.NotNull(page.Links);
		Assert.Contains("my%20products", page.Links.Current, StringComparison.Ordinal);

	}

}
