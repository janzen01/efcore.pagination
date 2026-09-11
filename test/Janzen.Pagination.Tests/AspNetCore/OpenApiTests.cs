using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Like;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Linq.Expressions;
using System.Text.Json;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>The config the documented endpoint advertises: one badge, and a small, predictable field set.</summary>
public sealed class DocumentedConfigProvider : IPaginateConfigProvider<Product> {

	public readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
		.WithLimits(defaultLimit: 15, maxLimit: 60)
		.Sortable("rank", p => p.Rank)
		.Sortable("name", p => p.Name)
		.WithTieBreaker(p => p.Id)
		.Searchable("name", p => p.Name)
		// Declared widest-first on purpose: the emitted example and the operator bullet list are both pinned
		// to an explicit rule, so neither may follow the order the operators happen to be declared in.
		.Filterable("status", p => p.Status, PaginateFilterOperator.In, PaginateFilterOperator.Eq)
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
///     A resource whose configuration carries its own <see cref="IPaginateLikeStrategy" />, so the document has to
///     be built from that one rather than from the process-wide default this host never sets.
/// </summary>
public sealed class PerConfigStrategyProvider : IPaginateConfigProvider<Product> {

	private sealed class ILikePreferringStrategy : IPaginateLikeStrategy {

		public PaginateFilterOperator? PreferredExampleOperator => PaginateFilterOperator.ILike;

		public Expression BuildLike(Expression value, Expression pattern) { return Expression.Constant(true); }

	}

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.WithLikeStrategy(new ILikePreferringStrategy())
			.Filterable("name", p => p.Name, PaginateFilterOperator.Eq, PaginateFilterOperator.ILike));
	}

}

/// <summary>A resource with all three of the guards that change what the parameter descriptions say.</summary>
public sealed class GuardedConfigProvider : IPaginateConfigProvider<Product> {

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.WithMaxOffset(5_000)
			.WithMinSearchLength(3)
			.AllowUnlimited(2_000)
			.Searchable("name", p => p.Name));
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
		// Controllers belong in the fixture: the transformer's parameter-removal branch exists for the MVC path,
		// where ApiExplorer expands a bound PaginateQuery into its PascalCase properties, and a Minimal API
		// handler never produces those. AddProblemDetails() is deliberately absent, which also makes every
		// Minimal API operation here the Minimal-API-only shape the 400 schema has to tell the truth about.
		builder.Services.AddControllers().AddApplicationPart(typeof(MvcProductsController).Assembly);
		builder.Services.AddOpenApi(options => options.AddOperationTransformer<PaginatedQueryOperationTransformer>());

		await using var app = builder.Build();

		app.MapOpenApi();
		app.MapControllers();
		app.MapGet("/products", () => Results.Ok()).WithPagination<DocumentedConfigProvider>();
		app.MapGet("/searchless", () => Results.Ok()).WithPagination<SearchlessConfigProvider>();
		app.MapGet("/guarded", () => Results.Ok()).WithPagination<GuardedConfigProvider>();
		app.MapGet("/per-config-strategy", () => Results.Ok()).WithPagination<PerConfigStrategyProvider>();
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

	private string Description(string name) { return this.Description("/products", name); }

	private string Description(string path, string name) {
		return this.Parameters(path).EnumerateArray()
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
	public void A_controller_action_that_binds_the_request_advertises_the_same_parameters() {

		// The generated-parameter removal exists only for this path and nothing exercised it: the unmarked
		// action below shows what ApiExplorer produces for a bound PaginateQuery, and the marked one shows that
		// none of it survives. Rename a PaginateQuery property with no coverage here and the document silently
		// carries both the framework's guess and the real contract.
		Assert.Equal(
			["Page", "Limit", "SortBy", "Search", "SearchBy", "Filters"],
			this.ParameterNames("/mvc/products/unmarked"));

		Assert.Equal(
			["page", "limit", "sortBy", "search", "searchBy", "filter.isFeatured", "filter.status"],
			this.ParameterNames("/mvc/products"));

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

		// Listed in operator order, not in declaration order -- the backing collection is a set and guarantees
		// none, so an arbitrary one would rewrite this line in a consumer's committed document on a rebuild.
		Assert.True(description.IndexOf("$eq", StringComparison.Ordinal) < description.IndexOf("$in", StringComparison.Ordinal));

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

		// The operator comes from the field's own allow-list, so it is one a caller may actually send -- and it
		// is chosen by an explicit rule ($eq when granted, otherwise the lowest operator), not by whichever the
		// set happens to enumerate first. This field declares $in before $eq to keep that honest.
		Assert.StartsWith("$eq:", example);

	}

	[Fact]
	public void A_configurations_own_like_strategy_decides_its_filter_examples() {

		// The process-wide default is never touched by this host, so before the config could carry a strategy the
		// example fell back to the field's first operator -- documenting $eq for a resource that serves ILIKE.
		var parameter = this.Parameters("/per-config-strategy").EnumerateArray()
			.Single(p => p.GetProperty("name").GetString() == "filter.name");

		string example = parameter.GetProperty("schema").GetProperty("items")
			.GetProperty("examples").EnumerateArray().First().GetString()!;

		Assert.StartsWith("$ilike:", example);

	}

	[Fact]
	public void A_filter_parameter_documents_the_value_type() {
		Assert.Contains("Value type", this.Description("filter.status"));
	}

	[Fact]
	public void No_description_carries_a_platform_line_ending() {

		// These descriptions land in a consumer's committed OpenAPI document, which CI regenerates and diffs. A CR
		// makes that artefact's bytes depend on the OS that built it, so it rewrites itself on every build and a bot
		// commits a change nobody made. Two independent paths put one here: Environment.NewLine in the joins that
		// build the field and operator lists, and a CRLF checkout of the transformer reaching its raw string
		// literals, which the compiler copies verbatim. Both are caught by looking for the CR itself.
		// sortBy lists two fields and filter.status two operators, so neither join is skipped for want of a second
		// element -- keep it that way, or this test passes without exercising them.
		string[] offenders = [.. this.Parameters("/products").EnumerateArray()
			.Where(p => p.TryGetProperty("description", out var description) && description.GetString()?.Contains('\r') == true)
			.Select(p => p.GetProperty("name").GetString()!)];

		Assert.Empty(offenders);

	}

	[Fact]
	public void The_validation_failure_response_is_documented() {

		var responses = fixture.Document.GetProperty("paths").GetProperty("/products").GetProperty("get").GetProperty("responses");

		Assert.Contains("invalid", responses.GetProperty("400").GetProperty("description").GetString()!, StringComparison.OrdinalIgnoreCase);

	}

	private string[] ValidationFailureMembers(string path) {

		return [.. fixture.Document.GetProperty("paths").GetProperty(path).GetProperty("get")
			.GetProperty("responses").GetProperty("400")
			.GetProperty("content").GetProperty("application/problem+json")
			.GetProperty("schema").GetProperty("properties")
			.EnumerateObject().Select(property => property.Name)];

	}

	[Fact]
	public void The_validation_failure_schema_documents_what_the_runtime_actually_sends() {

		// This host registers no AddProblemDetails(), so a Minimal API 400 reaches no problem-details writer and
		// carries no traceId. instance is on neither leg: nothing passes one and the framework synthesises none,
		// so a generated model used to carry a property that is always null.
		Assert.Equal(["type", "title", "status", "detail", "code"], this.ValidationFailureMembers("/products"));

	}

	[Fact]
	public void The_controller_leg_documents_the_traceId_its_factory_always_adds() {

		// The controller 400 is built by ProblemDetailsFactory, which the MVC services always bring, so traceId
		// is unconditional there -- the one place the member is honest without AddProblemDetails().
		Assert.Equal(["type", "title", "status", "detail", "code", "traceId"], this.ValidationFailureMembers("/mvc/products"));

	}

	[Fact]
	public void A_resource_with_nothing_searchable_advertises_neither_search_parameter() {

		// Both used to be emitted unconditionally, which is what pushes such a config into
		// IgnoreSearchByInQueryParam() purely to stop the generated documentation offering searchBy.
		Assert.Equal(["page", "limit", "sortBy", "filter.status"], this.ParameterNames("/searchless"));

	}

	[Fact]
	public void A_guarded_resource_documents_its_ceilings() {

		// Each of the three is a property of the resource, so an unguarded one says nothing extra -- which is
		// what the /products assertions above already pin.
		Assert.Contains("At most 5000 rows may be skipped", this.Description("/guarded", "page"), StringComparison.Ordinal);
		Assert.Contains("Send -1 with page=1", this.Description("/guarded", "limit"), StringComparison.Ordinal);
		Assert.Contains("at least 3 characters after trimming", this.Description("/guarded", "search"), StringComparison.Ordinal);

	}

	[Fact]
	public void An_unguarded_resource_says_nothing_about_them() {

		Assert.DoesNotContain("rows may be skipped", this.Description("page"), StringComparison.Ordinal);
		Assert.DoesNotContain("-1", this.Description("limit"), StringComparison.Ordinal);
		Assert.DoesNotContain("at least", this.Description("search"), StringComparison.Ordinal);

	}

}
