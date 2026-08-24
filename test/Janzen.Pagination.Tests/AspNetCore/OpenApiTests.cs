using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Text.Json;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>The config the documented endpoint advertises: one badge, and a small, predictable field set.</summary>
public sealed class DocumentedConfigProvider : IPaginateConfigProvider<Product> {

	public readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
		.WithLimits(defaultLimit: 15, maxLimit: 60)
		.Sortable("rank", p => p.Rank)
		.WithTieBreaker(p => p.Id)
		.Searchable("name", p => p.Name)
		.Filterable("status", p => p.Status, PaginateFilterOperator.Eq, PaginateFilterOperator.In)
		.Filterable("isFeatured", p => p.IsFeatured, PaginateFilterOperator.Eq)
			.When(true).ShowBadge("Admin only", "language-admin"));

	public PaginateConfig<Product> GetConfig() { return Config; }

}

/// <summary>A resource with no free-text surface at all — no <c>Searchable</c> field, and no opt-out either.</summary>
public sealed class SearchlessConfigProvider : IPaginateConfigProvider<Product> {

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.Filterable("status", p => p.Status, PaginateFilterOperator.Eq));
	}

}

/// <summary>
///     Starts a real application once and captures its OpenAPI document. Constructing an
///     <c>OpenApiOperationTransformerContext</c> by hand would test the transformer in isolation; running the
///     host also proves <c>WithPagination</c> attaches the metadata the transformer looks for.
/// </summary>
public sealed class OpenApiDocumentFixture : IAsyncLifetime {

	public JsonElement Document { get; private set; }

	public async ValueTask InitializeAsync() {

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Logging.ClearProviders();
		builder.Services.AddPagination(pagination => pagination.AddAspNetCore());
		builder.Services.AddOpenApi(options => options.AddOperationTransformer<PaginatedQueryOperationTransformer>());

		await using var app = builder.Build();

		app.MapOpenApi();
		app.MapGet("/products", () => Results.Ok()).WithPagination<DocumentedConfigProvider>();
		app.MapGet("/searchless", () => Results.Ok()).WithPagination<SearchlessConfigProvider>();
		app.MapGet("/plain", () => Results.Ok());

		await app.StartAsync();

		using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
		string json = await client.GetStringAsync("/openapi/v1.json");

		await app.StopAsync();

		this.Document = JsonDocument.Parse(json).RootElement.Clone();

	}

	public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }

}

public sealed class OpenApiTests(OpenApiDocumentFixture fixture) : IClassFixture<OpenApiDocumentFixture> {

	private JsonElement Parameters(string path) {
		return fixture.Document.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("parameters");
	}

	private string[] ParameterNames(string path) {
		return [.. this.Parameters(path).EnumerateArray().Select(p => p.GetProperty("name").GetString()!)];
	}

	private string Description(string name) {
		return this.Parameters("/products").EnumerateArray()
			.Single(p => p.GetProperty("name").GetString() == name)
			.GetProperty("description").GetString()!;
	}

	[Fact]
	public void The_documented_endpoint_advertises_every_pagination_parameter() {

		string[] names = this.ParameterNames("/products");

		Assert.Equal(["page", "limit", "sortBy", "search", "searchBy", "filter.isFeatured", "filter.status"], names);

	}

	[Fact]
	public void An_endpoint_without_the_attribute_is_untouched() {
		Assert.False(fixture.Document.GetProperty("paths").GetProperty("/plain").GetProperty("get").TryGetProperty("parameters", out _));
	}

	[Fact]
	public void The_limit_description_carries_the_resources_own_numbers() {

		string description = this.Description("limit");

		Assert.Contains("between 1 and 60", description);
		Assert.Contains("Defaults to 15", description);

	}

	[Fact]
	public void The_sort_description_lists_the_sortable_fields() { Assert.Contains("rank", this.Description("sortBy")); }

	[Fact]
	public void The_search_description_lists_the_searchable_fields() { Assert.Contains("name", this.Description("searchBy")); }

	[Fact]
	public void A_filter_description_lists_the_operators_that_field_allows() {

		string description = this.Description("filter.status");

		Assert.Contains("$eq", description);
		Assert.Contains("$in", description);
		Assert.DoesNotContain("$btw", description);

	}

	[Fact]
	public void A_badge_renders_as_a_code_chip_carrying_its_class() {
		Assert.Contains("<code class=\"language-admin\">Admin only</code>", this.Description("filter.isFeatured"));
	}

	[Fact]
	public void A_filter_parameter_carries_a_typed_example() {

		var parameter = this.Parameters("/products").EnumerateArray()
			.Single(p => p.GetProperty("name").GetString() == "filter.status");

		string example = parameter.GetProperty("schema").GetProperty("items")
			.GetProperty("examples").EnumerateArray().First().GetString()!;

		// The operator comes from the field's own allow-list, so it is one a caller may actually send.
		Assert.StartsWith("$eq:", example);

	}

	[Fact]
	public void A_filter_parameter_documents_the_value_type() {
		Assert.Contains("Value type", this.Description("filter.status"));
	}

	[Fact]
	public void The_validation_failure_response_is_documented() {

		var responses = fixture.Document.GetProperty("paths").GetProperty("/products").GetProperty("get").GetProperty("responses");

		Assert.Contains("invalid", responses.GetProperty("400").GetProperty("description").GetString()!, StringComparison.OrdinalIgnoreCase);

	}

	[Fact]
	public void The_validation_failure_schema_documents_what_the_runtime_actually_sends() {

		var properties = fixture.Document.GetProperty("paths").GetProperty("/products").GetProperty("get")
			.GetProperty("responses").GetProperty("400")
			.GetProperty("content").GetProperty("application/problem+json")
			.GetProperty("schema").GetProperty("properties");

		// traceId is added by ProblemDetailsFactory, which both pipelines go through; the schema used to list only
		// the RFC members and leave a client to discover it.
		Assert.True(properties.TryGetProperty("traceId", out _));
		Assert.True(properties.TryGetProperty("instance", out _));

	}

	[Fact]
	public void A_resource_with_nothing_searchable_advertises_neither_search_parameter() {

		// Both used to be emitted unconditionally, which is what pushes such a config into
		// IgnoreSearchByInQueryParam() purely to stop the generated documentation offering searchBy.
		Assert.Equal(["page", "limit", "sortBy", "filter.status"], this.ParameterNames("/searchless"));

	}

}
