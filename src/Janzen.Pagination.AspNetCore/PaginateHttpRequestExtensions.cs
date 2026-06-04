using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Links;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;

using System.Linq.Expressions;

namespace Janzen.Pagination.AspNetCore;

public static class PaginateHttpRequestExtensions {

	/// <summary>Builds a framework-agnostic link context from the current request.</summary>
	private static PaginateLinkContext ToPaginateLinkContext(this HttpRequest request) {

		ArgumentNullException.ThrowIfNull(request);

		var query = new List<KeyValuePair<string, string>>();

		foreach ((string key, var values) in request.Query) {
			query.AddRange(values.Select(value => new KeyValuePair<string, string>(key, value ?? string.Empty)));
		}

		return new PaginateLinkContext(request.Path.ToString(), query);

	}

	// Mirrors the engine's `extension<TEntity>` block so callers keep the single-type-arg ergonomics:
	// query.PaginateAsync<TResult>(request, config, httpRequest) — TEntity is inferred from the receiver.
	extension<TEntity>(IQueryable<TEntity> source) {

		public Task<PaginatedResponse<TResult>> PaginateAsync<TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateAsync<TEntity, TResult>(request, config, httpRequest.ToPaginateLinkContext(), ct);
		}

		public Task<PaginatedResponse<TResult>> PaginateAsync<TResult>(
			PaginateQuery request,
			PaginateConfig<TEntity> config,
			Expression<Func<TEntity, TResult>> selector,
			HttpRequest httpRequest,
			CancellationToken ct = default
		) {
			return source.PaginateAsync(request, config, selector, httpRequest.ToPaginateLinkContext(), ct);
		}

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
