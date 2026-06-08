namespace Janzen.Pagination.EntityFrameworkCore.Like;

/// <summary>
///     Process-wide default pattern-match strategy used by every pagination query. Defaults to a portable
///     <c>LIKE</c>; set it once at startup (e.g. <c>AddPagination(p =&gt; p.UsePostgreSql())</c>) to switch all
///     configurations to a provider-specific strategy such as PostgreSQL's native <c>ILIKE</c>.
/// </summary>
/// <remarks>Intended to be assigned once during startup (before requests) and read concurrently thereafter.</remarks>
public static class PaginateLikeDefaults {

	/// <summary>The strategy the engine uses to build case-insensitive pattern matches. Never <see langword="null" />.</summary>
	public static IPaginateLikeStrategy Strategy { get; set; } = new PortableLikeStrategy();

}
