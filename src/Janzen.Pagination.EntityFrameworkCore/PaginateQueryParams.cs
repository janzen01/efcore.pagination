namespace Janzen.Pagination.EntityFrameworkCore;

/// <summary>
///     Wire names of the pagination query-string parameters — the single source shared by the model binder,
///     the link builder and the OpenAPI metadata so the three cannot drift apart.
/// </summary>
internal static class PaginateQueryParams {

	public const string Page = "page";

	public const string Limit = "limit";

	public const string SortBy = "sortBy";

	public const string Search = "search";

	public const string SearchBy = "searchBy";

	/// <summary>Prefix of per-field filter parameters (<c>filter.&lt;field&gt;</c>).</summary>
	public const string FilterPrefix = "filter.";

}
