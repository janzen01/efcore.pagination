using Janzen.Pagination.AspNetCore.ModelBinding;
using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Links;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Janzen.Pagination.AspNetCore;

/// <summary>
///     The ASP.NET Core bridge between an <see cref="HttpRequest" /> and the engine: <c>ToPaginateQuery()</c> binds a
///     <see cref="PaginateQuery" /> from the query string, and the four <c>Paginate*Async</c> members mirror the core
///     entry points on <see cref="PaginateQueryableExtensions" />, taking the request where the core takes a
///     <see cref="PaginateLinkContext" />, so <see cref="PaginatedResponse{T}.Links" /> comes back populated instead of
///     <see langword="null" />. All four carry <c>[RequiresUnreferencedCode]</c> and <c>[RequiresDynamicCode]</c>: the
///     engine builds expression trees and uses reflection, so it is not trim- or AOT-safe.
/// </summary>
public static class PaginateHttpRequestExtensions {

	extension(HttpRequest request) {

		/// <summary>
		///     Builds a <see cref="PaginateQuery" /> from the request query string. Use in Minimal API handlers that take
		///     <see cref="HttpRequest" /> / <see cref="HttpContext" />; pair with <c>WithPagination&lt;TProvider&gt;()</c>
		///     to attach the OpenAPI metadata and the 400 ProblemDetails endpoint filter.
		/// </summary>
		public PaginateQuery ToPaginateQuery() {
			ArgumentNullException.ThrowIfNull(request);
			return PaginateQueryParser.FromQuery(request.Query);
		}

		/// <summary>Builds a framework-agnostic link context from the current request.</summary>
		private PaginateLinkContext ToPaginateLinkContext() {

			ArgumentNullException.ThrowIfNull(request);

			List<KeyValuePair<string, string>> query = [];

			foreach ((string key, var values) in request.Query) {
				query.AddRange(values.Select(value => new KeyValuePair<string, string>(key, value ?? string.Empty)));
			}

			// The path base belongs in the link: an app mounted under UsePathBase("/api") would otherwise hand clients
			// links that 404. PathString.Add keeps the escaping correct, and the builder emits the result verbatim.
			return new PaginateLinkContext(request.PathBase.Add(request.Path).ToString(), query);

		}

	}

	// Mirrors the engine's `extension<TEntity>` block so these read exactly like the core entry points:
	// query.PaginateAsync<TEntity, TResult>(request, config, httpRequest). Explicit type arguments must name the
	// entity first (it is the extension block's type parameter); passing a selector/projector makes them inferable.
	extension<TEntity>(IQueryable<TEntity> source) {

		/// <summary>
		///     Paginates and projects each row to <typeparamref name="TResult" /> in SQL using an automatically built
		///     projection (entity → DTO). Use when the response is directly buildable: scalars, single nested objects,
		///     Instant→DateTimeOffset. Delegates to <c>PaginateAsync</c> on <see cref="PaginateQueryableExtensions" />
		///     with <paramref name="httpRequest" /> as the link context, so <see cref="PaginatedResponse{T}.Links" />
		///     comes back populated instead of <see langword="null" />. <typeparamref name="TResult" /> is not
		///     inferable here, so both type arguments are written out, the entity first:
		///     <c>PaginateAsync&lt;TEntity, TResult&gt;</c>.
		/// </summary>
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

		/// <summary>
		///     Paginates and projects each row to <typeparamref name="TResult" /> using the supplied
		///     <paramref name="selector" /> as the query's <b>terminal</b> projection. Use for shapes the automatic
		///     builder cannot generate — aggregates (e.g. <c>Count</c>) and one-to-many <b>sub-collection</b>
		///     projections; supplying <paramref name="selector" /> makes both type arguments inferable. Delegates to
		///     <c>PaginateSelectAsync</c> on <see cref="PaginateQueryableExtensions" /> with
		///     <paramref name="httpRequest" /> as the link context, so <see cref="PaginatedResponse{T}.Links" /> comes
		///     back populated instead of <see langword="null" />.
		/// </summary>
		[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateSelectAsync<TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			Expression<Func<TEntity, TResult>> selector,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateSelectAsync(request, config, selector, httpRequest.ToPaginateLinkContext(), ct);
		}

		/// <summary>
		///     Paginates, SQL-projects each row to an intermediate <typeparamref name="TProjection" /> via
		///     <paramref name="selector" />, then applies <paramref name="postMap" /> in memory over the page to produce
		///     <typeparamref name="TResult" />. Use when most of the row is SQL-translatable but a field or two needs a
		///     computation EF cannot translate; supplying <paramref name="selector" /> and <paramref name="postMap" />
		///     makes all three type arguments inferable. Delegates to <c>PaginateSelectMapAsync</c> on
		///     <see cref="PaginateQueryableExtensions" /> with <paramref name="httpRequest" /> as the link context, so
		///     <see cref="PaginatedResponse{T}.Links" /> comes back populated instead of <see langword="null" />.
		/// </summary>
		[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateSelectMapAsync<TProjection, TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			Expression<Func<TEntity, TProjection>> selector,
			Func<TProjection, TResult> postMap,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateSelectMapAsync(request, config, selector, postMap, httpRequest.ToPaginateLinkContext(), ct);
		}

		/// <summary>
		///     Paginates, then maps the <b>fully materialized</b> page entities in memory using
		///     <paramref name="projector" />. Use only when the response genuinely needs the loaded entity — computed
		///     fields or logic that cannot be expressed in a query at all; supplying <paramref name="projector" /> makes
		///     both type arguments inferable. Delegates to <c>PaginateMapAsync</c> on
		///     <see cref="PaginateQueryableExtensions" /> with <paramref name="httpRequest" /> as the link context, so
		///     <see cref="PaginatedResponse{T}.Links" /> comes back populated instead of <see langword="null" />.
		/// </summary>
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
