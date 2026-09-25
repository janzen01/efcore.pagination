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
		await Assert.ThrowsAsync<PaginateQueryException>(() => Source().PaginateAsync<Product, ProductDto>(new PaginateQuery { Page = 0 }, TestData.Config, null, TestContext.Current.CancellationToken));
	}

	/// <summary>
	///     NOT a <c>PaginateQueryException</c>, and that is the assertion: this is a server wiring mistake, not
	///     something a caller sent, so answering it as a <c>400</c> would blame the client for the server's
	///     configuration and put an internal diagnostic in a response body. A plain
	///     <see cref="NotSupportedException" /> leaves the ASP.NET Core filters alone and surfaces as a 500.
	/// </summary>
	[Fact]
	public async Task An_async_provider_that_is_not_ef_is_refused_with_a_clear_message() {

		var source = new NonEfAsyncQueryable<Product>(Source());

		var bare = await Assert.ThrowsAsync<NotSupportedException>(() => source.PaginateAsync<Product, ProductDto>(new PaginateQuery(), TestData.Config, null, TestContext.Current.CancellationToken));

		var filtered = await Assert.ThrowsAsync<NotSupportedException>(() => source.PaginateAsync<Product, ProductDto>(Query.Filter("rank", "$eq:30"), TestData.Config, null, TestContext.Current.CancellationToken));

		foreach (var message in new[] { bare.Message, filtered.Message }) {
			Assert.Contains("Entity Framework Core", message, StringComparison.Ordinal);
			Assert.Contains("SQLite", message, StringComparison.Ordinal);
		}

		// The type is the contract here: PaginateQueryException is what the filters turn into a 400.
		Assert.IsNotType<PaginateQueryException>(bare);
		Assert.IsNotType<PaginateQueryException>(filtered);

	}

	[Fact]
	public void Both_composers_refuse_an_async_provider_that_is_not_ef() {

		var source = new NonEfAsyncQueryable<Product>(Source());

		// ApplyPaginateFilters never reaches ApplySorts, so the probe inside Compose is the one that has to
		// answer for it -- narrowing only the sorting probe would leave the filtered composer on the old path.
		Assert.Throws<NotSupportedException>(() => source.ApplyPaginateFilters(new PaginateQuery(), TestData.Config));
		Assert.Throws<NotSupportedException>(() => source.ApplyPagination(new PaginateQuery(), TestData.Config));

	}

	/// <summary>
	///     A queryable whose provider satisfies <see cref="IAsyncQueryProvider" /> without being Entity Framework
	///     Core's — the shape a queryable-backed mocking library hands a consumer's unit test. Execution is
	///     delegated to the wrapped in-memory queryable; the async path throws, because nothing in the engine may
	///     reach it once the provider has been recognized as foreign.
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

/// <summary>
///     Leg selection rests on <c>EntityQueryProvider</c>, which EF Core marks <c>[EntityFrameworkInternal]</c>.
///     A type an upstream package is free to move is a runtime <c>TypeLoadException</c> in a consumer's process,
///     not a compile error in ours — the engine names it in an <c>is</c>-pattern, which binds late. Pinning it
///     here moves that discovery to an EF Core bump on our own CI, where Dependabot opens the PR.
/// </summary>
public sealed class EfInternalCouplingTests : IClassFixture<SqliteFixture> {

	private readonly SqliteFixture _fixture;

	public EfInternalCouplingTests(SqliteFixture fixture) { _fixture = fixture; }

	[Fact]
	public async Task The_ef_provider_type_the_engine_probes_for_still_exists_and_still_matches() {

		var probed = typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly
			.GetType("Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider", throwOnError: false);

		Assert.True(probed is not null,
			"EntityQueryProvider has moved or been renamed; PaginateQueryableExtensions.UseDatabaseFunctions no longer "
			+ "recognizes EF Core and every query would take the in-memory leg."
		);

		await using var context = _fixture.CreateContext();

		Assert.True(
			probed.IsInstanceOfType(SqliteFixture.Products(context).Provider),
			"an EF Core queryable's provider is no longer an EntityQueryProvider."
		);

	}

}

/// <summary>
///     The promise the projections guide makes: a consumer's <c>projector</c> and <c>postMap</c> continue on a
///     thread-pool thread, never on the caller's <see cref="SynchronizationContext" />. What keeps it is
///     <c>ConfigureAwait(false)</c> on the engine's awaits, and nothing else pinned it -- dropping one left the
///     suite green, because SQLite completes its async calls synchronously and no await ever yielded.
/// </summary>
public sealed class ContinuationContextTests : IClassFixture<SqliteFixture> {

	private readonly SqliteFixture _fixture;

	public ContinuationContextTests(SqliteFixture fixture) { _fixture = fixture; }

	[Fact]
	public async Task The_projector_does_not_resume_on_the_callers_context() {

		await using var context = _fixture.CreateYieldingContext();
		var recording = new RecordingContext();
		SynchronizationContext? seen = recording;

		await StartUnder(recording, () => SqliteFixture.Products(context).PaginateMapAsync(
			new PaginateQuery(),
			TestData.Config,
			product => {
				seen = SynchronizationContext.Current;
				return product.Id;
			},
			null,
			TestContext.Current.CancellationToken
		));

		Assert.Null(seen);
		Assert.Equal(0, recording.Posts);

	}

	[Fact]
	public async Task The_post_map_does_not_resume_on_the_callers_context() {

		await using var context = _fixture.CreateYieldingContext();
		var recording = new RecordingContext();
		SynchronizationContext? seen = recording;

		await StartUnder(recording, () => SqliteFixture.Products(context).PaginateSelectMapAsync(
			new PaginateQuery(),
			TestData.Config,
			product => product.Id,
			id => {
				seen = SynchronizationContext.Current;
				return id;
			},
			null,
			TestContext.Current.CancellationToken
		));

		Assert.Null(seen);
		Assert.Equal(0, recording.Posts);

	}

	// The context is installed only while the engine's synchronous prefix runs, which is where an await would
	// capture it. Awaiting the task is left to the caller's own context, so the test's await posts nothing here.
	private static Task StartUnder(SynchronizationContext context, Func<Task> start) {

		var prior = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(context);

		try {
			return start();
		} finally {
			SynchronizationContext.SetSynchronizationContext(prior);
		}

	}

	/// <summary>Counts every post and still runs it, as a UI context would, so a capturing await is seen rather than deadlocked.</summary>
	private sealed class RecordingContext : SynchronizationContext {

		private int _posts;

		public int Posts => Volatile.Read(ref _posts);

		public override void Post(SendOrPostCallback d, object? state) {

			Interlocked.Increment(ref _posts);

			// Restored afterward: a pool thread left carrying this context would hand it to whatever runs there next.
			ThreadPool.QueueUserWorkItem(_ => {
				SetSynchronizationContext(this);

				try {
					d(state);
				} finally {
					SetSynchronizationContext(null);
				}
			});

		}

	}

}
