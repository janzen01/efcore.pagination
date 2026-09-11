using Janzen.Pagination.EntityFrameworkCore.DependencyInjection;
using Janzen.Pagination.EntityFrameworkCore.Like;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.Tests;

/// <summary>
///     A real PostgreSQL server, seeded with the same rows as the SQLite leg plus the literal-backslash row.
///     The connection string comes from the <c>JANZEN_TEST_POSTGRES</c> environment variable; absent, the
///     fixture initialises nothing and every test in <see cref="PostgreSqlLegTests" /> is gated off.
/// </summary>
/// <remarks>
///     The database the connection string names is <b>dropped and recreated</b>, so it must be a throwaway one.
///     CI points this at a service container that exists for the length of the job; a contributor pointing it
///     at a local server is expected to name a database created for the purpose.
/// </remarks>
public sealed class PostgreSqlFixture : IAsyncLifetime {

	public const string ConnectionStringVariable = "JANZEN_TEST_POSTGRES";

	private DbContextOptions<TestDbContext>? _options;

	public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

	public static bool IsAvailable => !string.IsNullOrWhiteSpace(ConnectionString);

	public async ValueTask InitializeAsync() {

		if (!IsAvailable) return;

		_options = new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(ConnectionString).Options;

		await using var context = this.CreateContext();
		await context.Database.EnsureDeletedAsync();
		await context.Database.EnsureCreatedAsync();
		// The backslash row comes along so "a literal escape character survives" is assertable against a real
		// ESCAPE implementation, which is the half of the escaping story SQLite answers differently.
		context.Products.AddRange(PatternEscapingTests.Rows());
		await context.SaveChangesAsync();

	}

	public TestDbContext CreateContext() { return new TestDbContext(_options!); }

	public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }

}

/// <summary>
///     The four things about this library that only a real PostgreSQL can answer, and that SQLite does not merely
///     answer less precisely but answers <i>differently</i>: which keyword the pattern operators emit, whether
///     matching is case-sensitive, whether a caller's wildcard survives a genuine <c>ESCAPE '\'</c>, and what
///     happens to a nul byte. Two of them — the case contract and the nul byte — reached a public release
///     because SQLite cannot see them.
/// </summary>
/// <remarks>
///     Gated on <see cref="PostgreSqlFixture.ConnectionStringVariable" /> rather than skipped by hand: the CI job
///     sets that variable unconditionally from its service container, so in the only place that decides whether
///     code merges there is nothing to skip. A contributor with no server runs the suite they run today.
///     <para>
///         The class swaps <see cref="PaginateLikeDefaults.Strategy" />, a process-wide mutable static, which is
///         why it joins the <c>LikeDefaults</c> collection and restores the previous value on the way out.
///     </para>
/// </remarks>
[Collection("LikeDefaults")]
public sealed class PostgreSqlLegTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>, IDisposable {

	private const string Gate = "Set " + PostgreSqlFixture.ConnectionStringVariable + " to a throwaway PostgreSQL database to run this leg.";

	private readonly IPaginateLikeStrategy _previous = PaginateLikeDefaults.Strategy;

	public static bool IsAvailable => PostgreSqlFixture.IsAvailable;

	public void Dispose() { PaginateLikeDefaults.Strategy = _previous; }

	private static void UsePostgreSql() { new ServiceCollection().AddPagination(p => p.UsePostgreSql()); }

	/// <summary>The four request shapes that reach a pattern predicate: three operators plus the search term.</summary>
	private static PaginateQuery Pattern(string token) {
		return token == "search" ? Query.Search("wid") : Query.Filter("name", token + ":wid");
	}

	private async Task<int[]> Ids(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		var page = await context.Products.AsNoTracking().PageAsync<ProductDto>(request);
		return [.. page.Items.Select(item => item.Id)];
	}

	private string Sql(PaginateQuery request) {
		using var context = fixture.CreateContext();
		return context.Products.AsNoTracking().ApplyPaginateFilters(request, TestData.Config).Query.ToQueryString();
	}

	[Theory(Skip = Gate, SkipUnless = nameof(IsAvailable))]
	[InlineData("$ilike")]
	[InlineData("$sw")]
	[InlineData("$contains")]
	[InlineData("search")]
	public void Only_UsePostgreSql_emits_native_ilike(string token) {

		// SQLite has no ILIKE at all, so this pair cannot be observed there: the native form does not translate
		// and the query fails before it runs.
		string portable = this.Sql(Pattern(token));

		Assert.Contains(" LIKE ", portable, StringComparison.Ordinal);
		Assert.DoesNotContain("ILIKE", portable, StringComparison.Ordinal);

		UsePostgreSql();

		string native = this.Sql(Pattern(token));

		Assert.Contains(" ILIKE ", native, StringComparison.Ordinal);
		Assert.Contains(@"ESCAPE '\'", native, StringComparison.Ordinal);

	}

	[Fact(Skip = Gate, SkipUnless = nameof(IsAvailable))]
	public async Task The_portable_strategy_is_case_sensitive_on_postgresql_and_UsePostgreSql_is_not() {

		// The rows differ only in case: APPLE is 7, "apple pie" is 8. This is the divergence the suite could not
		// see — the in-memory leg matches both (hard-coded OrdinalIgnoreCase) and SQLite's LIKE matches both
		// (ASCII case-insensitive by default), so only a real PostgreSQL distinguishes the two strategies.
		int[] portable = await this.Ids(Query.Filter("name", "$ilike:apple"));

		UsePostgreSql();

		int[] native = await this.Ids(Query.Filter("name", "$ilike:apple"));

		Assert.Equal([8], portable);
		Assert.Equal([7, 8], native);

	}

	[Theory(Skip = Gate, SkipUnless = nameof(IsAvailable))]
	[InlineData("$ilike:50% off", new[] { 4 })]
	[InlineData("$ilike:50_ off", new int[0])]
	[InlineData(@"$ilike:a\bc", new[] { 9 })]
	[InlineData("$ilike:a%c", new int[0])]
	public async Task A_wildcard_in_the_value_stays_literal_under_native_ilike(string criterion, int[] expected) {

		// Against a server that genuinely honours ESCAPE, not against the emitted pattern string. Both directions
		// are here on purpose: the negative cases fail if a metacharacter stays live, and the positive ones fail
		// if the escaping eats a character the caller meant literally.
		UsePostgreSql();

		Assert.Equal(expected, await this.Ids(Query.Filter("name", criterion)));

	}

	[Theory(Skip = Gate, SkipUnless = nameof(IsAvailable))]
	[InlineData("$eq:a\0b")]
	[InlineData("$ilike:a\0b")]
	[InlineData("$in:ok,a\0b")]
	public async Task A_nul_byte_never_reaches_the_server(string criterion) {

		// PostgreSQL answers 22021 "invalid byte sequence for encoding UTF8: 0x00" and the caller gets a 500,
		// because a PostgresException is not a PaginateQueryException and nothing in the pipeline converts it.
		// The guard sits in the parsing layer, above every provider; this is the leg that proves it has to.
		UsePostgreSql();

		Assert.Equal("Filter 'name' must not contain a null character.",
			await Assertions.RejectsAsync(() => this.Ids(Query.Filter("name", criterion))));

	}

	[Fact(Skip = Gate, SkipUnless = nameof(IsAvailable))]
	public async Task A_search_term_carrying_a_nul_byte_never_reaches_the_server() {

		UsePostgreSql();

		Assert.Equal("Search term must not contain a null character.",
			await Assertions.RejectsAsync(() => this.Ids(Query.Search("wid\0get"))));

	}

}
