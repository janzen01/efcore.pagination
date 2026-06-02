namespace Janzen.Pagination.EntityFrameworkCore;

/// <summary>
///     Process-wide pattern-match strategy used by the engine. Defaults to a portable <c>LIKE</c>;
///     <c>UsePostgreSql()</c> swaps in native <c>ILIKE</c>.
/// </summary>
public static class PaginateLike
{
    private static IPaginateLikeStrategy _strategy = new PortableLikeStrategy();

    public static IPaginateLikeStrategy Strategy
    {
        get => _strategy;
        set => _strategy = value ?? throw new ArgumentNullException(nameof(value));
    }
}
