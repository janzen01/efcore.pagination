using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>
///     A cancelled token faults the read with <see cref="OperationCanceledException" /> — it does not become a
///     <see cref="PaginateQueryException" />, a <c>400</c> or an empty page.
///     <para>
///         The code is correct by inspection and always was; the point is that nothing held it there.
///         <c>CancellationTokenSource</c> appeared nowhere in the repository, the suite's only token was
///         <c>TestContext.Current.CancellationToken</c> threaded as an argument and never cancelled, and the two
///         <c>ThrowIfCancellationRequested</c> guards on the in-memory terminal operators could be deleted with
///         the suite still green. The exception filters already list three exception types each; one future
///         widening to <c>catch (Exception)</c> turns every cancelled request into a 400 and merges green.
///     </para>
///     This class mutates no process-wide static, so it needs no collection (guardrail G1).
/// </summary>
public sealed class CancellationTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	[Fact]
	public async Task A_cancelled_token_faults_the_database_leg() {

		await using var context = fixture.CreateContext();

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => SqliteFixture.Products(context).PaginateAsync<Product, ProductDto>(new PaginateQuery(), TestData.Config, null, cts.Token));

	}

	[Fact]
	public async Task A_cancelled_token_faults_the_in_memory_leg() {

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		// This leg has no async provider to honour the token, so the engine checks it itself before each
		// synchronous terminal operator. Deleting either check leaves this the only test that notices.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => TestData.Products().AsQueryable().PaginateAsync<Product, ProductDto>(new PaginateQuery(), TestData.Config, null, cts.Token));

	}

	[Fact]
	public async Task A_cancelled_token_wins_over_an_invalid_request() {

		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		// page=0 is refused by the composer, which runs as the method's first statement -- so a caller who had
		// already gone away used to be answered with a client error for a request nobody was waiting for.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => TestData.Products().AsQueryable().PaginateAsync<Product, ProductDto>(new PaginateQuery { Page = 0 }, TestData.Config, null, cts.Token));

	}

	[Fact]
	public async Task A_token_cancelled_while_the_rows_are_read_stops_the_post_map() {

		using var cts = new CancellationTokenSource();

		int postMapCalls = 0;

		// The selector runs during enumeration, so the token is cancelled after the rows are in memory but before
		// postMap sees them -- the one window the engine never looked at, and the one where a consumer delegate
		// runs over up to UnlimitedMaxRows + 1 rows with no client left to read the answer.
		Expression<Func<Product, Product>> selector = product => CancelAndPass(cts, product);
		Func<Product, ProductDto> postMap = product => {
			postMapCalls++;
			return new ProductDto(product.Id, product.Name, product.Status, product.Rank);
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => TestData.Products().AsQueryable().PaginateSelectMapAsync(new PaginateQuery(), TestData.Config, selector, postMap, null, cts.Token));

		Assert.Equal(0, postMapCalls);

	}

	private static Product CancelAndPass(CancellationTokenSource cts, Product product) {
		cts.Cancel();
		return product;
	}

}
