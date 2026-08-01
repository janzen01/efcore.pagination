using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

using System.Collections.ObjectModel;
using System.Globalization;

namespace Janzen.Pagination.AspNetCore.ModelBinding;

internal static class PaginateQueryParser {

	public static PaginateQuery FromQuery(IQueryCollection query) {

		Dictionary<string, IReadOnlyList<string>>? filters = null;

		foreach ((string key, var values) in query) {
			if (!key.StartsWith(PaginateQueryParams.FilterPrefix, StringComparison.OrdinalIgnoreCase)) continue;

			string field = key[PaginateQueryParams.FilterPrefix.Length..];
			if (string.IsNullOrWhiteSpace(field)) continue;

			// Match config field lookup (OrdinalIgnoreCase) so case variants of the same field collapse to one
			// entry instead of each counting toward MaxFilterConditions and emitting a duplicate clause.
			filters ??= new Dictionary<string, IReadOnlyList<string>>(query.Count, StringComparer.OrdinalIgnoreCase);
			filters[field] = values.Select(value => value ?? string.Empty).ToArray();
		}

		string? error = null;

		return new PaginateQuery {
			Page = ParsePositiveInt(query[PaginateQueryParams.Page], PaginateQueryParams.Page, PaginateQuery.DefaultPage, ref error) ?? PaginateQuery.DefaultPage,
			Limit = ParsePositiveInt(query[PaginateQueryParams.Limit], PaginateQueryParams.Limit, null, ref error),
			SortBy = ReadValues(query[PaginateQueryParams.SortBy]),
			Search = ReadSingle(query[PaginateQueryParams.Search]),
			SearchBy = ReadValues(query[PaginateQueryParams.SearchBy]),
			Filters = filters is null
				? PaginateQuery.EmptyFilters
				: new ReadOnlyDictionary<string, IReadOnlyList<string>>(filters),
			ValidationError = error
		};

	}

	private static int? ParsePositiveInt(StringValues values, string name, int? fallback, ref string? error) {

		string? value = values.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(value)) return fallback;
		if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0) return parsed;

		error ??= $"Query parameter '{name}' must be a positive integer.";
		return fallback;

	}

	private static string[] ReadValues(StringValues values) { return values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray(); }

	private static string? ReadSingle(StringValues values) {
		string? value = values.FirstOrDefault();
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

}

/// <summary>
///     Model binder that fills a <see cref="PaginateQuery" /> from the request query string, so a controller action can
///     take <c>[FromQuery] PaginateQuery request</c>. Supplied by
///     <see cref="PaginateQueryModelBinderProvider" />, which <c>AddAspNetCore()</c> registers.
/// </summary>
public sealed class PaginateQueryModelBinder : IModelBinder {

	/// <summary>
	///     Reads <c>page</c>, <c>limit</c>, <c>sortBy</c>, <c>search</c>, <c>searchBy</c> and
	///     <c>filter.&lt;field&gt;</c> off the request query string and never reports a binding failure — any other
	///     parameter is ignored by design. A <c>page</c> or <c>limit</c> that is not a positive integer does not fail
	///     binding either — the message is recorded on the bound request and surfaced as a 400 when the query executes.
	/// </summary>
	public Task BindModelAsync(ModelBindingContext bindingContext) {
		ArgumentNullException.ThrowIfNull(bindingContext);
		bindingContext.Result = ModelBindingResult.Success(PaginateQueryParser.FromQuery(bindingContext.HttpContext.Request.Query));
		return Task.CompletedTask;
	}

}

/// <summary>
///     Model-binder provider for <see cref="PaginateQuery" />. <c>AddAspNetCore()</c> inserts it at index 0 of
///     <c>MvcOptions.ModelBinderProviders</c>, so it is consulted before the built-in providers.
/// </summary>
public sealed class PaginateQueryModelBinderProvider : IModelBinderProvider {

	/// <summary>
	///     Resolves a <see cref="PaginateQueryModelBinder" /> when the requested model type is
	///     <see cref="PaginateQuery" />, and <see langword="null" /> for every other type, leaving those to the
	///     remaining providers.
	/// </summary>
	public IModelBinder? GetBinder(ModelBinderProviderContext context) {
		ArgumentNullException.ThrowIfNull(context);
		return context.Metadata.ModelType == typeof(PaginateQuery) ? new PaginateQueryModelBinder() : null;
	}

}
