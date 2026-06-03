using Janzen.Pagination.EntityFrameworkCore;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

using System.Collections.ObjectModel;
using System.Globalization;

namespace Janzen.Pagination.AspNetCore;

internal static class PaginateQueryParser {

	public static PaginateQuery FromQuery(IQueryCollection query) {
		Dictionary<string, IReadOnlyList<string>>? filters = null;

		foreach ((string key, var values) in query) {
			if (!key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase)) continue;

			string field = key["filter.".Length..];
			if (field.IsNullOrWhiteSpace()) continue;

			filters ??= new Dictionary<string, IReadOnlyList<string>>(query.Count, StringComparer.Ordinal);
			filters[field] = values.Select(value => value ?? string.Empty).ToArray();
		}

		string? error = null;

		return new PaginateQuery {
			Page = ParseRequiredPositiveInt(query["page"], "page", PaginateQuery.DefaultPage, ref error),
			Limit = ParseOptionalPositiveInt(query["limit"], "limit", ref error),
			SortBy = ReadValues(query["sortBy"]),
			Search = ReadSingle(query["search"]),
			SearchBy = ReadValues(query["searchBy"]),
			Filters = filters is null
				? PaginateQuery.EmptyFilters
				: new ReadOnlyDictionary<string, IReadOnlyList<string>>(filters),
			ValidationError = error
		};
	}

	private static int ParseRequiredPositiveInt(StringValues values, string name, int fallback, ref string? error) {
		string? value = values.FirstOrDefault();
		if (value.IsNullOrWhiteSpace()) return fallback;
		if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0) return parsed;

		error ??= $"Query parameter '{name}' must be a positive integer.";
		return fallback;
	}

	private static int? ParseOptionalPositiveInt(StringValues values, string name, ref string? error) {
		string? value = values.FirstOrDefault();
		if (value.IsNullOrWhiteSpace()) return null;
		if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0) return parsed;

		error ??= $"Query parameter '{name}' must be a positive integer.";
		return null;
	}

	private static string[] ReadValues(StringValues values) { return values.Where(value => value.IsFilled()).Select(value => value!).ToArray(); }

	private static string? ReadSingle(StringValues values) {
		string? value = values.FirstOrDefault();
		return value.IsNullOrWhiteSpace() ? null : value;
	}

}

public sealed class PaginateQueryModelBinder : IModelBinder {

	public Task BindModelAsync(ModelBindingContext bindingContext) {
		ArgumentNullException.ThrowIfNull(bindingContext);
		bindingContext.Result = ModelBindingResult.Success(PaginateQueryParser.FromQuery(bindingContext.HttpContext.Request.Query));
		return Task.CompletedTask;
	}

}

public sealed class PaginateQueryModelBinderProvider : IModelBinderProvider {

	public IModelBinder? GetBinder(ModelBinderProviderContext context) {
		ArgumentNullException.ThrowIfNull(context);
		return context.Metadata.ModelType == typeof(PaginateQuery) ? new PaginateQueryModelBinder() : null;
	}

}
