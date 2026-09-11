using Janzen.Pagination.AspNetCore;
using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Net;
using System.Text.Json;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>
///     The controller half of the package, which nothing else exercises: the model binder, the provider that
///     installs it, the <c>PaginateExceptionFilter</c> that <c>AddAspNetCore()</c> registers, and
///     <c>[PaginatedQuery&lt;TProvider&gt;]</c> on a real action.
/// </summary>
[ApiController]
[Route("mvc/products")]
public sealed class MvcProductsController : ControllerBase {

	[HttpGet]
	[PaginatedQuery<DocumentedConfigProvider>]
	public Task<PaginatedResponse<ProductDto>> List([FromQuery] PaginateQuery request, CancellationToken ct) {
		return TestData.Products().AsQueryable()
			.PaginateAsync<Product, ProductDto>(request, DocumentedConfigProvider.Config, this.Request, ct);
	}

	/// <summary>
	///     The same action without <c>[PaginatedQuery]</c>, so the OpenAPI suite has the untransformed shape to
	///     compare against: ApiExplorer expands the bound <see cref="PaginateQuery" /> into its PascalCase
	///     properties, which is exactly what the transformer removes on the marked action.
	/// </summary>
	[HttpGet("unmarked")]
	public Task<PaginatedResponse<ProductDto>> Unmarked([FromQuery] PaginateQuery request, CancellationToken ct) {
		return TestData.Products().AsQueryable()
			.PaginateAsync<Product, ProductDto>(request, DocumentedConfigProvider.Config, this.Request, ct);
	}

}

/// <summary>
///     One host carrying both pipelines over the same config, so a controller response and a Minimal API response
///     to the same query string can be compared on the wire rather than as filter objects.
///     <c>AddProblemDetails</c> here is Microsoft's own canonical customizer sample — <c>Extensions.Add</c>, which
///     throws on a second invocation, so "the customizer runs exactly once" is proven by the request succeeding at
///     all rather than by counting.
/// </summary>
public sealed class PaginationHostFixture : IAsyncLifetime {

	private WebApplication? app;

	public HttpClient Client { get; private set; } = null!;

	public async ValueTask InitializeAsync() {

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Logging.ClearProviders();

		builder.Services.AddPagination(pagination => pagination.AddAspNetCore());
		builder.Services.AddControllers().AddApplicationPart(typeof(MvcProductsController).Assembly);
		builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails =
			context => context.ProblemDetails.Extensions.Add("nodeId", "fixture"));

		this.app = builder.Build();

		this.app.MapControllers();
		this.app.MapGet("/minimal/products", async (HttpContext http, CancellationToken ct) =>
				await TestData.Products().AsQueryable()
					.PaginateAsync<Product, ProductDto>(http.Request.ToPaginateQuery(), DocumentedConfigProvider.Config, http.Request, ct))
			.WithPagination<DocumentedConfigProvider>();

		await this.app.StartAsync();

		this.Client = new HttpClient { BaseAddress = new Uri(this.app.Urls.First()) };

	}

	public async ValueTask DisposeAsync() {

		this.Client?.Dispose();

		if (this.app is not null) {
			await this.app.StopAsync();
			await this.app.DisposeAsync();
		}

	}

}

public sealed class MvcPipelineTests(PaginationHostFixture fixture) : IClassFixture<PaginationHostFixture> {

	/// <summary>
	///     The body comes back as text, not parsed: a failure here is an unhandled exception answered with an
	///     empty 500 body, and parsing first would report a JSON error instead of the status that is the point.
	/// </summary>
	private async Task<(HttpResponseMessage Response, string Body)> GetAsync(string url) {

		var response = await fixture.Client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);

		return (response, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

	}

	private static JsonElement Json(string body) { return JsonDocument.Parse(body).RootElement.Clone(); }

	[Fact]
	public async Task The_controller_action_binds_the_query_string_and_pages() {

		// AddAspNetCore() -> PaginateQueryModelBinderProvider -> PaginateQueryModelBinder, over a bound
		// [FromQuery] PaginateQuery. Nothing else in the suite reaches any of the three.
		(var response, string text) = await this.GetAsync("/mvc/products?limit=2&page=2&sortBy=rank:DESC&filter.status=$eq:Active");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);

		var body = Json(text);
		var meta = body.GetProperty("meta");
		Assert.Equal(5, meta.GetProperty("totalItems").GetInt32());
		Assert.Equal(2, meta.GetProperty("currentPage").GetInt32());
		Assert.Equal(2, meta.GetProperty("itemsPerPage").GetInt32());
		Assert.Equal(["rank:DESC"], meta.GetProperty("sortBy").EnumerateArray().Select(v => v.GetString()));

		// rank DESC over the five Active rows (10, 20, 40, 70, 80) puts 40 and 20 on page 2.
		Assert.Equal([40, 20], body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("rank").GetInt32()));

		// The HttpRequest overload is what fills links; the link base is the controller route.
		Assert.StartsWith("/mvc/products?", body.GetProperty("links").GetProperty("current").GetString()!, StringComparison.Ordinal);

	}

	[Fact]
	public async Task Both_pipelines_serve_the_400_as_problem_json() {

		// The media type the OpenAPI document this package emits advertises, and the one RFC 9457 reserves for
		// this payload. The MVC filter leaves ContentTypes empty, so on a runtime that content-negotiates the
		// answer this is the assertion that notices; ProblemDetailsTests pins the declared media type itself.
		(var mvc, _) = await this.GetAsync("/mvc/products?sortBy=name:UP");
		(var minimal, _) = await this.GetAsync("/minimal/products?sortBy=name:UP");

		Assert.Equal(HttpStatusCode.BadRequest, mvc.StatusCode);
		Assert.Equal("application/problem+json", mvc.Content.Headers.ContentType?.MediaType);
		Assert.Equal(HttpStatusCode.BadRequest, minimal.StatusCode);
		Assert.Equal("application/problem+json", minimal.Content.Headers.ContentType?.MediaType);

	}

	[Fact]
	public async Task The_minimal_api_400_survives_the_documented_customizer() {

		// The endpoint filter built the payload through ProblemDetailsFactory (invocation 1) and then handed it
		// to Results.Problem, whose ProblemHttpResult routes it through IProblemDetailsService (invocation 2).
		// The canonical customizer sample's Extensions.Add throws the second time, so the documented 400 came
		// back as a 500.
		(var response, string body) = await this.GetAsync("/minimal/products?sortBy=name:UP");

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("fixture", Json(body).GetProperty("nodeId").GetString());

	}

	[Fact]
	public async Task Both_pipelines_answer_the_same_400_body() {

		(_, string mvcText) = await this.GetAsync("/mvc/products?sortBy=name:UP");
		(_, string minimalText) = await this.GetAsync("/minimal/products?sortBy=name:UP");

		var mvc = Json(mvcText);
		var minimal = Json(minimalText);

		foreach (string member in new[] { "type", "title", "status", "detail", "code", "nodeId" }) {
			Assert.Equal(mvc.GetProperty(member).ToString(), minimal.GetProperty(member).ToString());
		}

		Assert.Equal("Invalid query", mvc.GetProperty("title").GetString());
		Assert.Equal("Sort direction 'UP' is not supported.", mvc.GetProperty("detail").GetString());

		// traceId is per-request, so only its presence can be compared. Both legs reach something that adds it:
		// the controller ProblemDetailsFactory, the Minimal API the problem-details writer.
		Assert.True(mvc.TryGetProperty("traceId", out _));
		Assert.True(minimal.TryGetProperty("traceId", out _));

		// Nothing populates instance on either leg, which is why the published 400 schema stops advertising it.
		Assert.False(mvc.TryGetProperty("instance", out _));
		Assert.False(minimal.TryGetProperty("instance", out _));

	}

	[Fact]
	public async Task Both_pipelines_carry_the_machine_readable_cause() {

		// Without it the only way to branch on the cause is to match the detail prose, which pins the wording
		// permanently and cannot be localised.
		(_, string mvc) = await this.GetAsync("/mvc/products?sortBy=name:UP");
		(_, string minimal) = await this.GetAsync("/minimal/products?filter.status=$eq:Nope");

		Assert.Equal(nameof(PaginateQueryError.SortDirectionUnknown), Json(mvc).GetProperty("code").GetString());
		Assert.Equal(nameof(PaginateQueryError.ValueInvalid), Json(minimal).GetProperty("code").GetString());

	}

	[Fact]
	public async Task A_page_the_binder_could_not_parse_is_a_400_from_the_filter() {

		// The binder records the problem and never fails the bind, so the 400 has to come from the engine
		// through PaginateExceptionFilter -- the path that would otherwise surface as framework model state.
		(var response, string body) = await this.GetAsync("/mvc/products?page=abc");

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("Query parameter 'page' must be a positive integer.", Json(body).GetProperty("detail").GetString());

		// The deferred error carries its code through the bound request too, not only the message.
		Assert.Equal(nameof(PaginateQueryError.PageOutOfRange), Json(body).GetProperty("code").GetString());

	}

	[Fact]
	public async Task An_unknown_query_parameter_is_ignored_rather_than_rejected() {

		(var response, string body) = await this.GetAsync("/mvc/products?offset=40&utm_source=newsletter");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(8, Json(body).GetProperty("meta").GetProperty("totalItems").GetInt32());

	}

}
