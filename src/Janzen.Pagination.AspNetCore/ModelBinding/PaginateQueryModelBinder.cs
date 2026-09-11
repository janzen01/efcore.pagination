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

		// Held apart from `error` below so the published precedence survives: page and limit are reported
		// before filters, and `error ??=` would otherwise let whichever ran first win.
		string? duplicateFilter = null;

		foreach ((string key, var values) in query) {
			if (!key.StartsWith(PaginateQueryParams.FilterPrefix, StringComparison.OrdinalIgnoreCase)) continue;

			// A field name is an identifier, so padding around one is never meaningful -- sortBy has always
			// trimmed one and filter.<field> used to answer 400 for the same spelling.
			string field = key[PaginateQueryParams.FilterPrefix.Length..].Trim();
			if (field.Length == 0) continue;

			// The comparer matches the config's own field lookup (OrdinalIgnoreCase). Case variants never arrive
			// here separately -- IQueryCollection is itself case-insensitive and has already merged them -- but
			// two differently padded spellings do, and the trim above folds them onto one entry.
			//
			// TryAdd, not an indexer assignment. Before the trim the raw remainders were already unique under
			// this comparer, so an assignment could not overwrite; after it, ?filter.status=$eq:A&filter.%20status=$eq:B
			// collapses to one key and last-wins would drop $eq:A without a word -- turning a request that
			// answered 400 for the unconfigured ' status' into a 200 carrying half of what the caller sent.
			// Silently discarding a criterion is the failure mode this whole change exists to remove.
			filters ??= new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

			if (!filters.TryAdd(field, CopyValues(values))) {
				duplicateFilter ??= $"Filter for field '{field}' is specified more than once.";
			}
		}

		string? error = null;
		var errorCode = PaginateQueryError.Unspecified;

		return new PaginateQuery {
			Page = ParsePositiveInt(FirstNonBlank(query[PaginateQueryParams.Page]), PaginateQueryParams.Page, PaginateQueryError.PageOutOfRange, PaginateQuery.DefaultPage, ref error, ref errorCode) ?? PaginateQuery.DefaultPage,
			Limit = ParseLimit(FirstNonBlank(query[PaginateQueryParams.Limit]), ref error, ref errorCode),
			SortBy = ReadValues(query[PaginateQueryParams.SortBy]),
			Search = FirstNonBlank(query[PaginateQueryParams.Search]),
			SearchBy = ReadValues(query[PaginateQueryParams.SearchBy]),
			Filters = filters is null
				? PaginateQuery.EmptyFilters
				: new ReadOnlyDictionary<string, IReadOnlyList<string>>(filters),
			ValidationError = error ?? duplicateFilter,
			ValidationErrorCode = error is not null ? errorCode
				: duplicateFilter is not null ? PaginateQueryError.DuplicateFilterField
				: PaginateQueryError.Unspecified
		};

	}

	// Derived rather than restated: UnlimitedLimit is still Unshipped, so its value may still move, and a
	// second textual spelling of it would leave the HTTP path matching the old one with nothing failing.
	private readonly static string UnlimitedLiteral = PaginateQuery.UnlimitedLimit.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	///     The first occurrence that carries something. A repeated parameter still takes its <b>first</b> value,
	///     but a blank one is skipped rather than taken: an HTML GET form emits every empty input, so
	///     <c>?page=&amp;page=2</c> used to serve page 1 while <c>?sortBy=&amp;sortBy=name:ASC</c> correctly
	///     skipped. <see cref="ReadValues" /> has always dropped blanks; this is what makes all six inputs agree.
	/// </summary>
	private static string? FirstNonBlank(StringValues values) {

		for (int index = 0; index < values.Count; index++) {
			string? value = values[index];
			if (!string.IsNullOrWhiteSpace(value)) return value;
		}

		return null;

	}

	private static int? ParsePositiveInt(string? value, string name, PaginateQueryError code, int? fallback, ref string? error, ref PaginateQueryError errorCode) {

		if (value is null) return fallback;
		if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0) return parsed;

		// First problem wins, and its code travels with it: the message and the code must describe the same
		// parameter, so neither is assigned without the other.
		if (error is null) {
			error = $"Query parameter '{name}' must be a positive integer.";
			errorCode = code;
		}

		return fallback;

	}

	/// <summary>
	///     As <see cref="ParsePositiveInt" />, plus the one negative value the contract has a meaning for:
	///     <see cref="PaginateQuery.UnlimitedLimit" />. Whether this resource accepts it is the engine's call
	///     — the binder has no configuration — so <c>-1</c> is carried through and answered there, with the
	///     ordinary range message when the resource never opted in.
	/// </summary>
	private static int? ParseLimit(string? value, ref string? error, ref PaginateQueryError errorCode) {

		if (value is null) return null;

		// The literal, matched as text rather than by loosening the number styles. AllowLeadingSign would also
		// have started accepting "+5", which page still rejects -- one contract quietly forking into two.
		if (value == UnlimitedLiteral) return PaginateQuery.UnlimitedLimit;

		return ParsePositiveInt(value, PaginateQueryParams.Limit, PaginateQueryError.LimitOutOfRange, null, ref error, ref errorCode);

	}

	/// <summary>
	///     The multi-valued inputs: blank entries dropped, each surviving entry trimmed. Written as two index
	///     passes over the struct rather than a LINQ pipeline, which boxed <see cref="StringValues" /> and
	///     allocated iterators even for a parameter the request never sent.
	/// </summary>
	private static string[] ReadValues(StringValues values) {

		int count = values.Count;
		if (count == 0) return [];

		int kept = 0;
		for (int index = 0; index < count; index++) {
			if (!string.IsNullOrWhiteSpace(values[index])) kept++;
		}

		if (kept == 0) return [];

		string[] result = new string[kept];
		int next = 0;

		for (int index = 0; index < count; index++) {
			string? value = values[index];
			if (!string.IsNullOrWhiteSpace(value)) result[next++] = value.Trim();
		}

		return result;

	}

	/// <summary>Copies a filter's values verbatim — only the field name is an identifier, the values are not.</summary>
	private static string[] CopyValues(StringValues values) {

		int count = values.Count;
		if (count == 0) return [];

		string[] copied = new string[count];
		for (int index = 0; index < count; index++) copied[index] = values[index] ?? string.Empty;

		return copied;

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
	///     remaining providers. Also <see langword="null" /> when the parameter carries an explicit
	///     <c>[ModelBinder(typeof(…))]</c>, so the framework's own per-parameter override is not shadowed by
	///     sitting at index 0.
	/// </summary>
	/// <remarks>
	///     The binding <b>source</b> is deliberately not consulted. A bare <see cref="PaginateQuery" /> parameter
	///     on an <c>[ApiController]</c> is inferred as <c>BindingSource.Body</c>, indistinguishable from an
	///     explicit <c>[FromBody]</c>, so standing down for it would answer a working
	///     <c>GET /products?page=2</c> with "A non-empty request body is required."
	/// </remarks>
	public IModelBinder? GetBinder(ModelBinderProviderContext context) {

		ArgumentNullException.ThrowIfNull(context);
		if (context.Metadata.ModelType != typeof(PaginateQuery)) return null;

		// A [ModelBinder(typeof(...))] on the parameter is the framework's own per-parameter override. Claiming
		// the parameter anyway made it inert for this type application-wide, because this provider is first.
		return context.BindingInfo.BinderType is null ? new PaginateQueryModelBinder() : null;

	}

}
