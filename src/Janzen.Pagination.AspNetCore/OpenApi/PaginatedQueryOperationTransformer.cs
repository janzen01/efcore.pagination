using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using System.Collections.Frozen;
using System.Globalization;
using System.Net;
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

	/// <summary>
	///     Rewrites one operation: a no-op unless the endpoint carries <see cref="PaginatedQueryAttribute" />, otherwise
	///     it drops the generated <see cref="PaginateQuery" /> parameters and adds documented <c>page</c>, <c>limit</c>,
	///     <c>sortBy</c>, one <c>filter.&lt;field&gt;</c> per filterable field, and a <c>400</c> Problem Details
	///     response. <c>search</c> follows only when the config declares a <c>Searchable</c> field, and <c>searchBy</c>
	///     additionally requires <see cref="IPaginateConfig.IgnoreSearchByInQueryParam" /> to be unset — the engine
	///     ignores it otherwise.
	/// </summary>
	public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {

		var attribute = context.Description.ActionDescriptor.EndpointMetadata
			.OfType<PaginatedQueryAttribute>()
			.FirstOrDefault();

		if (attribute is null) return Task.CompletedTask;

		var provider = (IPaginateConfigProvider)ActivatorUtilities.CreateInstance(context.ApplicationServices, attribute.ConfigProviderType);
		var config = provider.GetConfig();

		operation.Parameters ??= [];
		RemoveGeneratedPaginateParameters(operation.Parameters);

		operation.Parameters.Add(CreatePageParameter());
		operation.Parameters.Add(CreateLimitParameter(config));
		operation.Parameters.Add(CreateSortByParameter(config));
		// A resource with no Searchable field has no free-text surface at all, so neither parameter belongs on it:
		// `search` would document an input whose only possible answer is a 400, and `searchBy` one with nothing to
		// narrow. Advertising them is what pushes a config into IgnoreSearchByInQueryParam() just to hide them.
		if (config.SearchableFields.Count > 0) {
			operation.Parameters.Add(CreateSearchParameter());

			// searchBy is ignored at runtime when the resource opts out, so it must not be advertised.
			if (!config.IgnoreSearchByInQueryParam) {
				operation.Parameters.Add(CreateSearchByParameter(config));
			}
		}

		foreach (var field in config.FilterableFields.OrderBy(field => field.Name, StringComparer.Ordinal)) {
			operation.Parameters.Add(CreateFilterParameter(field, PaginateLikeDefaults.Strategy));
		}

		AddValidationErrorResponse(operation);

		return Task.CompletedTask;

	}

	// Invalid pagination input is translated to a 400 ProblemDetails by PaginateExceptionFilter, so advertise it.
	private static void AddValidationErrorResponse(OpenApiOperation operation) {

		operation.Responses ??= new OpenApiResponses();

		if (operation.Responses.ContainsKey("400")) return;

		operation.Responses["400"] = new OpenApiResponse {
			Description = "The pagination query parameters were invalid.",
			Content = new Dictionary<string, OpenApiMediaType> {
				["application/problem+json"] = new OpenApiMediaType {
					Schema = new OpenApiSchema {
						Type = JsonSchemaType.Object,
						Properties = new Dictionary<string, IOpenApiSchema> {
							["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri" },
							["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
							["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
							["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
							["instance"] = new OpenApiSchema { Type = JsonSchemaType.String },
							// ProblemDetailsFactory adds traceId to every payload it builds, which both pipelines now
							// go through — documenting only the standard members would understate what clients receive.
							["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String }
						}
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

	private static OpenApiParameter CreatePageParameter() {
		return new OpenApiParameter {
			Name = PaginateQueryParams.Page,
			In = ParameterLocation.Query,
			Description = "Page number to retrieve (1-based). Must be a positive integer; invalid values return 400. Pages past the last page return an empty result set.",
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
		return new OpenApiParameter {
			Name = PaginateQueryParams.Limit,
			In = ParameterLocation.Query,
			Description = $"Number of records per page. Must be between 1 and {config.MaxLimit}; out-of-range values return 400. Defaults to {config.DefaultLimit} when omitted.",
			Required = false,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.Integer,
				Format = "int32",
				Minimum = "1",
				Maximum = config.MaxLimit.ToString(CultureInfo.InvariantCulture),
				Default = JsonValue.Create(config.DefaultLimit)
			}
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
				Default = BuildDefaultSort(config)
			}
		};
	}

	private static OpenApiParameter CreateSearchParameter() {
		return new OpenApiParameter {
			Name = PaginateQueryParams.Search,
			In = ParameterLocation.Query,
			Description = "Search term to filter result values.",
			Required = false,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.String
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

	private static OpenApiParameter CreateFilterParameter(PaginateFilterFieldMetadata field, IPaginateLikeStrategy likeStrategy) {
		string operators = string.Join('\n', BuildOperatorTokens(field).Select(token => $"- `{token}`"));
		var preferred = likeStrategy.PreferredExampleOperator;
		string exampleOperator = preferred.HasValue && field.Operators.Contains(preferred.Value)
			? PaginateFilterParser.GetOperatorToken(preferred.Value)
			: PaginateFilterParser.GetOperatorToken(field.Operators.First());

		return new OpenApiParameter {
			Name = $"{PaginateQueryParams.FilterPrefix}{field.Name}",
			In = ParameterLocation.Query,
			Description = $$"""
			                Filter by `{{field.Name}}`.{{RenderBadge(field.Badge)}}

			                Value type: `{{GetValueTypeName(field.Type)}}`

			                Format: `{{PaginateQueryParams.FilterPrefix}}{{field.Name}}={$not:}OPERATION:VALUE`

			                Available operations:

			                {{operators}}
			                """,
			Required = false,
			Style = ParameterStyle.Form,
			Explode = true,
			Schema = new OpenApiSchema {
				Type = JsonSchemaType.Array,
				Items = new OpenApiSchema {
					Type = JsonSchemaType.String,
					Examples = [JsonValue.Create($"{exampleOperator}:{GetExampleValue(field.Type)}")]
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

		var array = new JsonArray();

		foreach (var sort in config.DefaultSortBy) {
			array.Add($"{sort.Field}:{PaginateExpressionUtils.FormatDirection(sort.Direction)}");
		}

		return array;

	}

	private static string BuildFieldDescription(IEnumerable<PaginateFieldMetadata> fields) {
		return string.Join('\n', fields
			.OrderBy(field => field.Name, StringComparer.Ordinal)
			.Select(field => $"- `{field.Name}` (`{GetValueTypeName(field.Type)}`){RenderBadge(field.Badge)}"));
	}

	private static IEnumerable<string> BuildOperatorTokens(PaginateFilterFieldMetadata field) {

		foreach (var filterOperator in field.Operators) {
			yield return PaginateFilterParser.GetOperatorToken(filterOperator);
		}

		yield return "$not";
		yield return "$and";
		yield return "$or";

	}

	// NodaTime ships as a separate add-on package, so this assembly holds no reference to it and cannot use
	// typeof(). Resolving the name against the candidate's *own* assembly keeps these type-identity checks: a
	// same-named type from any other assembly can never match, and there is nothing to cache or preload.
	private static string GetValueTypeName(Type type) {
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t switch {
			_ when t == typeof(string) => "string",
			_ when t == typeof(Guid) => "uuid",
			_ when t == typeof(bool) => "boolean",
			_ when t == typeof(short) || t == typeof(int) || t == typeof(long) => "integer",
			_ when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => "number",
			_ when t == typeof(DateTimeOffset) || t == typeof(DateTime) => "date-time",
			_ when t == t.Assembly.GetType("NodaTime.Instant") => "date-time (UTC)",
			_ when t == t.Assembly.GetType("NodaTime.LocalDate") => "date",
			_ when t.IsEnum => string.Join(" | ", Enum.GetNames(t)),
			_ => t.Name
		};
	}

	private static string GetExampleValue(Type type) {
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t switch {
			_ when t == typeof(string) => "text",
			_ when t == typeof(Guid) => "00000000-0000-0000-0000-000000000000",
			_ when t == typeof(bool) => "true",
			_ when t == typeof(short) || t == typeof(int) || t == typeof(long) => "42",
			_ when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => "9.99",
			_ when t == typeof(DateTimeOffset) || t == typeof(DateTime) => "2025-01-01T00:00:00Z",
			_ when t == t.Assembly.GetType("NodaTime.Instant") => "2025-01-01T00:00:00Z",
			_ when t == t.Assembly.GetType("NodaTime.LocalDate") => "2025-01-01",
			_ when t.IsEnum => Enum.GetNames(t).FirstOrDefault() ?? "value",
			_ => "value"
		};
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
