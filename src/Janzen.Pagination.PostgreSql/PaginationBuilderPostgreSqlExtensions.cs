using Janzen.Pagination.EntityFrameworkCore;

namespace Janzen.Pagination.PostgreSql;

public static class PaginationBuilderPostgreSqlExtensions
{
    /// <summary>Replaces the portable LIKE strategy with PostgreSQL's native ILIKE.</summary>
    public static IPaginationBuilder UsePostgreSql(this IPaginationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        PaginateLike.Strategy = new NpgsqlLikeStrategy();
        return builder;
    }
}
