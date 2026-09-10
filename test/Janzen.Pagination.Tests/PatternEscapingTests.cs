using Janzen.Pagination.EntityFrameworkCore.Engine;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The shared eight rows plus one whose name carries a literal backslash. Nothing about LIKE escaping is
///     observable without such a row: a broken escape chain emits a pattern that still matches none of the
///     eight, so every existing escaping assertion stays green while the escaping itself is inverted.
/// </summary>
public sealed class BackslashFixture : IAsyncLifetime {

	private SqliteConnection _connection = null!;
	private DbContextOptions<TestDbContext> _options = null!;

	public async ValueTask InitializeAsync() {

		_connection = new SqliteConnection("Filename=:memory:");
		await _connection.OpenAsync();

		_options = new DbContextOptionsBuilder<TestDbContext>()
			.UseSqlite(_connection)
			.ConfigureWarnings(w => w.Ignore(SqliteEventId.CompositeKeyWithValueGeneration))
			.Options;

		await using var context = this.CreateContext();
		await context.Database.EnsureCreatedAsync();
		context.Products.AddRange(PatternEscapingTests.Rows());
		await context.SaveChangesAsync();

	}

	public TestDbContext CreateContext() { return new TestDbContext(_options); }

	public async ValueTask DisposeAsync() { await _connection.DisposeAsync(); }

}

/// <summary>
///     The escape chain in <c>EscapeLikePattern</c> and the wildcard the two pattern branches append. Both are
///     order-dependent in a way the code does not show, and both are what makes every other pattern guarantee in
///     this library true — including the one that pathological patterns are unreachable from the query string,
///     which holds only because the number of live wildcards is an engine-chosen constant rather than an input.
///     <para>
///         The escape character has to be replaced <b>first</b>. Escape the wildcard before it and the emitted
///         pattern carries a literal backslash followed by a <i>live</i> wildcard — the caller's wildcard handed
///         back. Each mutation was measured against this branch, and they do not behave alike:
///         moving <c>_</c> ahead of the escape replacement reddens <b>1 of 420</b> — the assertion below, and
///         nothing else, so that reorder is the shipped suite's true blind spot; deleting the escape replacement
///         reddens <b>4</b>, all of them new here; and moving <c>[</c> ahead reddens <b>2</b>, one of which is the
///         pre-existing <c>FilterOperatorTests.Bracket_in_the_value_is_escaped</c>, so that one is <i>not</i> a
///         blind spot at all. A full reversal is caught by two pre-existing tests —
///         <c>Bracket_in_the_value_is_escaped</c> asserts the helper's output directly and
///         <c>Underscore_in_the_value_is_escaped</c> ends in a positive <c>HasIds</c>. The narrow reorder, not the
///         wholesale one, is what this class exists to catch, and the row it seeds is what makes it visible.
///     </para>
/// </summary>
public sealed class PatternEscapingTests(BackslashFixture fixture) : IClassFixture<BackslashFixture> {

	/// <summary>Id of the extra row, whose name is <c>a</c>, a literal backslash, then <c>bc</c>.</summary>
	private const int BackslashRow = 9;

	internal static List<Product> Rows() {

		var rows = TestData.Products();
		rows.Add(new Product { Id = BackslashRow, Name = @"a\bc", Description = "literal backslash", Status = ProductStatus.Active, Rank = 90 });

		return rows;

	}

	private static async Task<int[]> Ids(IQueryable<Product> source, PaginateQuery request) {
		return [.. (await source.PageAsync<ProductDto>(request)).Items.Select(item => item.Id)];
	}

	private Task<int[]> InMemory(PaginateQuery request) { return Ids(Rows().AsQueryable(), request); }

	private async Task<int[]> Sqlite(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Ids(context.Products.AsNoTracking(), request);
	}

	[Fact]
	public void The_escape_character_is_doubled_before_the_wildcards_are_escaped() {

		// One value carrying all four escapable characters, so the assertion fails on any reordering that does
		// not keep the escape replacement first — rather than only on the ones a seeded row happens to expose.
		// Swapping % with _, or _ with [, is byte-identical for every input and is correctly not caught here.
		// Move the backslash out of first place and every character escaped before it acquires a second
		// backslash, which under ESCAPE '\' reads as a literal backslash followed by the caller's live wildcard.
		Assert.Equal(@"a\\b\%c\_d\[e", PaginateExpressionUtils.EscapeLikePattern(@"a\b%c_d[e"));

	}

	[Fact]
	public async Task A_literal_backslash_in_the_value_matches_the_row_that_contains_one() {

		// A positive assertion, unlike the four Assert.Empty ones: drop the backslash replacement entirely and
		// the emitted pattern reads the pair as an escaped 'b', so it looks for "abc" and this row stops matching.
		int[] inMemory = await this.InMemory(Query.Filter("name", @"$ilike:a\bc"));
		int[] sqlite = await this.Sqlite(Query.Filter("name", @"$ilike:a\bc"));

		Assert.Equal([BackslashRow], inMemory);
		Assert.Equal([BackslashRow], sqlite);

	}

	[Fact]
	public async Task A_percent_in_the_value_cannot_reach_the_backslash_row() {

		// Escape the wildcard before the escape character and the pattern becomes a literal 'a', a literal
		// backslash, a live wildcard and 'c' — which matches this row. Against the eight shared rows it matches
		// nothing, which is why the pre-existing assertion cannot see the difference.
		Assert.Empty(await this.InMemory(Query.Filter("name", "$ilike:a%c")));
		Assert.Empty(await this.Sqlite(Query.Filter("name", "$ilike:a%c")));

	}

	[Theory]
	[InlineData("$sw:")]
	[InlineData("$contains:")]
	public void A_value_ending_in_the_escape_character_still_leaves_the_pattern_ending_in_a_wildcard(string operatorToken) {

		using var context = fixture.CreateContext();

		string sql = context.Products.AsNoTracking()
			.ApplyPaginateFilters(Query.Filter("name", operatorToken + @"back\"), TestData.Config)
			.Query.ToQueryString();

		// Both pattern branches append the trailing wildcard after the escaped value, so a value ending in the
		// escape character can never produce a dangling escape. That is load-bearing rather than tidy: PostgreSQL
		// answers a pattern ending in its escape character with "LIKE pattern must not end with escape character",
		// an unhandled 500 on a value the caller chose. An "ends with" operator emitting the escaped value on its
		// own, or a refactor that moved the append, would reopen it.
		// The closing quote is part of the assertion: it is what makes the wildcard the pattern's last character
		// rather than merely present, and it holds wherever the pattern is rendered.
		Assert.Contains(@"back\\%'", sql, StringComparison.Ordinal);

	}

}
