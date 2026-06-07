using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Janzen.Pagination.AspNetCore.OpenApi;

public sealed class PaginatedQueryOperationTransformer : IOpenApiOperationTransformer {

	private readonly static FrozenSet<string> GeneratedParameterNames = new[] {
		"Page", "Limit", "SortBy", "Search", "SearchBy", "Filters", "page", "limit", "sortBy", "search", "searchBy"
	}.ToFrozenSet(StringComparer.Ordinal);

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
		operation.Parameters.Add(CreateSearchParameter());

		// searchBy is ignored at runtime when the resource opts out, so it must not be advertised.
		if (!config.IgnoreSearchByInQueryParam) {
			operation.Parameters.Add(CreateSearchByParameter(config));
		}

		foreach (var field in config.FilterableFields.OrderBy(field => field.Name, StringComparer.Ordinal)) {
			operation.Parameters.Add(CreateFilterParameter(field, config.LikeStrategy));
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
							["instance"] = new OpenApiSchema { Type = JsonSchemaType.String }
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

			if (GeneratedParameterNames.Contains(parameter.Name) || parameter.Name.StartsWith("filter.", StringComparison.OrdinalIgnoreCase)) {
				parameters.RemoveAt(i);
			}
		}
	}

	private static OpenApiParameter CreatePageParameter() {
		return new OpenApiParameter {
			Name = "page",
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
			Name = "limit",
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
			Name = "sortBy",
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
			Name = "search",
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
			Name = "searchBy",
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
					Enum = config.SearchableFields.Select(field => JsonValue.Create(field.Name)).OfType<JsonNode>().ToArray()
				}
			}
		};
	}

	private static OpenApiParameter CreateFilterParameter(PaginateFilterFieldMetadata field, IPaginateLikeStrategy likeStrategy) {
		string operators = string.Join(Environment.NewLine, BuildOperatorTokens(field).Select(token => $"- `{token}`"));
		var preferred = likeStrategy.PreferredExampleOperator;
		string exampleOperator = preferred.HasValue && field.Operators.Contains(preferred.Value)
			? PaginateFilterParser.GetOperatorToken(preferred.Value)
			: PaginateFilterParser.GetOperatorToken(field.Operators.First());

		return new OpenApiParameter {
			Name = $"filter.{field.Name}",
			In = ParameterLocation.Query,
			Description = $$"""
			                Filter by `{{field.Name}}`.

			                Value type: `{{GetValueTypeName(field.Type)}}`

			                Format: `filter.{{field.Name}}={$not:}OPERATION:VALUE`

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
					Example = JsonValue.Create($"{exampleOperator}:{GetExampleValue(field.Type)}")
				}
			}
		};
	}

	private static JsonNode[] BuildSortEnum(IPaginateConfig config) {
		return config.SortableFields
			.SelectMany(field => new[] {
				JsonValue.Create($"{field.Name}:ASC"), JsonValue.Create($"{field.Name}:DESC")
			})
			.OfType<JsonNode>()
			.ToArray();
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
		return string.Join(Environment.NewLine, fields
			.OrderBy(field => field.Name, StringComparer.Ordinal)
			.Select(field => $"- `{field.Name}` (`{GetValueTypeName(field.Type)}`)"));
	}

	private static IEnumerable<string> BuildOperatorTokens(PaginateFilterFieldMetadata field) {

		foreach (var filterOperator in field.Operators) {
			yield return PaginateFilterParser.GetOperatorToken(filterOperator);
		}

		yield return "$not";
		yield return "$and";
		yield return "$or";

	}

	private static string GetValueTypeName(Type type) {
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t switch {
			_ when t == typeof(string) => "string",
			_ when t == typeof(Guid) => "uuid",
			_ when t == typeof(bool) => "boolean",
			_ when t == typeof(short) || t == typeof(int) || t == typeof(long) => "integer",
			_ when t == typeof(float) || t == typeof(double) || t == typeof(decimal) => "number",
			_ when t == typeof(DateTimeOffset) || t == typeof(DateTime) => "date-time",
			_ when t.FullName == "NodaTime.Instant" => "date-time (UTC)",
			_ when t.FullName == "NodaTime.LocalDate" => "date",
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
			_ when t.FullName == "NodaTime.Instant" => "2025-01-01T00:00:00Z",
			_ when t.FullName == "NodaTime.LocalDate" => "2025-01-01",
			_ when t.IsEnum => Enum.GetNames(t).FirstOrDefault() ?? "value",
			_ => "value"
		};
	}

}
