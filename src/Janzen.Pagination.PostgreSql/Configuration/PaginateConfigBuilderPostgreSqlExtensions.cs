using Janzen.Pagination.PostgreSql.Like;

// Declared in the consumer's existing configuration namespace so `b.UsePostgreSql()` is discoverable
// wherever PaginateConfigBuilder is in scope, without an extra using directive.
namespace Janzen.Pagination.EntityFrameworkCore.Configuration;

public static class PaginateConfigBuilderPostgreSqlExtensions {

	/// <summary>
	///     Uses PostgreSQL's native <c>ILIKE</c> for case-insensitive search and pattern filtering in this
	///     configuration. Apply per resource: <c>PaginateConfig&lt;T&gt;.Create(b =&gt; b.UsePostgreSql()...)</c>.
	/// </summary>
	public static PaginateConfigBuilder<TEntity> UsePostgreSql<TEntity>(this PaginateConfigBuilder<TEntity> builder) {
		ArgumentNullException.ThrowIfNull(builder);

		return builder.UseLikeStrategy(new NpgsqlLikeStrategy());
	}

}
