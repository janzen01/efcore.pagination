using Janzen.Pagination.AspNetCore.ModelBinding;
using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Links;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Janzen.Pagination.AspNetCore;

public static class PaginateHttpRequestExtensions {

	/// <summary>
	///     Builds a <see cref="PaginateQuery" /> from the request query string. Use in Minimal API handlers that take
	///     <see cref="HttpRequest" /> / <see cref="HttpContext" />; pair with <c>WithPagination&lt;TProvider&gt;()</c>
	///     to attach the OpenAPI metadata and the 400 ProblemDetails endpoint filter.
	/// </summary>
	public static PaginateQuery ToPaginateQuery(this HttpRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		return PaginateQueryParser.FromQuery(request.Query);
	}

	/// <summary>Builds a framework-agnostic link context from the current request.</summary>
	private static PaginateLinkContext ToPaginateLinkContext(this HttpRequest request) {

		ArgumentNullException.ThrowIfNull(request);

		var query = new List<KeyValuePair<string, string>>();

		foreach ((string key, var values) in request.Query) {
			query.AddRange(values.Select(value => new KeyValuePair<string, string>(key, value ?? string.Empty)));
		}

		return new PaginateLinkContext(request.Path.ToString(), query);

	}

	// Mirrors the engine's `extension<TEntity>` block so these read exactly like the core entry points:
	// query.PaginateAsync<TEntity, TResult>(request, config, httpRequest). Explicit type arguments must name the
	// entity first (it is the extension block's type parameter); passing a selector/projector makes them inferable.
	extension<TEntity>(IQueryable<TEntity> source) {

		[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateAsync<TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateAsync<TEntity, TResult>(request, config, httpRequest.ToPaginateLinkContext(), ct);
		}

		[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateAsync<TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			Expression<Func<TEntity, TResult>> selector,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateAsync(request, config, selector, httpRequest.ToPaginateLinkContext(), ct);
		}

		[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateAsync<TProjection, TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			Expression<Func<TEntity, TProjection>> selector,
			Func<TProjection, TResult> postMap,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateAsync(request, config, selector, postMap, httpRequest.ToPaginateLinkContext(), ct);
		}

		[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateMapAsync<TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			Func<TEntity, TResult> projector,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateMapAsync(request, config, projector, httpRequest.ToPaginateLinkContext(), ct);
		}

	}

}
