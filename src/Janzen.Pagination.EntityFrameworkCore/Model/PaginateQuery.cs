using System.Collections.ObjectModel;

namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     An immutable pagination request. Bind it from a query string (ASP.NET integration) or construct it directly
///     with an object initializer for non-web callers (gRPC, console, tests), e.g.
///     <c>new PaginateQuery { Page = 2, Limit = 25, SortBy = ["name:DESC"] }</c>. Out-of-range values are
///     validated by the engine when the query is executed.
/// </summary>
public sealed class PaginateQuery {

	/// <summary>Page used when the request does not specify one.</summary>
	public const int DefaultPage = 1;

	internal readonly static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyFilters =
		new ReadOnlyDictionary<string, IReadOnlyList<string>>(
			new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

	/// <summary>1-based page number; defaults to <see cref="DefaultPage" />. Non-positive values are rejected on execution.</summary>
	public int Page { get; init; } = DefaultPage;

	/// <summary>Requested page size; <see langword="null" /> uses the configured default. Values outside 1..MaxLimit are rejected on execution.</summary>
	public int? Limit { get; init; }

	/// <summary>Sort instructions in <c>"field:ASC"</c> / <c>"field:DESC"</c> form, applied in order; fields must be configured as sortable.</summary>
	public IReadOnlyList<string> SortBy { get; init; } = [];

	/// <summary>Search term matched against the configured searchable fields.</summary>
	public string? Search { get; init; }

	/// <summary>Subset of searchable fields to search; empty uses the configured defaults. Ignored when the config sets <c>IgnoreSearchByInQueryParam()</c>.</summary>
	public IReadOnlyList<string> SearchBy { get; init; } = [];

	/// <summary>Filter criteria per field; each value uses the <c>"$op:value"</c> form (e.g. <c>"$eq:42"</c>).</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Filters { get; init; } = EmptyFilters;

	/// <summary>
	///     The same request pointed at a different page. Everything else — limit, sort, search and filters — is
	///     carried over, so a caller with no <see cref="Links.PaginateLinkContext" /> (and therefore a
	///     <see langword="null" /> <see cref="PaginatedResponse{T}.Links" />) can derive the next page from
	///     <see cref="PaginatedMeta.CurrentPage" /> and <see cref="PaginatedMeta.TotalPages" /> and hand the
	///     result straight back to the engine.
	/// </summary>
	/// <param name="page">1-based page number. Non-positive values are rejected on execution, not here.</param>
	public PaginateQuery WithPage(int page) => new() {
		Page            = page,
		Limit           = this.Limit,
		SortBy          = this.SortBy,
		Search          = this.Search,
		SearchBy        = this.SearchBy,
		Filters         = this.Filters,
		ValidationError = this.ValidationError,
	};

	/// <summary>Parse-time validation error captured during model binding; surfaced as a 400 on execution.</summary>
	internal string? ValidationError { get; init; }

	/// <summary>Throws the captured parse-time validation error, if any.</summary>
	internal void EnsureValid() {
		if (ValidationError is not null) throw new PaginateQueryException(ValidationError);
	}

}
