using Janzen.Pagination.EntityFrameworkCore.Configuration;
using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;
using Janzen.Pagination.EntityFrameworkCore.Links;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Janzen.Pagination.EntityFrameworkCore;

public static class PaginateQueryableExtensions {

	internal const string AotIncompatibleMessage =
		"Janzen.Pagination builds LINQ expression trees and uses reflection (projection mapping, MakeGenericMethod); it is not compatible with trimming or Native AOT.";

	// AsNoTracking has a `where TEntity : class` constraint that the engine's unconstrained TEntity cannot satisfy,
	// so it is applied reflectively (only on real EF providers) — the map path already does a round-trip, so the
	// one-time reflection cost is negligible.
	private readonly static MethodInfo AsNoTrackingMethod = typeof(EntityFrameworkQueryableExtensions)
		.GetMethods()
		.Single(method => method is { Name: nameof(EntityFrameworkQueryableExtensions.AsNoTracking), IsGenericMethodDefinition: true } && method.GetParameters().Length == 1);

	private static IQueryable<TEntity> ApplyFilters<TEntity>(
		IQueryable<TEntity> query,
		PaginateQuery request,
		PaginateConfig<TEntity> config,
		PaginateExpressionContext context
	) {

		if (request.Filters.Count == 0) return query;

		var entity = Expression.Parameter(typeof(TEntity), "item");

		Expression? aggregate = null;
		int conditionCount = 0;

		foreach ((string fieldName, var values) in request.Filters) {

			if (!config.TryGetFilterableField(fieldName, out var field)) throw new PaginateQueryException($"Filter for field '{fieldName}' is not configured.");

			Expression? fieldExpression = null;

			foreach (string rawValue in values) {

				if (++conditionCount > config.MaxFilterConditions) {
					throw new PaginateQueryException($"Too many filter conditions; at most {config.MaxFilterConditions} are allowed.");
				}

				var criterion = PaginateFilterParser.Parse(fieldName, rawValue);
				var criterionExpression = field.BuildExpression(entity, criterion, context, config.MaxFilterValues);

				fieldExpression = fieldExpression is null
					? criterionExpression
					: criterion.Connector == PaginateFilterConnector.Or
						? Expression.OrElse(fieldExpression, criterionExpression)
						: Expression.AndAlso(fieldExpression, criterionExpression);

			}

			if (fieldExpression is null) continue;

			aggregate = aggregate is null ? fieldExpression : Expression.AndAlso(aggregate, fieldExpression);

		}

		return aggregate is null ? query : query.Where(Expression.Lambda<Func<TEntity, bool>>(aggregate, entity));

	}

	private static IQueryable<TEntity> ApplySearch<TEntity>(
		IQueryable<TEntity> query,
		PaginateQuery request,
		PaginateConfig<TEntity> config,
		PaginateExpressionContext context
	) {

		if (string.IsNullOrWhiteSpace(request.Search)) {
			// No search runs, but a supplied searchBy is still validated: an unknown or repeated field is a client bug
			// either way, and silently ignoring it here is what makes "search does nothing" hard to diagnose.
			if (request.SearchBy.Count > 0) ResolveSearchFields(request, config);

			return query;
		}

		string search = request.Search;

		if (search.Length > config.MaxSearchLength) {
			throw new PaginateQueryException($"Search term must not exceed {config.MaxSearchLength} characters.");
		}

		var fields = ResolveSearchFields(request, config);
		if (fields.Count == 0) throw new PaginateQueryException("Search is not configured for this resource.");

		var entity = Expression.Parameter(typeof(TEntity), "item");

		var aggregate = (from field in fields
			select ParameterReplaceVisitor.Replace(field.Selector.Body, field.Selector.Parameters[0], entity)
			into valueExpression
			let notNull = Expression.NotEqual(valueExpression, Expression.Constant(null, valueExpression.Type))
			let match = context.UseDatabaseFunctions
				? context.LikeStrategy.BuildLike(
					valueExpression,
					PaginateExpressionUtils.ToDatabaseParameter(Expression.Constant($"%{PaginateExpressionUtils.EscapeLikePattern(search)}%")))
				: PaginateExpressionUtils.BuildInMemoryStringMatchExpression(valueExpression, search, false)
			select Expression.AndAlso(notNull, match)).Aggregate<Expression, Expression?>(null, (current, fieldExpression) => current is null
			? fieldExpression
			: Expression.OrElse(current, fieldExpression));

		var predicate = Expression.Lambda<Func<TEntity, bool>>(aggregate!, entity);
		return query.Where(predicate);

	}

	private static IReadOnlyList<PaginateSearchField<TEntity>> ResolveSearchFields<TEntity>(PaginateQuery request, PaginateConfig<TEntity> config) {

		if (config.IgnoreSearchByInQueryParam || request.SearchBy.Count == 0) return config.GetDefaultSearchFields();

		List<PaginateSearchField<TEntity>> fields = [];
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string fieldName in request.SearchBy) {
			if (!config.TryGetSearchableField(fieldName, out var field)) throw new PaginateQueryException($"Search for field '{fieldName}' is not configured.");
			if (!seen.Add(fieldName)) throw new PaginateQueryException($"Search field '{fieldName}' is specified more than once.");

			fields.Add(field);
		}

		return fields;

	}

	private static IQueryable<TEntity> ApplySort<TEntity>(IQueryable<TEntity> query, PaginateQuery request, PaginateConfig<TEntity> config) {

		IReadOnlyList<PaginateSort> sorts;

		if (request.SortBy.Count == 0) {
			// Default sort must not fail when a default field is disabled by When(false) for this caller — skip it.
			sorts = config.DefaultSortBy.Where(sort => config.IsSortEnabled(sort.Field)).ToArray();
		} else {
			if (request.SortBy.Count > config.MaxSortFields) {
				throw new PaginateQueryException($"Too many sort fields; at most {config.MaxSortFields} are allowed.");
			}

			sorts = request.SortBy.Select(PaginateExpressionUtils.ParseSort).ToArray();
		}

		bool first = true;

		foreach (var sort in sorts) {
			if (!config.TryGetSortableField(sort.Field, out var field)) throw new PaginateQueryException($"Sort for field '{sort.Field}' is not configured.");

			query = PaginateExpressionUtils.ApplyOrder(query, field.Selector, sort.Direction == PaginateSortDirection.Desc, first);
			first = false;
		}

		// Append the configured tie-breaker as the final ordering key, so offset paging is deterministic even when the
		// primary sort is absent or non-unique (Skip/Take over an unordered or ambiguous set is non-deterministic).
		if (config.TieBreakerSelector is not null) {
			query = PaginateExpressionUtils.ApplyOrder(query, config.TieBreakerSelector, config.TieBreakerDirection == PaginateSortDirection.Desc, first);
			first = false;
		}

		if (first) {
			throw new PaginateQueryException(
				"Pagination requires a deterministic sort order. Pass 'sortBy', configure DefaultSortBy(...), or add WithTieBreaker(...) to the pagination configuration.");
		}

		return query;

	}

	private static IQueryable<T> AsNoTrackingIfSupported<T>(IQueryable<T> query) {
		return query.Provider is IAsyncQueryProvider
			? (IQueryable<T>)AsNoTrackingMethod.MakeGenericMethod(typeof(T)).Invoke(null, [query])!
			: query;
	}

	private static Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct) {
		if (query.Provider is IAsyncQueryProvider) return query.CountAsync(ct);

		ct.ThrowIfCancellationRequested();
		return Task.FromResult(query.Count());
	}

	private static Task<T[]> ToArrayAsync<T>(IQueryable<T> query, CancellationToken ct) {
		if (query.Provider is IAsyncQueryProvider) return query.ToArrayAsync(ct);

		ct.ThrowIfCancellationRequested();
		return Task.FromResult(query.ToArray());
	}

	extension<TEntity>(IQueryable<TEntity> source) {

		/// <summary>
		///     Paginates and projects each row to <typeparamref name="TResult" /> in SQL using an automatically built
		///     projection (entity → DTO). Use when the response is directly buildable: scalars, single nested objects,
		///     Instant→DateTimeOffset.
		/// </summary>
		[RequiresUnreferencedCode(AotIncompatibleMessage)]
		[RequiresDynamicCode(AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateAsync<TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			PaginateLinkContext? linkContext = null,
			CancellationToken ct = default
		) {
			var selector = PaginateProjectionBuilder.Build<TEntity, TResult>();
			return source.PaginateCoreAsync(request, config, (query, token) => ToArrayAsync(query.Select(selector), token), linkContext, ct);
		}

		/// <summary>
		///     Paginates and projects each row to <typeparamref name="TResult" /> using the supplied
		///     <paramref name="selector" /> as the query's <b>terminal</b> projection. Use for shapes the automatic
		///     builder cannot generate — aggregates (e.g. <c>Count</c>) and one-to-many <b>sub-collection</b>
		///     projections.
		/// </summary>
		/// <remarks>
		///     The selector is the outermost <c>Select</c>, so EF Core may evaluate non-translatable leaves of it in
		///     the shaper (client-side, over the page rows only) while everything else runs in SQL. In practice this
		///     means a single selector can freely mix sub-collections with inexpensive CLR reinterpreting such as NodaTime
		///     <c>Instant.ToDateTimeOffset()</c> (and the nullable path) — <b>including inside sub-collection items</b> —
		///     and still execute as <b>one</b> query whose <c>SELECT</c> contains only the referenced columns (unused
		///     columns, e.g., a large <c>jsonb</c>, stay out). Prefer this over <c>PaginateMapAsync</c> for such
		///     shapes: it avoids materializing the full entity.
		/// </remarks>
		[RequiresUnreferencedCode(AotIncompatibleMessage)]
		[RequiresDynamicCode(AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateAsync<TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			Expression<Func<TEntity, TResult>> selector,
			PaginateLinkContext? linkContext = null,
			CancellationToken ct = default
		) {
			ArgumentNullException.ThrowIfNull(selector);
			return source.PaginateCoreAsync(request, config, (query, token) => ToArrayAsync(query.Select(selector), token), linkContext, ct);
		}

		/// <summary>
		///     Paginates, SQL-projects each row to an intermediate <typeparamref name="TProjection" /> via
		///     <paramref name="selector" />, then applies <paramref name="postMap" /> in memory over the page to
		///     produce <typeparamref name="TResult" />. Use when most of the row is SQL-translatable but a field or two
		///     needs a computation EF cannot translate (e.g. a weighted aggregate over a sub-collection with a guard or
		///     rounding): project the flat fields plus the raw ingredients, then finish them in <paramref name="postMap" />.
		/// </summary>
		/// <remarks>
		///     The <c>SELECT</c> stays as narrow as the <paramref name="selector" /> (no full-entity materialization);
		///     <paramref name="postMap" /> runs only over the current page (O(page size)). Prefer the plain
		///     <c>selector</c> overload when the whole row translates, and <c>PaginateMapAsync</c> only when the response
		///     genuinely needs the loaded entity.
		/// </remarks>
		[RequiresUnreferencedCode(AotIncompatibleMessage)]
		[RequiresDynamicCode(AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateAsync<TProjection, TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			Expression<Func<TEntity, TProjection>> selector,
			Func<TProjection, TResult> postMap,
			PaginateLinkContext? linkContext = null,
			CancellationToken ct = default
		) {
			ArgumentNullException.ThrowIfNull(selector);
			ArgumentNullException.ThrowIfNull(postMap);
			return source.PaginateCoreAsync(request, config,
				async (query, token) => (await ToArrayAsync(query.Select(selector), token).ConfigureAwait(false)).Select(postMap).ToArray(),
				linkContext, ct);
		}

		/// <summary>
		///     Paginates, then maps the <b>fully materialized</b> page entities in memory using
		///     <paramref name="projector" />. Use only when the response genuinely needs the loaded entity — computed
		///     fields or logic that cannot be expressed in a query at all.
		/// </summary>
		/// <remarks>
		///     This materializes every column of each entity (it over-fetches by design). A projection that merely
		///     combines sub-collections with NodaTime conversions does <b>not</b> need this — use the
		///     <c>selector</c> overload of <c>PaginateAsync</c>, which keeps the <c>SELECT</c>
		///     narrow and applies such conversions in the shaper.
		/// </remarks>
		[RequiresUnreferencedCode(AotIncompatibleMessage)]
		[RequiresDynamicCode(AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateMapAsync<TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			Func<TEntity, TResult> projector,
			PaginateLinkContext? linkContext = null,
			CancellationToken ct = default
		) {
			ArgumentNullException.ThrowIfNull(projector);
			return source.PaginateCoreAsync(request, config, async (query, token) => {
				// Read-only list path: do not track the materialized entities (avoids change-tracker pollution + snapshots).
				var entities = await ToArrayAsync(AsNoTrackingIfSupported(query), token).ConfigureAwait(false);
				return entities.Select(projector).ToArray();
			}, linkContext, ct);
		}

		private async Task<PaginatedResponse<TResult>> PaginateCoreAsync<TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			Func<IQueryable<TEntity>, CancellationToken, Task<TResult[]>> project,
			PaginateLinkContext? linkContext,
			CancellationToken ct
		) {

			ArgumentNullException.ThrowIfNull(source);
			ArgumentNullException.ThrowIfNull(request);
			ArgumentNullException.ThrowIfNull(config);

			request.EnsureValid();

			// Mirrors the 'limit' guard: an out-of-range page is a caller bug, so surface it instead of clamping it away.
			if (request.Page < PaginateQuery.DefaultPage) throw new PaginateQueryException("Query parameter 'page' must be a positive integer.");

			int page = request.Page;
			int limit = PaginateExpressionUtils.ParseLimit(request, config);
			bool useDatabaseFunctions = source.Provider is IAsyncQueryProvider;
			var context = new PaginateExpressionContext(useDatabaseFunctions, PaginateLikeDefaults.Strategy);

			var query = ApplyFilters(source, request, config, context);
			query = ApplySearch(query, request, config, context);

			int totalItems = await CountAsync(query, ct).ConfigureAwait(false);
			int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)limit);

			// Use long arithmetic so a very large page cannot overflow; skip past the last row short-circuits to an empty page.
			long skip = (long)(page - 1) * limit;

			TResult[] items;

			if (skip >= totalItems) {
				items = [];
			} else {
				query = ApplySort(query, request, config);
				items = await project(query.Skip((int)skip).Take(limit), ct).ConfigureAwait(false);
			}

			var meta = new PaginatedMeta(totalItems, items.Length, limit, totalPages, page);
			var links = PaginateLinkBuilder.Build(linkContext, page, totalPages);

			return new PaginatedResponse<TResult>(items, meta, links);

		}

	}

}
