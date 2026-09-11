using Microsoft.EntityFrameworkCore;

using System.Collections;
using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>
///     What the engine composes, asserted on the composed query itself rather than on the rows it returns: the
///     search stage builds one shared pattern parameter however many fields it spans, the ordering stage builds
///     the same call chain the typed <c>Queryable</c> overloads do, and neither hides a provider failure behind a
///     reflection wrapper. These are the properties a hoisted constant or a removed <c>MethodInfo.Invoke</c> could
///     plausibly disturb, so they are pinned where a rewrite of those stages has to look.
/// </summary>
public sealed class QueryCompositionTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>Three searchable fields, one of them across a navigation, so the OR chain is long enough to show sharing.</summary>
	private readonly static PaginateConfig<Product> ThreeSearchFields = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.Sortable("rank", p => p.Rank)
		.DefaultSortBy("rank")
		.WithTieBreaker(p => p.Id)
		.Searchable("name", p => p.Name)
		.Searchable("description", p => p.Description)
		.Searchable("categoryName", p => p.Category!.Name));

	private static int Occurrences(string text, string token) {
		int count = 0;
		for (int index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal)) count++;

		return count;
	}

	[Fact]
	public void A_search_over_three_fields_composes_one_shared_pattern_parameter() {

		using var context = fixture.CreateContext();

		string sql = SqliteFixture.Products(context).ApplyPagination(Query.Search("wid"), ThreeSearchFields).Query.ToQueryString();

		// Three branches, one parameter: EF collapses structurally equal pattern constants, so building the node
		// once instead of once per field must leave the command byte-identical.
		Assert.Equal(3, Occurrences(sql, @"LIKE @p ESCAPE '\'"));
		Assert.Equal(1, Occurrences(sql, ".param set @p '%wid%'"));

	}

	[Fact]
	public void A_multi_key_sort_composes_the_ordering_the_typed_overloads_would() {

		using var context = fixture.CreateContext();

		string sql = SqliteFixture.Products(context).ApplyPagination(Query.Sort("status:DESC", "rank:ASC"), TestData.Config).Query.ToQueryString();

		// OrderByDescending, then ThenBy, then the tie-breaker — the shape the reflective call produced, which is
		// what a rewrite around Queryable.OrderBy's own body has to reproduce exactly.
		Assert.Contains("ORDER BY \"p\".\"Status\" DESC, \"p\".\"Rank\", \"p\".\"Id\"", sql, StringComparison.Ordinal);

	}

	[Fact]
	public async Task A_descending_secondary_key_still_orders_on_both_legs() {

		var request = Query.Sort("status:ASC", "rank:DESC");

		await using var context = fixture.CreateContext();
		var database = await SqliteFixture.Products(context).PageAsync<ProductDto>(request);
		var memory = await TestData.Products().AsQueryable().PageAsync<ProductDto>(request);

		// Draft first, then Active, then Discontinued — each group by descending rank. A ThenByDescending key is
		// the one arm of the four-way switch a rewrite could silently swap for its ascending twin.
		Assertions.HasIds(database, 5, 3, 8, 7, 4, 2, 1, 6);
		Assert.Equal(database.Items, memory.Items);

	}

	[Fact]
	public void A_provider_failure_while_ordering_reaches_the_caller_unwrapped() {

		var source = new UncomposableQueryable<Product>(TestData.Products().AsQueryable());

		// A bare request applies no filter and no search, so the ordering stage is the first thing to ask the
		// provider to compose anything. Reflective invocation reports this as TargetInvocationException, which no
		// consumer catch clause names.
		var exception = Assert.Throws<NotSupportedException>(() => source.ApplyPagination(new PaginateQuery(), TestData.Config));

		Assert.Equal("This provider composes nothing.", exception.Message);

	}

	[Fact]
	public async Task A_value_typed_element_type_paginates_on_the_map_path() {

		var request = new PaginateQuery { Limit = 50 };
		var config = PaginateConfig<int>.Create(b => b.WithLimits(50, 50).WithTieBreaker(value => value));
		var ct = TestContext.Current.CancellationToken;

		await using var context = fixture.CreateContext();

		// AsNoTracking is constrained to reference types, so the entry point that applies it used to fail here
		// while the other three answered the same queryable. A value type is never tracked, so skipping it is the
		// whole fix — and both legs must agree, which is what the mirror below pins.
		var database = await context.Products.AsNoTracking().Select(p => p.Id).PaginateMapAsync(request, config, value => value, null, ct);
		var memory = await TestData.Products().Select(p => p.Id).AsQueryable().PaginateMapAsync(request, config, value => value, null, ct);

		Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], database.Items);
		Assert.Equal(database.Items, memory.Items);

	}

	[Fact]
	public void Contains_over_a_collection_across_a_navigation_still_matches_in_memory() {

		List<Holder> holders = [
			new Holder { Id = 1, Bag = new Bag { Tags = ["red", "small"] } },
			new Holder { Id = 2, Bag = new Bag { Tags = ["blue"] } },
			new Holder { Id = 3, Bag = null }
		];

		var config = PaginateConfig<Holder>.Create(b => b
			.WithLimits(50, 50)
			.WithTieBreaker(holder => holder.Id)
			.Filterable("tags", holder => holder.Bag!.Tags, PaginateFilterOperator.Contains));

		// The in-memory leg rewrites the selector into its null-safe form before the element type is resolved, so
		// a precomputed element type has to stay in step with the expression actually built — and the row with no
		// bag must answer "no match" rather than throw.
		var matched = holders.AsQueryable().ApplyPagination(Query.Filter("tags", "$contains:red"), config).Query.ToList();

		Assert.Equal([1], matched.Select(holder => holder.Id).ToArray());

	}

	private sealed class Holder {

		public int Id { get; set; }

		public Bag? Bag { get; set; }

	}

	private sealed class Bag {

		public List<string> Tags { get; set; } = [];

	}

	/// <summary>
	///     A synchronous provider that refuses to compose anything. It is deliberately not an
	///     <c>IAsyncQueryProvider</c>: the engine refuses those outright, so this is the remaining shape whose
	///     composition failure the engine can still pass on, and the point is what the caller sees when it does.
	/// </summary>
	private sealed class UncomposableQueryable<T>(IQueryable<T> inner) : IQueryable<T>, IQueryProvider {

		public Type ElementType => inner.ElementType;

		public Expression Expression => inner.Expression;

		public IQueryProvider Provider => this;

		public IEnumerator<T> GetEnumerator() { return inner.GetEnumerator(); }

		IEnumerator IEnumerable.GetEnumerator() { return this.GetEnumerator(); }

		public IQueryable CreateQuery(Expression expression) { throw new NotSupportedException("This provider composes nothing."); }

		public IQueryable<TElement> CreateQuery<TElement>(Expression expression) { throw new NotSupportedException("This provider composes nothing."); }

		public object? Execute(Expression expression) { return inner.Provider.Execute(expression); }

		public TResult Execute<TResult>(Expression expression) { return inner.Provider.Execute<TResult>(expression); }

	}

}
