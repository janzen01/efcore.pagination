using System.Collections;
using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>Ordering: the wire format, the defaults, and the tie-breaker every configuration must declare.</summary>
public sealed class SortingTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>
	///     No sortable field and no default sort — the tie-breaker alone is the ordering. This used to be the
	///     config the engine refused to page; it is now the minimum a valid one can be.
	/// </summary>
	private readonly static PaginateConfig<Product> Unordered = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.Filterable("id", p => p.Id, PaginateFilterOperator.Eq));

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request, config);
	}

	private async Task<string> Rejects(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request, config));
	}

	[Theory]
	[InlineData("rank:DESC")]
	[InlineData("rank:desc")]
	[InlineData("rank:DeSc")]
	public async Task Direction_is_case_insensitive(string sort) {
		Assertions.HasIds(await this.Page(Query.Sort(sort)), 8, 7, 6, 5, 4, 3, 2, 1);
	}

	[Fact]
	public async Task A_missing_direction_is_rejected() {
		Assert.Equal("Sort value 'rank' must use the format 'field:ASC' or 'field:DESC'.", await this.Rejects(Query.Sort("rank")));
	}

	[Fact]
	public async Task An_unknown_direction_is_rejected() {
		Assert.Equal("Sort direction 'UP' is not supported.", await this.Rejects(Query.Sort("rank:UP")));
	}

	[Fact]
	public async Task An_unsortable_field_is_rejected() {
		Assert.Equal("Sort for field 'nope' is not configured.", await this.Rejects(Query.Sort("nope:ASC")));
	}

	[Fact]
	public async Task Sorts_are_applied_in_the_order_given() {
		// Draft(0) then Active(1) then Discontinued(2), each by descending rank.
		Assertions.HasIds(await this.Page(Query.Sort("status:ASC", "rank:DESC")), 5, 3, 8, 7, 4, 2, 1, 6);
	}

	[Fact]
	public async Task The_tie_breaker_orders_rows_the_primary_sort_ties() {
		// Sorting by status alone leaves five Active rows tied; the id tie-breaker decides among them.
		Assertions.HasIds(await this.Page(Query.Sort("status:ASC")), 3, 5, 1, 2, 4, 7, 8, 6);
	}

	[Fact]
	public async Task The_tie_breaker_direction_is_honoured() {

		// Descending, so the tied rows come back in the opposite order to the one the storage would
		// happen to hand back. Without the tie-breaker applied this assertion cannot pass by luck.
		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.Sortable("status", p => p.Status)
			.WithTieBreaker(p => p.Id, PaginateSortDirection.Desc));

		Assertions.HasIds(await this.Page(Query.Sort("status:ASC"), config), 5, 3, 8, 7, 4, 2, 1, 6);

	}

	[Fact]
	public async Task Defaults_apply_when_the_request_sorts_by_nothing() {
		Assertions.HasIds(await this.Page(new PaginateQuery { Limit = Query.All }), 1, 2, 3, 4, 5, 6, 7, 8);
	}

	[Fact]
	public async Task A_requested_sort_replaces_the_defaults_rather_than_extending_them() {
		// Products 1 and 2 are the only reviewed ones; the rest tie at zero and fall to the tie-breaker.
		// Under the default rank sort this would be 1..8, so the defaults demonstrably did not apply.
		Assertions.HasIds(await this.Page(Query.Sort("reviewCount:DESC")), 1, 2, 3, 4, 5, 6, 7, 8);
	}

	[Fact]
	public void A_config_that_cannot_order_is_refused_at_build_time() {

		// This used to be a runtime 400 on every request the config could not order -- a configuration defect
		// reported as a client error, and one that hid for as long as every caller happened to send sortBy.
		var exception = Assert.Throws<InvalidOperationException>(() => PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.Sortable("rank", p => p.Rank)));

		Assert.StartsWith("A pagination configuration requires WithTieBreaker(...):", exception.Message);

	}

	[Fact]
	public async Task A_tie_breaker_alone_is_enough_to_page() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(50, 50)
			.WithTieBreaker(p => p.Id, PaginateSortDirection.Desc));

		Assertions.HasIds(await this.Page(new PaginateQuery { Limit = Query.All }, config), 8, 7, 6, 5, 4, 3, 2, 1);

	}

	/// <summary>
	///     Sort resolution runs before the count, so the three tests below hold for requests that never reach the
	///     row-fetching query at all. It used to run inside the non-empty branch, which meant a filter matching
	///     nothing — or simply a page past the end — answered 200 to a sort the config does not have.
	/// </summary>
	private static PaginateQuery MatchingNothing(params string[] sortBy) {
		return new PaginateQuery {
			Limit = Query.All,
			SortBy = sortBy,
			Filters = new Dictionary<string, IReadOnlyList<string>> { ["id"] = ["$eq:999"] }
		};
	}

	[Fact]
	public async Task An_unsortable_field_is_rejected_even_when_the_filter_matches_nothing() {
		Assert.Equal("Sort for field 'nope' is not configured.", await this.Rejects(MatchingNothing("nope:ASC")));
	}

	[Fact]
	public async Task An_unsortable_field_is_rejected_even_past_the_last_page() {
		Assert.Equal("Sort for field 'nope' is not configured.", await this.Rejects(new PaginateQuery { Page = 999, SortBy = ["nope:ASC"] }));
	}

	[Theory]
	[InlineData("rank:ASC", "rank:DESC")]
	[InlineData("rank:ASC", "RANK:ASC")]
	public async Task A_repeated_sort_field_is_rejected(string first, string second) {

		// Symmetric with searchBy, whose published reason is "so a client cannot ship a typo that silently
		// does nothing": the second key was dead, consumed a MaxSortFields slot and was echoed in meta.sortBy.
		Assert.Equal($"Sort field '{second.Split(':')[0]}' is specified more than once.",
			await this.Rejects(Query.Sort(first, second)));

	}

	[Fact]
	public async Task A_repeated_sort_field_is_rejected_before_the_query_runs() {
		// Resolution happens before the count, so the refusal does not depend on rows matching.
		Assert.Equal("Sort field 'rank' is specified more than once.", await this.Rejects(MatchingNothing("rank:ASC", "rank:DESC")));
	}

	[Fact]
	public async Task A_config_with_only_a_tie_breaker_orders_by_it_even_when_nothing_matches() {

		// The counterpart of the build-time refusal above: a config that declares no sortable field at all is
		// still perfectly valid, because the tie-breaker is the ordering.
		var page = await this.Page(MatchingNothing(), Unordered);

		Assert.Empty(page.Items);
		Assert.Empty(page.Meta.SortBy);

	}

}

/// <summary>
///     The culture-pinned string comparer is for the <b>in-memory</b> leg, and the flag that used to select it
///     reads "not Entity Framework Core" rather than "in memory". A synchronous provider backed by a database
///     falls into the same branch, and the three-argument <c>OrderBy</c> is an overload essentially no relational
///     LINQ provider translates — so the fix for an ordering inconsistency turned a working string sort into a
///     <c>NotSupportedException</c> on a provider that never asked for a culture.
/// </summary>
public sealed class OrderingOverloadTests {

	[Fact]
	public void A_synchronous_custom_provider_keeps_the_plain_OrderBy_overload() {

		var composed = new SynchronousQueryable<Product>(TestData.Products().AsQueryable())
			.ApplyPagination(new PaginateQuery { SortBy = ["name:ASC"] }, ByName);

		Assert.Equal(2, ArgumentCountOfOutermostOrder(composed.Query.Expression));

	}

	/// <summary>
	///     The pattern operators stay two-way, deliberately, and this pins that rather than the other rule. An
	///     unrecognised provider takes the in-memory shape — <c>string.IndexOf(value, StringComparison)</c> —
	///     which it will refuse to translate. Narrowing here the way the ordering was narrowed would need a third
	///     construct, and the only candidate is the two-argument <c>string.Contains</c>: translatable, but
	///     <b>case-sensitive</b> where this is case-insensitive. That trades a loud failure for a silent change
	///     of matching semantics, which is the worse of the two. The ordering had no such cost — narrowing
	///     there returns a provider to exactly what it got before the comparer existed.
	/// </summary>
	[Fact]
	public void A_pattern_operator_keeps_one_shape_for_every_provider_that_is_not_ef() {

		var composed = new SynchronousQueryable<Product>(TestData.Products().AsQueryable())
			.ApplyPaginateFilters(Query.Filter("name", "$contains:wid"), ByNameContains);

		Assert.Contains("OrdinalIgnoreCase", composed.Query.Expression.ToString(), StringComparison.Ordinal);

	}

	private readonly static PaginateConfig<Product> ByNameContains = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.Filterable("name", p => p.Name, PaginateFilterOperator.Contains));

	[Fact]
	public void The_in_memory_leg_still_gets_the_culture_pinned_comparer() {

		// EnumerableQuery is what List<T>.AsQueryable() returns, and the only leg whose ordering has a culture
		// to choose: Comparer<string>.Default reads CurrentCulture, so the page order would follow the host's —
		// or, under request localization, the caller's Accept-Language.
		var composed = TestData.Products().AsQueryable()
			.ApplyPagination(new PaginateQuery { SortBy = ["name:ASC"] }, ByName);

		Assert.Equal(3, ArgumentCountOfOutermostOrder(composed.Query.Expression));

	}

	private readonly static PaginateConfig<Product> ByName = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.Sortable("name", p => p.Name)
		.WithTieBreaker(p => p.Id));

	/// <summary>The composed tree is ThenBy(OrderBy(...)), so the first ordering call found going down is the one.</summary>
	private static int ArgumentCountOfOutermostOrder(Expression expression) {

		for (var node = expression as MethodCallExpression; node is not null; node = node.Arguments[0] as MethodCallExpression) {
			if (node.Method.Name is "OrderBy" or "OrderByDescending") return node.Arguments.Count;
		}

		throw new InvalidOperationException("no ordering call in the composed tree");

	}

}

/// <summary>
///     A queryable whose provider is neither Entity Framework Core's nor <see cref="EnumerableQuery{T}" /> — the
///     shape of a synchronous LINQ provider over a database. Composition is what is under test, so execution just
///     delegates to the wrapped in-memory queryable.
/// </summary>
internal sealed class SynchronousQueryable<T>(IQueryable<T> inner) : IOrderedQueryable<T>, IQueryProvider {

	public Type ElementType => inner.ElementType;

	public Expression Expression => inner.Expression;

	public IQueryProvider Provider => this;

	// Honours the expression's own element type rather than assuming T. The engine only calls the generic
	// overload today, so returning SynchronousQueryable<T> unconditionally would pass — until the first test
	// that composes a projection against this double, which would then fail with an InvalidCastException thrown
	// from inside the double rather than from the code under test.
	public IQueryable CreateQuery(Expression expression) {
		var element = expression.Type.GetGenericArguments().SingleOrDefault() ?? typeof(T);
		return (IQueryable)Activator.CreateInstance(
			typeof(SynchronousQueryable<>).MakeGenericType(element),
			inner.Provider.CreateQuery(expression))!;
	}

	public IQueryable<TElement> CreateQuery<TElement>(Expression expression) {
		return new SynchronousQueryable<TElement>(inner.Provider.CreateQuery<TElement>(expression));
	}

	public object? Execute(Expression expression) { return inner.Provider.Execute(expression); }

	public TResult Execute<TResult>(Expression expression) { return inner.Provider.Execute<TResult>(expression); }

	public IEnumerator<T> GetEnumerator() { return inner.GetEnumerator(); }

	IEnumerator IEnumerable.GetEnumerator() { return this.GetEnumerator(); }

}
