using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore;

public static class PaginateQueryableExtensions {

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

			if (!config.TryGetFilterableField(fieldName, out var field)) throw new PaginateQueryException($"Filter '{fieldName}' is not configured.");

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

		if (request.Search.IsNullOrWhiteSpace()) return query;

		string search = request.Search;

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

		var fields = new List<PaginateSearchField<TEntity>>();

		foreach (string fieldName in request.SearchBy) {
			if (!config.TryGetSearchableField(fieldName, out var field)) throw new PaginateQueryException($"Search field '{fieldName}' is not configured.");

			fields.Add(field);
		}

		return fields;

	}

	private static IQueryable<TEntity> ApplySort<TEntity>(IQueryable<TEntity> query, PaginateQuery request, PaginateConfig<TEntity> config) {

		IReadOnlyList<PaginateSort> sorts;

		if (request.SortBy.Count == 0) {
			sorts = config.DefaultSortBy;
		} else {
			if (request.SortBy.Count > config.MaxSortFields) {
				throw new PaginateQueryException($"Too many sort fields; at most {config.MaxSortFields} are allowed.");
			}

			sorts = request.SortBy.Select(PaginateExpressionUtils.ParseSort).ToArray();
		}

		if (sorts.Count == 0) return query;

		bool first = true;

		foreach (var sort in sorts) {
			if (!config.TryGetSortableField(sort.Field, out var field)) throw new PaginateQueryException($"Sort field '{sort.Field}' is not configured.");

			query = PaginateExpressionUtils.ApplyOrder(query, field.Selector, sort.Direction == PaginateSortDirection.Desc, first);
			first = false;
		}

		return query;

	}

	private static Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct) { return query.Provider is IAsyncQueryProvider ? query.CountAsync(ct) : Task.FromResult(query.Count()); }

	private static Task<T[]> ToArrayAsync<T>(IQueryable<T> query, CancellationToken ct) { return query.Provider is IAsyncQueryProvider ? query.ToArrayAsync(ct) : Task.FromResult(query.ToArray()); }

	extension<TEntity>(IQueryable<TEntity> source) {

		/// <summary>
		///     Paginates and projects each row to <typeparamref name="TResult" /> in SQL using an automatically built
		///     projection (entity → DTO). Use when the response is directly buildable: scalars, single nested objects,
		///     Instant→DateTimeOffset.
		/// </summary>
		public Task<PaginatedResponse<TResult>> PaginateAsync<TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			PaginateLinkContext? linkContext = null,
			CancellationToken ct = default
		) {
			var selector = PaginateProjectionBuilder.Build<TEntity, TResult>();
			return source.PaginateCoreAsync(request, config, (query, token) => ToArrayAsync(query.Select(selector), token), linkContext, ct);
		}

		/// <summary>
		///     Paginates and projects each row to <typeparamref name="TResult" /> in SQL using the supplied
		///     <paramref name="selector" />. Use for SQL-translatable projections the automatic builder cannot generate
		///     (aggregates like Count, sub-collection projections) when no in-memory mapping is required.
		/// </summary>
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
		///     Paginates, then maps the materialized page in memory using <paramref name="projector" />. Use only when the
		///     response cannot be produced by a SQL projection (computed fields or collections needing in-memory logic).
		/// </summary>
		public Task<PaginatedResponse<TResult>> PaginateMapAsync<TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			Func<TEntity, TResult> projector,
			PaginateLinkContext? linkContext = null,
			CancellationToken ct = default
		) {
			ArgumentNullException.ThrowIfNull(projector);
			return source.PaginateCoreAsync(request, config, async (query, token) => {
				var entities = await ToArrayAsync(query, token).ConfigureAwait(false);
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

			int page = Math.Max(request.Page, PaginateQuery.DefaultPage);
			int limit = PaginateExpressionUtils.ParseLimit(request, config);
			bool useDatabaseFunctions = source.Provider is IAsyncQueryProvider;
			var context = new PaginateExpressionContext(useDatabaseFunctions, PaginateLike.Strategy);

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
