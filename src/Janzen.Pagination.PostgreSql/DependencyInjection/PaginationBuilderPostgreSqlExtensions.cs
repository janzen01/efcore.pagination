using Janzen.Pagination.PostgreSql.Like;

// Declared in the core DI namespace so `p.UsePostgreSql()` is discoverable wherever AddPagination / AddAspNetCore
// are in scope, without an extra using directive.
namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

public static class PaginationBuilderPostgreSqlExtensions {

	/// <summary>
	///     Registers PostgreSQL's native <c>ILIKE</c> as the global pattern-match strategy for all pagination
	///     queries (case-insensitive search and pattern filtering). Call once inside <c>AddPagination(...)</c>.
	/// </summary>
	public static IPaginationBuilder UsePostgreSql(this IPaginationBuilder builder) {

		ArgumentNullException.ThrowIfNull(builder);

		return builder.UseLikeStrategy(new NpgsqlLikeStrategy());

	}

}
