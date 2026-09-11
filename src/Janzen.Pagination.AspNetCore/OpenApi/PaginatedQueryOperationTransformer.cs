using Janzen.Pagination.AspNetCore.Filters;
using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Janzen.Pagination.AspNetCore.OpenApi;

/// <summary>
///     Documents the pagination query string on every operation marked with <c>[PaginatedQuery&lt;TProvider&gt;]</c> or
///     <c>WithPagination&lt;TProvider&gt;()</c>, generated from that resource's own config so the published parameters
///     cannot drift from what the engine enforces. Register it once with
///     <c>AddOpenApi(options =&gt; options.AddOperationTransformer&lt;PaginatedQueryOperationTransformer&gt;())</c>.
/// </summary>
public sealed class PaginatedQueryOperationTransformer : IOpenApiOperationTransformer {

	// PascalCase entries are the PaginateQuery property names ASP.NET generates by default; camelCase entries are
	// the wire names this package advertises instead.
	private readonly static FrozenSet<string> GeneratedParameterNames = new[] {
		nameof(PaginateQuery.Page), nameof(PaginateQuery.Limit), nameof(PaginateQuery.SortBy),
		nameof(PaginateQuery.Search), nameof(PaginateQuery.SearchBy), nameof(PaginateQuery.Filters),
		PaginateQueryParams.Page, PaginateQueryParams.Limit, PaginateQueryParams.SortBy,
		PaginateQueryParams.Search, PaginateQueryParams.SearchBy
	}.ToFrozenSet(StringComparer.Ordinal);

	// The site's grammar reference calls these modifiers rather than operators, and they are available on every
	// field regardless of its operator set -- so they are a list of their own rather than three entries appended
	// to the field's, where the emitted document offered no way to learn where they go. '\n', not
	// Environment.NewLine: this text lands in a consumer's committed OpenAPI artefact.
	private readonly static string Modifiers = string.Join('\n', new[] { "$not", "$and", "$or" }.Select(token => $"- `{token}`"));

	/// <summary>
	///     Rewrites one operation: a no-op unless the endpoint carries <see cref="PaginatedQueryAttribute" />, otherwise
	///     it drops the generated <see cref="PaginateQuery" /> parameters and adds documented <c>page</c>, <c>limit</c>,
	///     <c>sortBy</c>, one <c>filter.&lt;field&gt;</c> per filterable field, and a <c>400</c> Problem Details
	///     response. <c>search</c> follows only when the config declares a <c>Searchable</c> field, and <c>searchBy</c>
	///     additionally requires <see cref="IPaginateConfig.IgnoreSearchByInQueryParam" /> to be unset — the engine
	///     ignores it otherwise.
	/// </summary>
	public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {

		// The token is part of the contract and the body is not free: one provider construction plus a
		// resolve-by-name walk per filterable field, per operation, per document generation. A client that
		// disconnects mid-generation had no way to stop any of it.
		cancellationToken.ThrowIfCancellationRequested();

		var attribute = context.Description.ActionDescriptor.EndpointMetadata
			.OfType<PaginatedQueryAttribute>()
			.FirstOrDefault();

		if (attribute is null) return Task.CompletedTask;

		var config = this.GetConfig(context.ApplicationServices, attribute.ConfigProviderType);

		operation.Parameters ??= [];
		RemoveGeneratedPaginateParameters(operation.Parameters);

		operation.Parameters.Add(CreatePageParameter(config));
		operation.Parameters.Add(CreateLimitParameter(config));
		operation.Parameters.Add(CreateSortByParameter(config));
		// A resource with no Searchable field has no free-text surface at all, so neither parameter belongs on it:
		// `search` would document an input whose only possible answer is a 400, and `searchBy` one with nothing to
		// narrow. Advertising them is what pushes a config into IgnoreSearchByInQueryParam() just to hide them.
		if (config.SearchableFields.Count > 0) {
			operation.Parameters.Add(CreateSearchParameter(config));

			// searchBy is ignored at runtime when the resource opts out, so it must not be advertised.
			if (!config.IgnoreSearchByInQueryParam) {
				operation.Parameters.Add(CreateSearchByParameter(config));
			}
		}

		// Read once rather than per field: it is loop-invariant, and a configuration carrying its own strategy must
		// be documented with that one rather than with whatever the process-wide static happens to hold.
		var likeStrategy = config.LikeStrategy ?? PaginateLikeDefaults.Strategy;

		foreach (var field in config.FilterableFields.OrderBy(field => field.Name, StringComparer.Ordinal)) {
			operation.Parameters.Add(CreateFilterParameter(config, field, likeStrategy));
		}

		AddValidationErrorResponse(operation, context);

		return Task.CompletedTask;

	}

	// One entry per document generation, holding one config per provider type. ASP.NET Core rebuilds the whole
	// document on every request to the OpenAPI endpoint, and TransformAsync runs once per marked operation, so
	// without this an app with fifty operations sharing one provider type paid fifty constructions -- and fifty
	// config builds, where the provider builds rather than caches -- for one Swagger UI page load.
	//
	// Keyed on the scope rather than held as a plain field because the scope is what "one document" means here:
	// OpenApiDocumentService passes the request's own IServiceProvider, so two documents generating concurrently
	// cannot see each other's entry, and a transformer registered as a shared instance does not carry one
	// document's answer into the next. That matters for When(...), which exists so a config can vary per caller.
	// Weak keys, so a finished scope takes its entry with it.
	private readonly ConditionalWeakTable<IServiceProvider, Dictionary<Type, IPaginateConfig>> _configsPerDocument = new();

	private IPaginateConfig GetConfig(
		IServiceProvider services,
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type providerType) {

		var configs = this._configsPerDocument.GetValue(services, static _ => []);

		if (configs.TryGetValue(providerType, out var cached)) return cached;

		// A provider the consumer registered is the consumer's: ActivatorUtilities constructs outside the
		// container, so a singleton provider's own state -- a prebuilt config, a cache -- was never reached from
		// here. Activation stays the fallback, which is what makes a parameterless provider need no registration.
		var registered = services.GetService(providerType) as IPaginateConfigProvider;
		var provider = registered ?? (IPaginateConfigProvider)ActivatorUtilities.CreateInstance(services, providerType);

		// Only what this code created. `registered is null` is the whole distinction, and it cannot be expressed by
		// a using declaration over `provider`: that would also dispose the container's own instance, which the
		// container owns and every later consumer of it still needs. The expression is null in both of the cases
		// that must not be disposed -- the container's instance, and a provider that is not IDisposable -- and
		// `using` over a null does nothing, which is exactly the old finally's condition read forwards.
		using (registered is null ? provider as IDisposable : null) {
			var config = provider.GetConfig();
			configs[providerType] = config;
			return config;
		}

	}

	// Invalid pagination input is translated to a 400 ProblemDetails by PaginateExceptionFilter on the controller
	// leg and PaginateExceptionEndpointFilter on the Minimal API leg, so advertise it — with the members that leg
	// actually sends and no others. `instance` is on neither: no producer passes one and the framework synthesises
	// none, so documenting it only taught generated clients an always-null member.
	private static void AddValidationErrorResponse(OpenApiOperation operation, OpenApiOperationTransformerContext context) {

		operation.Responses ??= new OpenApiResponses();

		if (operation.Responses.ContainsKey("400")) return;

		var properties = new Dictionary<string, IOpenApiSchema> {
			["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" },
			["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
			["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
			["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
			// Both filters set it from PaginateQueryException.Code, so it is unconditional. Enumerating the
			// members here would freeze the set into every consumer's committed document and break it on the
			// next member added, so it is documented as the string it is.
			[PaginateExceptionFilter.CodeExtension] = new OpenApiSchema {
				Type = JsonSchemaType.String,
				Description = "Machine-readable cause, for branching without matching the 'detail' prose."
			}
		};

		// traceId has two producers and only one of them is unconditional. A controller action gets it from
		// ProblemDetailsFactory, which the MVC services always bring; a Minimal API endpoint gets it from the
		// problem-details writer, which only AddProblemDetails() registers. A Minimal-API-only app is a supported
		// configuration and sends none, so publishing it there was the document promising a member the runtime
		// does not send.
		if (context.Description.ActionDescriptor is ControllerActionDescriptor
			|| context.ApplicationServices.GetService<IProblemDetailsService>() is not null) {
			properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };
		}

		operation.Responses["400"] = new OpenApiResponse {
			Description = "The pagination query parameters were invalid.",
			Content = new Dictionary<string, OpenApiMediaType> {
				[PaginateExceptionFilter.ProblemJson] = new OpenApiMediaType {
					Schema = new OpenApiSchema {
						Type = JsonSchemaType.Object,
						Properties = properties
					}
				}
			}
		};

	}

	private static void RemoveGeneratedPaginateParameters(IList<IOpenApiParameter> parameters) {
		for (int i = parameters.Count - 1; i >= 0; i--) {
			var parameter = parameters[i];
			if (parameter.In != ParameterLocation.Query) continue;
			if (parameter.Name is null) continue;

			if (GeneratedParameterNames.Contains(parameter.Name) || parameter.Name.StartsWith(PaginateQueryParams.FilterPrefix, StringComparison.OrdinalIgnoreCase)) {
				parameters.RemoveAt(i);
			}
		}
	}

	private static OpenApiParameter CreatePageParameter(IPaginateConfig config) {

		// The offset ceiling is a property of the resource, so it belongs in the description rather than in the
		// schema: 'maximum' on page would be wrong, since which page a given offset reaches moves with limit.
		string offset = config.MaxOffset is { } maxOffset
			? $" At most {maxOffset} rows may be skipped, so the deepest reachable page depends on 'limit'; beyond it the request returns 400."
			: string.Empty;

		return new OpenApiParameter {
			Name = PaginateQueryParams.Page,
			In = ParameterLocation.Query,
			Description = $"Page number to retrieve (1-based). Must be a positive integer; invalid values return 400. Pages past the last page return an empty result set.{offset}",
			Required = false,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.Integer,
				Format = "int32",
				Minimum = "1",
				Default = JsonValue.Create(1)
			}
		};
	}

	private static OpenApiParameter CreateLimitParameter(IPaginateConfig config) {

		string unlimited = config.UnlimitedMaxRows is { } maxRows
			? $" Send -1 with page=1 to receive every matching row as one page, up to {maxRows} of them; more than that returns 400."
			: string.Empty;

		string maximum = config.MaxLimit.ToString(CultureInfo.InvariantCulture);

		// A flat "minimum 1" contradicted the sentence above on any resource that opted in, and the artefact is
		// read by validators as well as by renderers: a gateway doing OpenAPI request validation refused -1 at the
		// edge, making AllowUnlimited unreachable over HTTP. Expressing "1..max, or exactly -1" needs a oneOf, and
		// the objection to one was its rendering quality -- so it is emitted only where the resource opted in, and
		// every other resource keeps the single range it always had. Dropping `minimum` instead would widen the
		// schema to 0 and every other negative, which the engine still answers with 400.
		var schema = config.UnlimitedMaxRows is null
			? new OpenApiSchema {
				Type = JsonSchemaType.Integer,
				Format = "int32",
				Minimum = "1",
				Maximum = maximum,
				Default = JsonValue.Create(config.DefaultLimit)
			}
			: new OpenApiSchema {
				Type = JsonSchemaType.Integer,
				Format = "int32",
				Default = JsonValue.Create(config.DefaultLimit),
				OneOf = [
					new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1", Maximum = maximum },
					new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Enum = [JsonValue.Create(PaginateQuery.UnlimitedLimit)] }
				]
			};

		return new OpenApiParameter {
			Name = PaginateQueryParams.Limit,
			In = ParameterLocation.Query,
			Description = $"Number of records per page. Must be between 1 and {config.MaxLimit}; out-of-range values return 400. Defaults to {config.DefaultLimit} when omitted.{unlimited}",
			Required = false,
			Schema = schema
		};
	}

	private static OpenApiParameter CreateSortByParameter(IPaginateConfig config) {
		return new OpenApiParameter {
			Name = PaginateQueryParams.SortBy,
			In = ParameterLocation.Query,
			Description = $"""
			               Parameter to sort by. Repeat this parameter to sort by multiple fields. The URL order defines sort priority.

			               Sortable fields:

			               {BuildFieldDescription(config.SortableFields)}
			               """,
			Required = false,
			Style = ParameterStyle.Form,
			Explode = true,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.Array,
				Items = new OpenApiSchema {
					Type = JsonSchemaType.String,
					Enum = BuildSortEnum(config)
				},
				// Only request-supplied sorts count towards the guard, and the schema describes exactly those --
				// the default below and the configured tie-breaker are not measured against it.
				MaxItems = config.MaxSortFields,
				Default = BuildDefaultSort(config)
			}
		};
	}

	private static OpenApiParameter CreateSearchParameter(IPaginateConfig config) {
		return new OpenApiParameter {
			Name = PaginateQueryParams.Search,
			In = ParameterLocation.Query,
			Description = config.MinSearchLength > 1
				? $"Search term to filter result values. Must be at least {config.MinSearchLength} characters after trimming; shorter terms return 400."
				: "Search term to filter result values.",
			Required = false,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.String,
				// The engine measures the *trimmed* term, which no schema keyword can express, so this is the
				// conservative reading of the same ceiling: a validating gateway turns a padded term away a few
				// characters before the engine would. MinSearchLength stays prose-only for that reason -- as a
				// minLength it would reject padding the engine trims off and then accepts.
				MaxLength = config.MaxSearchLength
			}
		};
	}

	private static OpenApiParameter CreateSearchByParameter(IPaginateConfig config) {
		return new OpenApiParameter {
			Name = PaginateQueryParams.SearchBy,
			In = ParameterLocation.Query,
			Description = $"""
			               List of configured fields to search by term. If omitted, all searchable fields are used.

			               Searchable fields:

			               {BuildFieldDescription(config.SearchableFields)}
			               """,
			Required = false,
			Style = ParameterStyle.Form,
			Explode = true,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.Array,
				Items = new OpenApiSchema {
					Type = JsonSchemaType.String,
					Enum = [.. config.SearchableFields.Select(field => JsonValue.Create(field.Name))]
				}
			}
		};
	}

	private static OpenApiParameter CreateFilterParameter(IPaginateConfig config, PaginateFilterFieldMetadata field, IPaginateLikeStrategy likeStrategy) {
		string operators = string.Join('\n', BuildOperatorTokens(field).Select(token => $"- `{token}`"));
		var value = DescribeValueType(field.Type);
		var preferred = likeStrategy.PreferredExampleOperator;

		// Min(), not First(): Operators is a set and guarantees no enumeration order, so First() made the example
		// depend on the backing collection and on the order the field happened to declare its operators in -- and
		// this example lands in a consumer's committed OpenAPI document, which CI regenerates and diffs. Eq is the
		// lowest member, so the rule reads as "$eq where the field grants it, otherwise its lowest operator".
		// $null is excluded from that choice rather than ranked: it carries no value, so a field granting it
		// alongside anything else is better exemplified by the operator that does. It stays the answer when it is
		// the only operator there is.
		var exampleOperator = preferred.HasValue && field.Operators.Contains(preferred.Value)
			? preferred.Value
			: field.Operators
				.Where(filterOperator => filterOperator != PaginateFilterOperator.Null)
				.DefaultIfEmpty(PaginateFilterOperator.Null)
				.Min();

		string token = PaginateFilterParser.GetOperatorToken(exampleOperator);

		// Not every operator is spelled "$op:one scalar", and rendering them all that way documented requests the
		// engine refuses: "$null:42" is a 400 because $null takes no value, and "$btw:9.99" is a 400 because $btw
		// takes exactly two.
		string example = exampleOperator switch {
			PaginateFilterOperator.Null => token,
			PaginateFilterOperator.Between => $"{token}:{value.Example},{value.Upper ?? value.Example}",
			_ => $"{token}:{value.Example}"
		};

		return new OpenApiParameter {
			Name = $"{PaginateQueryParams.FilterPrefix}{field.Name}",
			In = ParameterLocation.Query,
			Description = $$"""
			                Filter by `{{field.Name}}`.{{RenderBadge(field.Badge)}}

			                Value type: `{{value.Name}}`

			                Format: `{{PaginateQueryParams.FilterPrefix}}{{field.Name}}=[$not:][$and:|$or:]$OPERATION[:VALUE[,VALUE...]]`

			                At most {{config.MaxFilterValues}} comma-separated values in one criterion, and at most {{config.MaxFilterConditions}} filter criteria across the whole request; beyond either the request returns 400.

			                Available operations:

			                {{operators}}

			                Modifiers, available on every field:

			                {{Modifiers}}
			                """,
			Required = false,
			Style = ParameterStyle.Form,
			Explode = true,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.Array,
				Items = new OpenApiSchema {
					Type = JsonSchemaType.String,
					Examples = [JsonValue.Create(example)]
				}
			}
		};
	}

	private static JsonNode[] BuildSortEnum(IPaginateConfig config) {
		return [.. config.SortableFields
			.SelectMany(field => new JsonNode[] {
				JsonValue.Create($"{field.Name}:ASC"), JsonValue.Create($"{field.Name}:DESC")
			})];
	}

	private static JsonArray? BuildDefaultSort(IPaginateConfig config) {

		if (config.DefaultSortBy.Count == 0) return null;

		// Spelled as a JsonNode element rather than array.Add(string): the ICollection<JsonNode?> overload takes a
		// node, while Add<T> boxes an arbitrary T into a JsonValue and is [RequiresUnreferencedCode] for it. Same
		// shape as BuildSortEnum above, and the node is a string either way.
		return [.. config.DefaultSortBy
			.Select(JsonNode (sort) => JsonValue.Create($"{sort.Field}:{PaginateExpressionUtils.FormatDirection(sort.Direction)}"))];

	}

	private static string BuildFieldDescription(IEnumerable<PaginateFieldMetadata> fields) {
		return string.Join('\n', fields
			.OrderBy(field => field.Name, StringComparer.Ordinal)
			.Select(field => $"- `{field.Name}` (`{DescribeValueType(field.Type).Name}`){RenderBadge(field.Badge)}"));
	}

	private static IEnumerable<string> BuildOperatorTokens(PaginateFilterFieldMetadata field) {

		// Ordered for the same reason the example is pinned: a set has no order, so an unordered list would
		// rewrite this bullet list in a consumer's committed document whenever the backing collection changes.
		foreach (var filterOperator in field.Operators.Order()) {
			yield return PaginateFilterParser.GetOperatorToken(filterOperator);
		}

	}

	// One row per documented value type: what the description calls it, an example value its parser accepts, and
	// the upper bound for the two-valued $btw example. Both halves come from one table on purpose -- as two
	// switches over the same domain they drifted by fourteen rows, and the document ended up naming a type
	// precisely beside an example that answered 400. Upper is null where a range over the type is arbitrary
	// rather than meaningful; the single example then serves as both bounds.
	// NodaTime ships as a separate add-on package, so this assembly holds no reference to it and cannot use
	// typeof(). Resolving the name against the candidate's *own* assembly keeps these type-identity checks: a
	// same-named type from any other assembly can never match, and there is nothing to cache or preload.
	private static (string Name, string Example, string? Upper) DescribeValueType(Type type) {

		var t = Nullable.GetUnderlyingType(type) ?? type;

		return t switch {
			_ when t == typeof(string) => ("string", "text", null),
			_ when t == typeof(Guid) => ("uuid", "00000000-0000-0000-0000-000000000000", null),
			_ when t == typeof(bool) => ("boolean", "true", null),
			_ when t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) => ("integer", "42", "99"),
			_ when t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) => ("integer", "42", "99"),
			_ when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => ("number", "9.99", "99.99"),
			_ when t == typeof(DateTimeOffset) || t == typeof(DateTime) => ("date-time", "2025-01-01T00:00:00Z", "2025-12-31T23:59:59Z"),
			_ when t == typeof(DateOnly) => ("date", "2025-01-01", "2025-12-31"),
			_ when t == typeof(TimeOnly) => ("time", "09:00:00", "17:00:00"),
			// The ISO spelling rather than the colon one: both parse, and this is the form that survives a URL
			// without percent-encoded colons, which is what a reader copying the example out of the document does.
			_ when t == typeof(TimeSpan) => ("duration", "PT2H30M", "PT8H"),
			_ when t == typeof(char) => ("character", "a", "z"),
			// Above the probes below on purpose: no NodaTime type is an enum, and this way an enum field does not
			// walk all seven name comparisons before reaching its arm.
			//
			// FullName rather than t.Assembly.GetType("NodaTime.X"): the two answer identically -- GetType resolves
			// within t's own assembly, so it returns t exactly when t's full name is the one asked for -- but
			// Assembly.GetType is [RequiresUnreferencedCode], which made seven IL2026 out of a comparison needing no
			// reflection at all. The package still holds no reference to NodaTime.
			_ when t.IsEnum => DescribeEnum(t),
			_ when t.FullName is "NodaTime.Instant" => ("date-time (UTC)", "2025-01-01T00:00:00Z", "2025-12-31T23:59:59Z"),
			_ when t.FullName is "NodaTime.LocalDate" => ("date", "2025-01-01", "2025-12-31"),
			_ when t.FullName is "NodaTime.LocalDateTime" => ("date-time (local)", "2025-01-01T00:00:00", "2025-12-31T23:59:59"),
			_ when t.FullName is "NodaTime.LocalTime" => ("time", "09:00:00", "17:00:00"),
			// A negative offset, because a literal '+' in a query string decodes to a space: the example is there to
			// be copied into a URL, and "+02:00" would arrive as " 02:00".
			_ when t.FullName is "NodaTime.OffsetDateTime" => ("date-time (offset)", "2025-01-01T00:00:00-05:00", "2025-12-31T23:59:59-05:00"),
			_ when t.FullName is "NodaTime.Duration" => ("duration", "PT2H30M", "PT8H"),
			_ when t.FullName is "NodaTime.YearMonth" => ("year-month", "2025-01", "2025-12"),
			// A consumer-registered type: the parser is the consumer's, so there is no form this package can name.
			_ => (t.Name, "value", null)
		};

	}

	private static (string Name, string Example, string? Upper) DescribeEnum(Type type) {
		string[] members = Enum.GetNames(type);
		return members.Length == 0
			? (type.Name, "value", null)
			: (string.Join(" | ", members), members[0], members[^1]);
	}

	// Renders an optional field badge as a <code> chip appended to the parameter description. The API reference
	// sanitizer (Scalar uses GitHub-flavored Markdown / rehype-sanitize) strips inline style and every class except
	// one matching /^language-/ on <code>. So a badge is a <code> chip carrying that class, and the consumer colors
	// it through the reference UI's custom CSS. ShowBadge guarantees the class starts with "language-". Without a
	// class it's a neutral code chip. Name and class are HTML-encoded so a stray character can't break the markup.
	private static string RenderBadge(PaginateBadge? badge) {
		if (badge is null) return string.Empty;

		string name = WebUtility.HtmlEncode(badge.Name);

		return string.IsNullOrEmpty(badge.CssClass)
			? $" <code>{name}</code>"
			: $" <code class=\"{WebUtility.HtmlEncode(badge.CssClass)}\">{name}</code>";
	}

}
