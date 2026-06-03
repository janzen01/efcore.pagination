using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Like;
using Janzen.Pagination.PostgreSql.Like;

namespace Janzen.Pagination.PostgreSql.DependencyInjection;

public static class PaginationBuilderPostgreSqlExtensions {

	/// <summary>Replaces the portable LIKE strategy with PostgreSQL's native ILIKE.</summary>
	public static IPaginationBuilder UsePostgreSql(this IPaginationBuilder builder) {
		ArgumentNullException.ThrowIfNull(builder);
		PaginateLike.Strategy = new NpgsqlLikeStrategy();
		return builder;
	}

}
