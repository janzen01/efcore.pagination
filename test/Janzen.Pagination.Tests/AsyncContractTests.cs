using Microsoft.EntityFrameworkCore.Query;

using System.Collections;
using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The two halves of the async contract the four entry points publish: an <b>argument</b> error is raised at
///     the call, a <b>request</b> error is delivered through the returned task, and a queryable whose provider is
///     asynchronous but is not Entity Framework Core is refused with a message a developer can act on rather than
///     with whatever the engine happens to trip over first.
///     <para>
///         This class mutates no process-wide static, so it needs no collection (guardrail G1).
///     </para>
/// </summary>
public sealed class AsyncContractTests {

	private static IQueryable<Product> Source() { return TestData.Products().AsQueryable(); }

	[Fact]
	public void A_null_argument_is_thrown_at_the_call_not_through_the_task() {

		var source = Source();
		var request = new PaginateQuery();
		var ct = TestContext.Current.CancellationToken;

		// Assert.Throws, not ThrowsAsync: the point is that no task is ever handed back. A fan-out that builds
		// its tasks with Select(...).ToArray() before awaiting them loses every sibling task if this throws
		// late, so the guard has to be on the synchronous side of the wrapper.
		Assert.Throws<ArgumentNullException>(() => { _ = source.PaginateAsync<Product, ProductDto>(null!, TestData.Config, null, ct); });
		Assert.Throws<ArgumentNullException>(() => { _ = source.PaginateAsync<Product, ProductDto>(request, null!, null, ct); });

		Assert.Throws<ArgumentNullException>(() => { _ = source.PaginateSelectAsync(request, null!, (Product product) => product.Id, null, ct); });
		Assert.Throws<ArgumentNullException>(() => { _ = source.PaginateSelectMapAsync(request, null!, (Product product) => product.Id, id => id, null, ct); });
		Assert.Throws<ArgumentNullException>(() => { _ = source.PaginateMapAsync(request, null!, (Product product) => product.Id, null, ct); });

	}

	[Fact]
	public async Task A_request_error_is_still_delivered_through_the_task() {

		// The mirror of the test above: moving the argument guards must not drag the request validation with
		// them. An invalid page stays a faulted task, which is what the ASP.NET Core filters translate.
		await Assert.ThrowsAsync<PaginateQueryException>(
			() => Source().PaginateAsync<Product, ProductDto>(new PaginateQuery { Page = 0 }, TestData.Config, null, TestContext.Current.CancellationToken));

	}

	[Fact]
	public async Task An_async_provider_that_is_not_ef_is_refused_with_a_clear_message() {

		var source = new NonEfAsyncQueryable<Product>(Source());

		var bare = await Assert.ThrowsAsync<PaginateQueryException>(
			() => source.PaginateAsync<Product, ProductDto>(new PaginateQuery(), TestData.Config, null, TestContext.Current.CancellationToken));

		var filtered = await Assert.ThrowsAsync<PaginateQueryException>(
			() => source.PaginateAsync<Product, ProductDto>(Query.Filter("rank", "$eq:30"), TestData.Config, null, TestContext.Current.CancellationToken));

		foreach (var message in new[] { bare.Message, filtered.Message }) {
			Assert.Contains("Entity Framework Core", message, StringComparison.Ordinal);
			Assert.Contains("SQLite", message, StringComparison.Ordinal);
		}

	}

	[Fact]
	public void Both_composers_refuse_an_async_provider_that_is_not_ef() {

		var source = new NonEfAsyncQueryable<Product>(Source());

		// ApplyPaginateFilters never reaches ApplySorts, so the probe inside Compose is the one that has to
		// answer for it -- narrowing only the sorting probe would leave the filtered composer on the old path.
		Assert.Throws<PaginateQueryException>(() => source.ApplyPaginateFilters(new PaginateQuery(), TestData.Config));
		Assert.Throws<PaginateQueryException>(() => source.ApplyPagination(new PaginateQuery(), TestData.Config));

	}

	/// <summary>
	///     A queryable whose provider satisfies <see cref="IAsyncQueryProvider" /> without being Entity Framework
	///     Core's — the shape a queryable-backed mocking library hands a consumer's unit test. Execution is
	///     delegated to the wrapped in-memory queryable; the async path throws, because nothing in the engine may
	///     reach it once the provider has been recognised as foreign.
	/// </summary>
	private sealed class NonEfAsyncQueryable<T>(IQueryable<T> inner) : IQueryable<T>, IAsyncQueryProvider {

		public Type ElementType => inner.ElementType;

		public Expression Expression => inner.Expression;

		public IQueryProvider Provider => this;

		public IEnumerator<T> GetEnumerator() { return inner.GetEnumerator(); }

		IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

		public IQueryable CreateQuery(Expression expression) { return inner.Provider.CreateQuery(expression); }

		public IQueryable<TElement> CreateQuery<TElement>(Expression expression) {
			return new NonEfAsyncQueryable<TElement>(inner.Provider.CreateQuery<TElement>(expression));
		}

		public object? Execute(Expression expression) { return inner.Provider.Execute(expression); }

		public TResult Execute<TResult>(Expression expression) { return inner.Provider.Execute<TResult>(expression); }

		public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default) {
			throw new NotSupportedException("This double has no asynchronous execution path.");
		}

	}

}
