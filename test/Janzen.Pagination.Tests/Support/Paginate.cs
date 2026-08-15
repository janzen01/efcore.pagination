using Janzen.Pagination.EntityFrameworkCore.Links;

using System.Linq.Expressions;

namespace Janzen.Pagination.Tests.Support;

/// <summary>
///     Thin wrappers over the four entry points. They default the config to <see cref="TestData.Config" /> and
///     pass the test's cancellation token, which is both correct and what keeps xUnit1051 quiet at ~100 call
///     sites instead of one.
/// </summary>
public static class Paginate {

	extension(IQueryable<Product> source) {

		public Task<PaginatedResponse<TResult>> PageAsync<TResult>(PaginateQuery request,
			PaginateConfig<Product>? config = null,
			PaginateLinkContext? linkContext = null
		) {
			return source.PaginateAsync<Product, TResult>(request, config ?? TestData.Config, linkContext, TestContext.Current.CancellationToken);
		}

		public Task<PaginatedResponse<TResult>> PageSelectAsync<TResult>(PaginateQuery request,
			Expression<Func<Product, TResult>> selector,
			PaginateConfig<Product>? config = null
		) {
			return source.PaginateSelectAsync(request, config ?? TestData.Config, selector, null, TestContext.Current.CancellationToken);
		}

		public Task<PaginatedResponse<TResult>> PageSelectMapAsync<TProjection, TResult>(PaginateQuery request,
			Expression<Func<Product, TProjection>> selector,
			Func<TProjection, TResult> postMap,
			PaginateConfig<Product>? config = null
		) {
			return source.PaginateSelectMapAsync(request, config ?? TestData.Config, selector, postMap, null, TestContext.Current.CancellationToken);
		}

		public Task<PaginatedResponse<TResult>> PageMapAsync<TResult>(PaginateQuery request,
			Func<Product, TResult> projector,
			PaginateConfig<Product>? config = null
		) {
			return source.PaginateMapAsync(request, config ?? TestData.Config, projector, null, TestContext.Current.CancellationToken);
		}

	}

}
