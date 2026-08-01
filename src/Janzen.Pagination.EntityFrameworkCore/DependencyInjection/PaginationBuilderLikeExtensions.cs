using Janzen.Pagination.EntityFrameworkCore.Like;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

/// <summary>
///     Pattern-match strategy selection on <see cref="IPaginationBuilder" />: <c>UseLikeStrategy</c> assigns
///     <see cref="PaginateLikeDefaults.Strategy" />, a process-wide static, so the choice is not container-scoped —
///     the last call at startup wins for every configuration, and <c>UsePostgreSql()</c> routes through it.
/// </summary>
public static class PaginationBuilderLikeExtensions {

	/// <summary>
	///     Sets the process-wide pattern-match strategy for all pagination queries. Call once inside
	///     <c>AddPagination(...)</c>. Defaults to a portable <c>LIKE</c> when not set.
	/// </summary>
	public static IPaginationBuilder UseLikeStrategy(this IPaginationBuilder builder, IPaginateLikeStrategy strategy) {

		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(strategy);

		PaginateLikeDefaults.Strategy = strategy;

		return builder;

	}

}
