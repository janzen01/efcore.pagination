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

/// <summary>
///     The four pagination entry points on <c>IQueryable&lt;TEntity&gt;</c>: <c>PaginateAsync</c>,
///     <c>PaginateSelectAsync</c>, <c>PaginateSelectMapAsync</c> and <c>PaginateMapAsync</c>, each with an optional
///     <see cref="PaginateLinkContext" /> — the ASP.NET Core package mirrors the same four names with an
///     <c>HttpRequest</c> in its place. One per projection strategy, deliberately not overloads of one name, so the
///     call site names the strategy it uses: <c>Select</c> produces the shape in SQL, <c>Map</c> in memory over the
///     page rows. The engine builds expression trees and uses reflection, so all four carry
///     <c>[RequiresUnreferencedCode]</c> / <c>[RequiresDynamicCode]</c> — not trim- or AOT-safe.
/// </summary>
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

	/// <summary>
	///     Applies the search term and reports through <paramref name="searchFields" /> which searchable fields it
	///     actually ran over — the request's <c>searchBy</c>, or every configured field when it was omitted. That
	///     resolution is the half a client cannot perform, so it is what <c>meta.searchBy</c> carries; empty when no
	///     search ran.
	/// </summary>
	private static IQueryable<TEntity> ApplySearch<TEntity>(
		IQueryable<TEntity> query,
		PaginateQuery request,
		PaginateConfig<TEntity> config,
		PaginateExpressionContext context,
		out IReadOnlyList<string> searchFields
	) {

		searchFields = [];

		if (string.IsNullOrWhiteSpace(request.Search)) {
			// No search runs, but a supplied searchBy is still validated: an unknown or repeated field is a client bug
			// either way, and silently ignoring it here is what makes "search does nothing" hard to diagnose.
			if (request.SearchBy.Count > 0) ResolveSearchFields(request, config);

			return query;
		}

		// Trimmed before either guard, so both measure the term that is actually searched for. Without this a
		// three-space-padded "a" satisfied a minimum of 3 and then went looking for the spaces.
		string search = request.Search.Trim();

		if (search.Length > config.MaxSearchLength) {
			throw new PaginateQueryException($"Search term must not exceed {config.MaxSearchLength} characters.");
		}

		if (search.Length < config.MinSearchLength) {
			throw new PaginateQueryException($"Search term must be at least {config.MinSearchLength} characters.");
		}

		var fields = ResolveSearchFields(request, config);
		if (fields.Count == 0) throw new PaginateQueryException("Search is not configured for this resource.");

		searchFields = fields.Select(field => field.Name).ToArray();

		var entity = Expression.Parameter(typeof(TEntity), "item");

		var aggregate = (from field in fields
			select ParameterReplaceVisitor.Replace(field.Selector.Body, field.Selector.Parameters[0], entity)
			into spliced
			let valueExpression = context.UseDatabaseFunctions ? spliced : PaginateNullSafeRewriter.Rewrite(spliced, entity)
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

	/// <summary>
	///     Validates the sort and resolves it to ordering keys, the tie-breaker appended last, alongside the same
	///     order in wire form for <c>meta.sortBy</c> — canonical field names, tie-breaker excluded. Kept separate from
	///     applying them because the validation belongs <b>before</b> the count: resolution used to live inside the
	///     non-empty branch, so a request that matched nothing — or asked for a page past the end — never had its
	///     <c>sortBy</c> checked at all, and answered 200 to a name that does not exist.
	/// </summary>
	private static (IReadOnlyList<(LambdaExpression Selector, bool Descending)> Keys, IReadOnlyList<string> Tokens) ResolveSorts<TEntity>(
		PaginateQuery request,
		PaginateConfig<TEntity> config
	) {

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

		List<(LambdaExpression Selector, bool Descending)> keys = [];
		List<string> tokens = [];

		foreach (var sort in sorts) {
			if (!config.TryGetSortableField(sort.Field, out var field)) throw new PaginateQueryException($"Sort for field '{sort.Field}' is not configured.");

			keys.Add((field.Selector, sort.Direction == PaginateSortDirection.Desc));
			// The configured name, not the requested spelling: field lookup is case-insensitive, so echoing the
			// request back would report 'COLOR:DESC' for a field the rest of the contract calls 'color'.
			tokens.Add($"{field.Name}:{PaginateExpressionUtils.FormatDirection(sort.Direction)}");
		}

		// Appended last, so offset paging is deterministic even when the primary sort is absent or non-unique
		// (Skip/Take over an unordered or ambiguous set is non-deterministic). Build() requires it, which is what
		// makes this non-null and what deleted the runtime refusal that used to live here: a configuration
		// defect is not a client error, and reporting it as one hid behind clients that happened to send sortBy.
		keys.Add((config.TieBreakerSelector!, config.TieBreakerDirection == PaginateSortDirection.Desc));

		return (keys, tokens);

	}

	private static IQueryable<TEntity> ApplySorts<TEntity>(IQueryable<TEntity> query, IReadOnlyList<(LambdaExpression Selector, bool Descending)> sorts) {

		// The same provider test the filter and search stages make, asked here rather than threaded down from
		// Compose: sorting is resolved separately from the composed query, and one of the two callers has no
		// context object to carry. A sort key crossing a navigation needs the null-safe form on the in-memory
		// leg exactly as a filter does — ordering by a rewritten key puts the missing ones where the provider
		// puts nulls.
		bool useDatabaseFunctions = query.Provider is IAsyncQueryProvider;

		for (int index = 0; index < sorts.Count; index++) {
			var selector = useDatabaseFunctions ? sorts[index].Selector : PaginateNullSafeRewriter.Rewrite(sorts[index].Selector);
			query = PaginateExpressionUtils.ApplyOrder(query, selector, sorts[index].Descending, index == 0);
		}

		return query;

	}

	/// <summary>
	///     The shared front half of every path: validate, resolve the effective limit, then apply filters and search.
	///     <c>PaginateAsync</c>, <c>ApplyPaginateFilters</c> and <c>ApplyPagination</c> all enter here, which is what
	///     keeps "what the composer shows" and "what the engine runs" from drifting apart. The sort is deliberately
	///     <b>not</b> resolved here — <c>ApplyPaginateFilters</c> never reaches ordering, so validating
	///     <c>sortBy</c> for it would reject requests it does not act on.
	/// </summary>
	private static (IQueryable<TEntity> Query, int Limit, string? Search, IReadOnlyList<string> SearchBy) Compose<TEntity>(
		IQueryable<TEntity> source,
		PaginateQuery request,
		PaginateConfig<TEntity> config
	) {

		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(config);

		request.EnsureValid();

		// Mirrors the 'limit' guard: an out-of-range page is a caller bug, so surface it instead of clamping it away.
		if (request.Page < PaginateQuery.DefaultPage) throw new PaginateQueryException("Query parameter 'page' must be a positive integer.");

		int limit = PaginateExpressionUtils.ParseLimit(request, config);

		// Arithmetic only, so it costs nothing and runs before the count: a guarded deep page is refused without
		// the database being asked anything at all. Applied here rather than at the paging stage so it holds for
		// every entry point, the filtered composer included -- which already validates page and limit the same way.
		PaginateExpressionUtils.ValidateOffset(request.Page, limit, config);

		bool useDatabaseFunctions = source.Provider is IAsyncQueryProvider;
		var context = new PaginateExpressionContext(useDatabaseFunctions, PaginateLikeDefaults.Strategy);

		var query = ApplyFilters(source, request, config, context);
		query = ApplySearch(query, request, config, context, out var searchBy);

		// A whitespace-only term runs no search, so it is reported as absent rather than echoed back as applied.
		// The echo is trimmed for the same reason the guards are: it reports what ran.
		string? search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

		return (query, limit, search, searchBy);

	}

	/// <summary>
	///     How many pages a caller may actually request, which is <paramref name="totalPages" /> unless the
	///     configuration caps the offset. Only the navigation uses it; <c>meta.totalPages</c> keeps reporting what
	///     the data holds, so a client can still see there is more of it than paging will reach.
	/// </summary>
	private static int NavigablePages(int totalPages, int limit, IPaginateConfig config) {

		// An unlimited read is one page and never skips, so the offset ceiling has nothing to say about it.
		if (limit == PaginateQuery.UnlimitedLimit || config.MaxOffset is not { } maxOffset) return totalPages;

		// Long arithmetic for the same reason ApplyCeiling needs it: at limit 1 a ceiling of int.MaxValue makes
		// the + 1 wrap to int.MinValue, which then wins the Math.Min -- so a ceiling meaning "no practical cap"
		// clamped navigation to one page rather than leaving it alone.
		return (int)Math.Min(totalPages, ((long)maxOffset / limit) + 1);

	}

	/// <summary>
	///     Bounds an unlimited read at <c>maxRows + 1</c> rows — one past the ceiling, so "exactly at the limit" and
	///     "over it" are distinguishable. A no-op for an ordinary paged request, which <c>ApplyPage</c> bounds.
	/// </summary>
	private static IQueryable<TEntity> ApplyCeiling<TEntity>(IQueryable<TEntity> query, int limit, IPaginateConfig config) {

		if (limit != PaginateQuery.UnlimitedLimit) return query;

		// Long arithmetic before the clamp: maxRows + 1 overflows for a ceiling at int.MaxValue, and Take with a
		// negative count returns nothing -- so an unlimited read would have answered zero rows and a zero count,
		// silently, with no exception anywhere. Clamped, the fetch is simply unbounded in practice and the
		// items.Length > maxRows check can never fire, which is the honest reading of that ceiling.
		int take = (int)Math.Min((long)config.UnlimitedMaxRows!.Value + 1, int.MaxValue);

		return query.Take(take);

	}

	private static IQueryable<TEntity> ApplyPage<TEntity>(IQueryable<TEntity> query, int page, int limit) {

		// Unlimited: one page holding everything. ValidateOffset has already refused any page but the first, and
		// the caller has bounded the read with Take(maxRows + 1) instead.
		if (limit == PaginateQuery.UnlimitedLimit) return query;

		// Long arithmetic so a very large page cannot overflow the int that Skip takes. PaginateCoreAsync never
		// reaches the cast — its skip >= totalItems short-circuit runs first, so skip is below totalItems and
		// therefore below int.MaxValue — but ApplyPagination has no count to guard it with.
		// Saturating, so a request whose page × limit exceeds int.MaxValue composes OFFSET int.MaxValue
		// rather than the true arithmetic. Both land past the end of any real table, so the rows returned are the
		// same; only the printed SQL differs from the request. Widen if a provider ever stores that many rows.
		long skip = (long)(page - 1) * limit;
		return query.Skip((int)Math.Min(skip, int.MaxValue)).Take(limit);
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
		public Task<PaginatedResponse<TResult>> PaginateSelectAsync<TResult>(PaginateQuery request,
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
		///     <paramref name="postMap" /> runs only over the current page (O(page size)). Prefer
		///     <c>PaginateSelectAsync</c> when the whole row translates, and <c>PaginateMapAsync</c> only when the
		///     response genuinely needs the loaded entity.
		/// </remarks>
		[RequiresUnreferencedCode(AotIncompatibleMessage)]
		[RequiresDynamicCode(AotIncompatibleMessage)]
		public Task<PaginatedResponse<TResult>> PaginateSelectMapAsync<TProjection, TResult>(PaginateQuery request,
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
		///     combines sub-collections with NodaTime conversions does <b>not</b> need this — use
		///     <c>PaginateSelectAsync</c>, which keeps the <c>SELECT</c> narrow and applies such conversions in the
		///     shaper.
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

		/// <summary>
		///     Applies the request's filters and search to the queryable and hands it back <b>unexecuted</b>, so a
		///     caller can compute something over the matching set that is not a page of it: facet counts, a
		///     <c>Sum</c>, a full export. Without this the only way to do that was to re-implement the filter
		///     translation by hand and hope the two stayed in step.
		/// </summary>
		/// <remarks>
		///     Ordering, <c>Skip</c>/<c>Take</c>, the count and the projection are <b>not</b> applied — use
		///     <c>ApplyPagination</c> for the page itself. Validation matches the real pipeline for the stages this
		///     runs, so <paramref name="request" />'s <c>page</c>, <c>limit</c>, filters, <c>searchBy</c> <b>and</b>
		///     <c>sortBy</c> are rejected here exactly as <c>PaginateAsync</c> rejects them — the two composers
		///     validate identically. The result's <see cref="PaginateComposedQuery{TEntity}.SortBy" /> reports the
		///     ordering that <i>would</i> apply, even though this query carries none.
		/// </remarks>
		[RequiresUnreferencedCode(AotIncompatibleMessage)]
		[RequiresDynamicCode(AotIncompatibleMessage)]
		public PaginateComposedQuery<TEntity> ApplyPaginateFilters(PaginateQuery request, PaginateConfig<TEntity> config) {

			var (query, limit, search, searchBy) = Compose(source, request, config);

			// Resolved but not applied. It used to be skipped here because ResolveSorts could refuse a config that
			// had nothing to order by -- rejecting a facet count over a request that never wanted an order. With
			// the tie-breaker required at build time that refusal is gone, so validating sortBy costs nothing and
			// the two composers stop disagreeing about what a valid request is.
			var sorts = ResolveSorts(request, config);

			return new PaginateComposedQuery<TEntity>(query, request.Page, limit, sorts.Tokens, search, searchBy, request.Filters);

		}

		/// <summary>
		///     Composes the full page query — filters, search, ordering (tie-breaker included) and
		///     <c>Skip</c>/<c>Take</c> — and hands it back <b>unexecuted</b>, together with the request state the
		///     engine resolved for it. This is the handle to call <c>ToQueryString()</c> on: the SQL it prints is the
		///     SQL <c>PaginateAsync</c> would run for the same request, because both compose through one code path.
		/// </summary>
		/// <remarks>
		///     No count is issued and no projection is added, and unlike <c>PaginateAsync</c> there is no
		///     short-circuit for a page past the last row — the composer describes what would run, it does not
		///     optimize it away. Validation is the complete one, <c>sortBy</c> included. The returned
		///     <see cref="PaginateComposedQuery{TEntity}" /> carries the effective limit, ordering and search fields
		///     for callers assembling their own envelope.
		///     One consequence for <c>limit=-1</c>: the composed query is bounded at <c>maxRows + 1</c>, which is
		///     what <c>PaginateAsync</c> fetches so it can tell "at the ceiling" from "over it" — and the check
		///     itself is something only execution can do. So a caller executing this query is the one holding the
		///     ceiling: expect the extra row, and refuse the read when it arrives. <c>Limit</c> comes back as
		///     <see cref="PaginateQuery.UnlimitedLimit" /> rather than a row count, for the same reason — there are
		///     no items here to count.
		/// </remarks>
		[RequiresUnreferencedCode(AotIncompatibleMessage)]
		[RequiresDynamicCode(AotIncompatibleMessage)]
		public PaginateComposedQuery<TEntity> ApplyPagination(PaginateQuery request, PaginateConfig<TEntity> config) {

			var (query, limit, search, searchBy) = Compose(source, request, config);
			var sorts = ResolveSorts(request, config);

			return new PaginateComposedQuery<TEntity>(
				ApplyPage(ApplyCeiling(ApplySorts(query, sorts.Keys), limit, config), request.Page, limit),
				request.Page,
				limit,
				sorts.Tokens,
				search,
				searchBy,
				request.Filters
			);

		}

		private async Task<PaginatedResponse<TResult>> PaginateCoreAsync<TResult>(PaginateQuery request,
			PaginateConfig<TEntity> config,
			Func<IQueryable<TEntity>, CancellationToken, Task<TResult[]>> project,
			PaginateLinkContext? linkContext,
			CancellationToken ct
		) {

			var (query, limit, search, searchBy) = Compose(source, request, config);

			int page = request.Page;

			// Resolved before the count, so the documented order (page/limit, filters, search, sortBy, then SQL) holds
			// even when the request is answered by the empty short-circuit below and never reaches the database.
			var sorts = ResolveSorts(request, config);

			int totalItems;
			int totalPages;
			TResult[] items;

			if (limit == PaginateQuery.UnlimitedLimit) {

				// No count query: the fetched set is the whole match set, so counting it would ask the database the
				// same question twice. One row past the ceiling is fetched so exceeding it can be told apart from
				// landing exactly on it.
				int maxRows = config.UnlimitedMaxRows!.Value;
				items = await project(ApplyCeiling(ApplySorts(query, sorts.Keys), limit, config), ct).ConfigureAwait(false);

				if (items.Length > maxRows) {
					throw new PaginateQueryException($"The unlimited read is too large: this resource returns at most {maxRows} rows for 'limit=-1'.");
				}

				totalItems = items.Length;
				totalPages = totalItems == 0 ? 0 : 1;

			} else {

				totalItems = await CountAsync(query, ct).ConfigureAwait(false);
				totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)limit);

				// Use long arithmetic so a very large page cannot overflow; skip past the last row short-circuits to an empty page.
				long skip = (long)(page - 1) * limit;

				// Past the last row there is nothing to fetch, so the second query is never issued at all — the count
				// above is the whole cost of asking for a page that does not exist.
				items = skip >= totalItems
					? []
					: await project(ApplyPage(ApplySorts(query, sorts.Keys), page, limit), ct).ConfigureAwait(false);

			}

			// itemsPerPage echoes what the page actually holds rather than the requested -1, which is not a size.
			int itemsPerPage = limit == PaginateQuery.UnlimitedLimit ? items.Length : limit;

			// totalPages stays the honest count of pages the data has; navigation reports the ones a caller may
			// actually ask for. Without this split, a config with WithMaxOffset hands out a 'next' link and a
			// hasNextPage of true for a page it then answers with 400 -- a client paging by following next walks
			// into a hard error instead of the end of the collection.
			int navigablePages = NavigablePages(totalPages, limit, config);

			var meta = new PaginatedMeta(totalItems, items.Length, itemsPerPage, totalPages, page) {
				SortBy          = sorts.Tokens,
				Search          = search,
				SearchBy        = searchBy,
				Filter          = request.Filters,
				HasPreviousPage = page > PaginateQuery.DefaultPage,
				HasNextPage     = page < navigablePages,
			};

			var links = PaginateLinkBuilder.Build(linkContext, page, totalPages, navigablePages);

			return new PaginatedResponse<TResult>(items, meta, links);

		}

	}

}
