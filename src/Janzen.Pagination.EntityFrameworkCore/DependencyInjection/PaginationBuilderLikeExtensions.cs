using Janzen.Pagination.EntityFrameworkCore.Like;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

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
