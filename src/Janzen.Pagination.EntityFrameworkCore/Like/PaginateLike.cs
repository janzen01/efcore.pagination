namespace Janzen.Pagination.EntityFrameworkCore.Like;

/// <summary>
///     Process-wide pattern-match strategy used by the engine. Defaults to a portable <c>LIKE</c>;
///     <c>UsePostgreSql()</c> swaps in native <c>ILIKE</c>.
/// </summary>
public static class PaginateLike {

	public static IPaginateLikeStrategy Strategy {
		get;
		set => field = value ?? throw new ArgumentNullException(nameof(value));
	} = new PortableLikeStrategy();

}
