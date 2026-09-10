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

}
