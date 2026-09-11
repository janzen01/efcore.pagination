using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Like;
using Janzen.Pagination.NodaTime;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Microsoft.OpenApi;

using NodaTime;

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

/// <summary>A resource with every guard that changes what the document says, all on non-default values.</summary>
/// <summary>
///     A string field granting a pattern operator under guards tight enough to bind it: the floor is above the
///     sample value's length and the ceiling is below the padded one, so both halves of the example rule are
///     exercised by the same config.
/// </summary>
/// <summary>A ceiling tighter than the sample value itself, which no padding path reaches.</summary>
public sealed class TightCeilingConfigProvider : IPaginateConfigProvider<Product> {

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.WithGuards(maxSearchLength: 2)
			.Sortable("rank", p => p.Rank)
			.Filterable("name", p => p.Name, PaginateFilterOperator.ILike));
	}

}

public sealed class PatternGuardedConfigProvider : IPaginateConfigProvider<Product> {

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.WithMinSearchLength(5)
			.WithGuards(maxSearchLength: 6)
			.Sortable("rank", p => p.Rank)
			.Filterable("name", p => p.Name, PaginateFilterOperator.ILike));
	}

}

public sealed class GuardedConfigProvider : IPaginateConfigProvider<Product> {

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.WithMaxOffset(5_000)
			.WithMinSearchLength(3)
			.AllowUnlimited(2_000)
			// Every value differs from the engine's own default, so an assertion cannot pass on the default by
			// accident: 100 / 20 / 5 / 256.
			.WithGuards(maxFilterValues: 25, maxFilterConditions: 8, maxSortFields: 3, maxSearchLength: 120)
			.Sortable("rank", p => p.Rank)
			.DefaultSortBy("rank", PaginateSortDirection.Desc)
			.Searchable("name", p => p.Name)
			.Filterable("status", p => p.Status, PaginateFilterOperator.Eq));
	}

}

/// <summary>
///     Registered in DI as one instance, and able to tell that instance apart from a freshly activated one: the
///     two name their filterable field differently, so the emitted document says which was asked.
/// </summary>
public sealed class RegisteredConfigProvider(string fieldName) : IPaginateConfigProvider<Product> {

	// ActivatorUtilities takes the greediest constructor whose parameters it can resolve, and no string is
	// registered, so an activation lands here -- which is the point: "activated" in the document means the
	// container's own instance was bypassed.
	public RegisteredConfigProvider() : this("activated") { }

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.Filterable(fieldName, p => p.Name, PaginateFilterOperator.Eq));
	}

}

/// <summary>Counts what this library does to a consumer type it constructs itself: how often, and whether it disposes.</summary>
public sealed class CountingConfigProvider : IPaginateConfigProvider<Product>, IDisposable {

	public static int Constructions;

	public static int Disposals;

	public CountingConfigProvider() { Interlocked.Increment(ref Constructions); }

	public void Dispose() { Interlocked.Increment(ref Disposals); }

	public PaginateConfig<Product> GetConfig() {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(p => p.Id)
			.Filterable("status", p => p.Status, PaginateFilterOperator.Eq));
	}

}

/// <summary>
///     One property per value type the transformer documents by name, so a config can declare one filterable
///     field for each and the emitted examples can be fed straight back into the engine.
/// </summary>
public sealed class EveryValueType {

	public int Id { get; set; }

	public byte ByteValue { get; set; }

	public sbyte SByteValue { get; set; }

	public short ShortValue { get; set; }

	public ushort UShortValue { get; set; }

	public int IntValue { get; set; }

	public uint UIntValue { get; set; }

	public long LongValue { get; set; }

	public ulong ULongValue { get; set; }

	public float FloatValue { get; set; }

	public double DoubleValue { get; set; }

	public decimal DecimalValue { get; set; }

	public string Text { get; set; } = "";

	public string? OptionalText { get; set; }

	public Guid Uuid { get; set; }

	public bool Flag { get; set; }

	public char Letter { get; set; }

	public ProductStatus Status { get; set; }

	public DateTime Timestamp { get; set; }

	public DateTimeOffset Moment { get; set; }

	public DateOnly Day { get; set; }

	public TimeOnly TimeOfDay { get; set; }

	public TimeSpan Length { get; set; }

	public Instant Instant { get; set; }

	public LocalDate LocalDate { get; set; }

	public LocalDateTime LocalDateTime { get; set; }

	public LocalTime LocalTime { get; set; }

	public OffsetDateTime OffsetDateTime { get; set; }

	public Duration Duration { get; set; }

	public YearMonth YearMonth { get; set; }

}

/// <summary>
///     A resource declaring one filterable field per documented value type, plus the two operator shapes whose
///     example is not a single scalar. Every field takes an explicit operator list, so the emitted example is
///     decided by the operator under test rather than by whatever the type derives.
/// </summary>
public sealed class EveryValueTypeConfigProvider : IPaginateConfigProvider<EveryValueType> {

	// Lazy because NodaTime's registration has to land before the builder runs, and a static field initializer
	// would order itself against the rest of the assembly rather than against Register().
	private readonly static Lazy<PaginateConfig<EveryValueType>> Configuration = new(Build);

	public static PaginateConfig<EveryValueType> Config => Configuration.Value;

	public PaginateConfig<EveryValueType> GetConfig() { return Config; }

	private static PaginateConfig<EveryValueType> Build() {

		PaginateNodaTime.Register();

		return PaginateConfig<EveryValueType>.Create(b => b
			.WithLimits(defaultLimit: 15, maxLimit: 60)
			.WithTieBreaker(x => x.Id)
			.Filterable("byteValue", x => x.ByteValue, PaginateFilterOperator.Eq)
			.Filterable("sbyteValue", x => x.SByteValue, PaginateFilterOperator.Eq)
			.Filterable("shortValue", x => x.ShortValue, PaginateFilterOperator.Eq)
			.Filterable("ushortValue", x => x.UShortValue, PaginateFilterOperator.Eq)
			.Filterable("intValue", x => x.IntValue, PaginateFilterOperator.Eq)
			.Filterable("uintValue", x => x.UIntValue, PaginateFilterOperator.Eq)
			.Filterable("longValue", x => x.LongValue, PaginateFilterOperator.Eq)
			.Filterable("ulongValue", x => x.ULongValue, PaginateFilterOperator.Eq)
			.Filterable("floatValue", x => x.FloatValue, PaginateFilterOperator.Eq)
			.Filterable("doubleValue", x => x.DoubleValue, PaginateFilterOperator.Eq)
			.Filterable("decimalValue", x => x.DecimalValue, PaginateFilterOperator.Eq)
			.Filterable("text", x => x.Text, PaginateFilterOperator.Eq)
			.Filterable("uuid", x => x.Uuid, PaginateFilterOperator.Eq)
			.Filterable("flag", x => x.Flag, PaginateFilterOperator.Eq)
			.Filterable("letter", x => x.Letter, PaginateFilterOperator.Eq)
			.Filterable("status", x => x.Status, PaginateFilterOperator.Eq)
			.Filterable("timestamp", x => x.Timestamp, PaginateFilterOperator.Eq)
			.Filterable("moment", x => x.Moment, PaginateFilterOperator.Eq)
			.Filterable("day", x => x.Day, PaginateFilterOperator.Eq)
			.Filterable("timeOfDay", x => x.TimeOfDay, PaginateFilterOperator.Eq)
			.Filterable("length", x => x.Length, PaginateFilterOperator.Eq)
			.Filterable("instant", x => x.Instant, PaginateFilterOperator.Eq)
			.Filterable("localDate", x => x.LocalDate, PaginateFilterOperator.Eq)
			.Filterable("localDateTime", x => x.LocalDateTime, PaginateFilterOperator.Eq)
			.Filterable("localTime", x => x.LocalTime, PaginateFilterOperator.Eq)
			.Filterable("offsetDateTime", x => x.OffsetDateTime, PaginateFilterOperator.Eq)
			.Filterable("duration", x => x.Duration, PaginateFilterOperator.Eq)
			.Filterable("yearMonth", x => x.YearMonth, PaginateFilterOperator.Eq)
			// The two operators whose example is not "$op:one scalar": $null takes no value at all, and $btw takes
			// exactly two. Each is its field's only operator, so it is the one the example has to be written for.
			.Filterable("nullOnly", x => x.OptionalText, PaginateFilterOperator.Null)
			.Filterable("betweenOnly", x => x.IntValue, PaginateFilterOperator.Between));

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
		// Registered as an instance the container owns, so the transformer has something to find -- and
		// something it must not construct a second copy of.
		builder.Services.AddSingleton(new RegisteredConfigProvider("registered"));
		builder.Services.AddOpenApi(options => options.AddOperationTransformer<PaginatedQueryOperationTransformer>());

		await using var app = builder.Build();

		app.MapOpenApi();
		app.MapControllers();
		app.MapGet("/products", () => Results.Ok()).WithPagination<DocumentedConfigProvider>();
		app.MapGet("/searchless", () => Results.Ok()).WithPagination<SearchlessConfigProvider>();
		app.MapGet("/guarded", () => Results.Ok()).WithPagination<GuardedConfigProvider>();
		app.MapGet("/pattern-guarded", () => Results.Ok()).WithPagination<PatternGuardedConfigProvider>();
		app.MapGet("/tight-ceiling", () => Results.Ok()).WithPagination<TightCeilingConfigProvider>();
		app.MapGet("/per-config-strategy", () => Results.Ok()).WithPagination<PerConfigStrategyProvider>();
		app.MapGet("/every-type", () => Results.Ok()).WithPagination<EveryValueTypeConfigProvider>();
		app.MapGet("/registered", () => Results.Ok()).WithPagination<RegisteredConfigProvider>();
		// Two operations, one provider type: the transformer used to construct it once per operation.
		app.MapGet("/counted-a", () => Results.Ok()).WithPagination<CountingConfigProvider>();
		app.MapGet("/counted-b", () => Results.Ok()).WithPagination<CountingConfigProvider>();
		app.MapGet("/plain", () => Results.Ok());

		await app.StartAsync();

		using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
		// Twice on purpose. The document is regenerated per request, and the transformer memoises a config only
		// for the duration of one document -- never for the process, which would freeze the first document's
		// answer. The construction counts below are what prove the scope of that memo.
		await client.GetStringAsync("/openapi/v1.json");
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

	/// <summary>
	///     The search-length guards bound the three pattern operators as well as `search`, and the document has to
	///     say so — and has to demonstrate a value the engine accepts. Both halves were unasserted: the existing
	///     checks are substring matches that pass whether or not the sentence renders.
	/// </summary>
	[Fact]
	public void A_pattern_operator_is_documented_within_the_guards_it_is_bound_by() {

		string description = this.Description("/pattern-guarded", "filter.name");

		Assert.Contains("must be between 5 and 6 characters", description, StringComparison.Ordinal);

		// The sample is "text": four characters, below the floor of five. Padding repeats it, and the ceiling of
		// six then truncates -- without that, the document advertises `$ilike:texttext`, which is a 400. The
		// example lives on the item schema, not in the prose.
		string sample = this.Parameters("/pattern-guarded").EnumerateArray()
			.Single(parameter => parameter.GetProperty("name").GetString() == "filter.name")
			.GetProperty("schema").GetProperty("items").GetProperty("examples")[0].GetString()!;

		Assert.Equal("$ilike:textte", sample);

	}

	/// <summary>
	///     A ceiling below the sample's own length truncates it even though no padding ran. Without that the
	///     document advertises `$ilike:text` on a resource whose ceiling is two — a 400 the reader would have
	///     to discover by sending it.
	/// </summary>
	[Fact]
	public void A_ceiling_below_the_sample_truncates_it_without_any_padding() {

		string sample = this.Parameters("/tight-ceiling").EnumerateArray()
			.Single(parameter => parameter.GetProperty("name").GetString() == "filter.name")
			.GetProperty("schema").GetProperty("items").GetProperty("examples")[0].GetString()!;

		Assert.Equal("$ilike:te", sample);

	}

	/// <summary>
	///     At the default floor of one the sentence names the ceiling only. "Between 1 and 256" rules nothing out,
	///     and these descriptions land in a consumer's committed OpenAPI artefact, so a sentence carrying no
	///     information is a diff every such repository takes for nothing.
	/// </summary>
	[Fact]
	public void An_unbound_floor_is_not_advertised_as_a_guard() {

		string description = this.Description("/per-config-strategy", "filter.name");

		Assert.Contains("must not exceed", description, StringComparison.Ordinal);
		Assert.DoesNotContain("must be between 1 and", description, StringComparison.Ordinal);

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
	public void The_filter_description_states_the_grammar_the_reference_publishes() {

		// The emitted line read "{$not:}OPERATION:VALUE": braces conventionally mark a *required* placeholder, the
		// two connectives were advertised under "Available operations" with no position in the grammar at all, and
		// a value was made mandatory although $null takes none and $in takes a list.
		string description = this.Description("filter.status");

		Assert.Contains("Format: `filter.status=[$not:][$and:|$or:]$OPERATION[:VALUE[,VALUE...]]`", description, StringComparison.Ordinal);
		Assert.DoesNotContain("{$not:}", description, StringComparison.Ordinal);

		// The site calls them modifiers rather than operators, and says so in the one place it defines the grammar.
		Assert.Contains("Modifiers", description, StringComparison.Ordinal);
		Assert.True(
			description.IndexOf("Available operations", StringComparison.Ordinal) < description.IndexOf("Modifiers", StringComparison.Ordinal),
			"the field's own operators come before the always-available modifiers");

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

	private JsonElement Schema(string path, string name) {
		return this.Parameters(path).EnumerateArray()
			.Single(parameter => parameter.GetProperty("name").GetString() == name)
			.GetProperty("schema");
	}

	[Fact]
	public void The_request_shape_guards_reach_the_schema() {

		// The transformer read five of the nine guards and never these. A client generated from the document
		// happily sent six sortBy values or a 300-character search term and met the ceiling only as a 400 the
		// document had never mentioned -- while the transformer's own summary says the published parameters
		// cannot drift from what the engine enforces.
		Assert.Equal(3, this.Schema("/guarded", "sortBy").GetProperty("maxItems").GetInt32());
		Assert.Equal(120, this.Schema("/guarded", "search").GetProperty("maxLength").GetInt32());

	}

	[Fact]
	public void Only_an_unlimited_resource_admits_minus_one_in_its_limit_schema() {

		// The description said "Send -1 with page=1" while the schema beside it said minimum 1, so a gateway doing
		// request validation against the published document (APIM, Kong, a generated client's range check) refused
		// -1 at the edge and made AllowUnlimited unreachable over HTTP for that deployment.
		var guarded = this.Schema("/guarded", "limit");
		var branches = guarded.GetProperty("oneOf").EnumerateArray().ToArray();

		Assert.Equal(2, branches.Length);
		Assert.Contains(branches, branch => branch.TryGetProperty("maximum", out _));
		Assert.Contains(branches, branch => branch.TryGetProperty("enum", out var members) && members.EnumerateArray().Any(member => member.GetInt32() == -1));
		Assert.False(guarded.TryGetProperty("minimum", out _));

		// A resource that never opted in keeps the single honest range: -1 is a 400 there, so the schema says so.
		var plain = this.Schema("/products", "limit");

		Assert.False(plain.TryGetProperty("oneOf", out _));
		Assert.True(plain.TryGetProperty("minimum", out _));

	}

	[Fact]
	public void The_configured_default_sort_reaches_the_sortBy_schema() {

		// What a caller gets when sortBy is omitted, and the only part of the config the schema states as a
		// value rather than a bound -- so it is also the one place the emitted array is built element by element.
		string?[] applied = [.. this.Schema("/guarded", "sortBy").GetProperty("default").EnumerateArray().Select(entry => entry.GetString())];

		Assert.Equal(["rank:DESC"], applied);

		// A resource with no default says nothing rather than saying "none".
		Assert.False(this.Schema("/products", "sortBy").TryGetProperty("default", out _));

	}

	[Fact]
	public void The_filter_list_and_request_ceilings_reach_the_description() {

		// Neither has a JSON Schema keyword that fits -- one bounds the elements inside a single string value,
		// the other spans parameters -- so they follow the pattern WithMaxOffset already set and go in the prose.
		string description = this.Description("/guarded", "filter.status");

		Assert.Contains("25", description, StringComparison.Ordinal);
		Assert.Contains("8 filter criteria", description, StringComparison.Ordinal);

	}

	[Fact]
	public void An_unguarded_resource_says_nothing_about_them() {

		Assert.DoesNotContain("rows may be skipped", this.Description("page"), StringComparison.Ordinal);
		Assert.DoesNotContain("-1", this.Description("limit"), StringComparison.Ordinal);
		Assert.DoesNotContain("at least", this.Description("search"), StringComparison.Ordinal);

	}

	private (string Name, string Example)[] FilterExamples(string path) {
		return [.. this.Parameters(path).EnumerateArray()
			.Where(parameter => parameter.GetProperty("name").GetString()!.StartsWith("filter.", StringComparison.Ordinal))
			.Select(parameter => (
				parameter.GetProperty("name").GetString()!,
				parameter.GetProperty("schema").GetProperty("items").GetProperty("examples").EnumerateArray().First().GetString()!
			))];
	}

	[Fact]
	public void No_filter_example_falls_back_to_the_placeholder() {

		// The documented type name and the example beside it were produced by two switches enumerating the same
		// domain, and they had drifted by fourteen rows: the description named `date`, `duration`, `character` or
		// a NodaTime type precisely, and the example beside it was the literal "value".
		string[] placeholders = [.. this.FilterExamples("/every-type")
			.Where(entry => entry.Example.EndsWith(":value", StringComparison.Ordinal))
			.Select(entry => entry.Name)];

		Assert.Empty(placeholders);

	}

	[Fact]
	public void A_valueless_or_two_valued_operator_is_exemplified_in_its_own_shape() {

		// "$op:one scalar" is not the shape of every operator, and writing it that way documented requests the
		// engine answers 400: $null takes no value at all and $btw takes exactly two.
		var examples = this.FilterExamples("/every-type").ToDictionary(entry => entry.Name, entry => entry.Example, StringComparer.Ordinal);

		Assert.Equal("$null", examples["filter.nullOnly"]);
		Assert.Equal("$btw:42,99", examples["filter.betweenOnly"]);

	}

	[Fact]
	public void Every_emitted_filter_example_is_a_value_the_engine_accepts() {

		// The property the document is actually claiming: the example beside a parameter is a request that
		// resource answers. Feeding every one back through the engine is what stops the emitted examples and the
		// value grammar drifting apart again -- neither can move without this going red.
		var source = new List<EveryValueType>().AsQueryable();

		string[] refused = [.. this.FilterExamples("/every-type")
			.Select(entry => (entry.Name, entry.Example, Error: Refuses(source, entry.Name, entry.Example)))
			.Where(entry => entry.Error is not null)
			.Select(entry => $"{entry.Name}={entry.Example} -> {entry.Error}")];

		Assert.Empty(refused);

	}

	private static string? Refuses(IQueryable<EveryValueType> source, string parameterName, string example) {
		try {
			source.ApplyPaginateFilters(Query.Filter(parameterName["filter.".Length..], example), EveryValueTypeConfigProvider.Config);
			return null;
		} catch (PaginateQueryException exception) {
			return exception.Message;
		}
	}

	[Fact]
	public void A_provider_the_container_owns_is_the_one_that_is_asked() {

		// ActivatorUtilities constructs outside the container, so a registered provider -- a singleton holding a
		// prebuilt config, a cache, a handle -- was never reached from the OpenAPI path.
		Assert.Contains("filter.registered", this.ParameterNames("/registered"));
		Assert.DoesNotContain("filter.activated", this.ParameterNames("/registered"));

	}

	[Fact]
	public void An_activated_provider_is_built_once_per_document_and_disposed() {

		// Two documents were fetched and the provider type is on two operations, so four constructions before,
		// two now: one per document. The second half of that is as important as the first -- a process-wide cache
		// would say one, and would then be serving the first document's answer for the life of the app.
		Assert.Equal(2, CountingConfigProvider.Constructions);

		// And this library created them, so this library disposes them: the container does not dispose what it
		// did not create, which is exactly what ActivatorUtilities produces.
		Assert.Equal(2, CountingConfigProvider.Disposals);

	}

	[Fact]
	public async Task A_cancelled_document_generation_stops_the_transformer() {

		// The token is part of the IOpenApiOperationTransformer contract and was accepted and ignored, while the
		// body does a DI activation and a reflection walk per filterable field, per operation, per document.
		using var services = new ServiceCollection().BuildServiceProvider();

		var context = new OpenApiOperationTransformerContext {
			DocumentName = "v1",
			Description = new ApiDescription(),
			ApplicationServices = services
		};

		await Assert.ThrowsAsync<OperationCanceledException>(
			() => new PaginatedQueryOperationTransformer().TransformAsync(new OpenApiOperation(), context, new CancellationToken(true)));

	}

}
