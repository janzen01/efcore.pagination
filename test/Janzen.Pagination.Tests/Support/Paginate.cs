using Janzen.Pagination.EntityFrameworkCore.Links;

using System.Linq.Expressions;

namespace Janzen.Pagination.Tests.Support;

/// <summary>
///     Thin wrappers over the four entry points. They default the config to <see cref="TestData.Config" /> and
///     pass the test's cancellation token, which is both correct and what keeps xUnit1051 quiet at ~100 call
///     sites instead of one.
/// </summary>
public static class Paginate {

	public static Task<PaginatedResponse<TResult>> PageAsync<TResult>(
		this IQueryable<Product> source,
		PaginateQuery request,
		PaginateConfig<Product>? config = null,
		PaginateLinkContext? linkContext = null
	) {
		return source.PaginateAsync<Product, TResult>(request, config ?? TestData.Config, linkContext, TestContext.Current.CancellationToken);
	}

	public static Task<PaginatedResponse<TResult>> PageSelectAsync<TResult>(
		this IQueryable<Product> source,
		PaginateQuery request,
		Expression<Func<Product, TResult>> selector,
		PaginateConfig<Product>? config = null
	) {
		return source.PaginateSelectAsync(request, config ?? TestData.Config, selector, null, TestContext.Current.CancellationToken);
	}

	public static Task<PaginatedResponse<TResult>> PageSelectMapAsync<TProjection, TResult>(
		this IQueryable<Product> source,
		PaginateQuery request,
		Expression<Func<Product, TProjection>> selector,
		Func<TProjection, TResult> postMap,
		PaginateConfig<Product>? config = null
	) {
		return source.PaginateSelectMapAsync(request, config ?? TestData.Config, selector, postMap, null, TestContext.Current.CancellationToken);
	}

	public static Task<PaginatedResponse<TResult>> PageMapAsync<TResult>(
		this IQueryable<Product> source,
		PaginateQuery request,
		Func<Product, TResult> projector,
		PaginateConfig<Product>? config = null
	) {
		return source.PaginateMapAsync(request, config ?? TestData.Config, projector, null, TestContext.Current.CancellationToken);
	}

}
