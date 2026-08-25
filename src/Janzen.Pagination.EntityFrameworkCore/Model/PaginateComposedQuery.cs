namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     The composed query plus the request state the engine actually resolved for it — what
///     <c>ApplyPagination</c> hands back so a caller can execute the query itself (or read its
///     <c>ToQueryString()</c>) and still build an envelope that reports the <b>effective</b> paging, sorting and
///     searching rather than the raw request. Defaults are already applied here: an omitted <c>limit</c> reads as
///     the configured default, an omitted <c>sortBy</c> as the configured <c>DefaultSortBy</c>, an omitted
///     <c>searchBy</c> as every searchable field. The same values reach
///     <see cref="PaginatedMeta" /> on the normal <c>PaginateAsync</c> path, from this very object.
/// </summary>
/// <remarks>
///     A class rather than a record: the collection members and <see cref="Query" /> would make synthesized value
///     equality compare by reference and answer questions it cannot actually answer — the same reason
///     <see cref="PaginateQuery" /> is a class.
/// </remarks>
/// <typeparam name="TEntity">The entity type the query was composed over.</typeparam>
public sealed class PaginateComposedQuery<TEntity> {

	internal PaginateComposedQuery(
		IQueryable<TEntity> query,
		int page,
		int limit,
		IReadOnlyList<string> sortBy,
		string? search,
		IReadOnlyList<string> searchBy,
		IReadOnlyDictionary<string, IReadOnlyList<string>> filter
	) {
		Query    = query;
		Page     = page;
		Limit    = limit;
		SortBy   = sortBy;
		Search   = search;
		SearchBy = searchBy;
		Filter   = filter;
	}

	/// <summary>
	///     The composed queryable: filters, search, ordering (tie-breaker included) and <c>Skip</c>/<c>Take</c>
	///     applied. No count is issued and no projection is added, so this is exactly what
	///     <c>PaginateAsync</c> would run for the page.
	/// </summary>
	public IQueryable<TEntity> Query { get; }

	/// <summary>The 1-based page that was requested. Not clamped, so it can point past the last page — that page is simply empty.</summary>
	public int Page { get; }

	/// <summary>The effective page size: the requested <c>limit</c>, or the configured default when it was omitted.</summary>
	public int Limit { get; }

	/// <summary>
	///     The ordering that was applied, in wire form (<c>"name:DESC"</c>) and in order — the request's
	///     <c>sortBy</c>, or the configured <c>DefaultSortBy</c> when it was omitted. The tie-breaker is not
	///     listed: it is an implementation detail of deterministic paging, not part of the requested order.
	/// </summary>
	public IReadOnlyList<string> SortBy { get; }

	/// <summary>The search term that was applied, or <see langword="null" /> when the request carried none.</summary>
	public string? Search { get; }

	/// <summary>
	///     The searchable fields the term actually ran over — the request's <c>searchBy</c>, or every configured
	///     searchable field when it was omitted. Empty when no search ran.
	/// </summary>
	public IReadOnlyList<string> SearchBy { get; }

	/// <summary>The request's filters, echoed verbatim per field. Every field here passed validation, because an unknown one is a 400 before this object exists.</summary>
	public IReadOnlyDictionary<string, IReadOnlyList<string>> Filter { get; }

}
