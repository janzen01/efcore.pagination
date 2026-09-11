using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Janzen.Pagination.Tests;

/// <summary>The shared eight rows plus a non-ASCII case pair, which is the only shape the divergence is visible on.</summary>
public sealed class AccentFixture : IAsyncLifetime {

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
		context.Products.AddRange(CaseParityTests.Rows());
		await context.SaveChangesAsync();

	}

	public TestDbContext CreateContext() { return new TestDbContext(_options); }

	public async ValueTask DisposeAsync() { await _connection.DisposeAsync(); }

}

/// <summary>
///     Where the two legs agree on case and where they do not. The in-memory leg matches
///     <c>$ilike</c> / <c>$sw</c> / <c>$contains</c> / <c>search</c> with <c>OrdinalIgnoreCase</c>, which is
///     culture-free and covers the whole of Unicode; the database leg answers with whatever the provider's
///     <c>LIKE</c> does. They look identical on ASCII and are not the same rule, so the suite pinned only the
///     agreement and never the disagreement.
///     <para>
///         The disagreement this class pins is SQLite's: its <c>LIKE</c> folds case for ASCII only, so
///         <c>Ä</c> and <c>ä</c> are different characters to it and the same character in memory. That needs no
///         PostgreSQL and is testable here. The other half of the same divergence is not: PostgreSQL's portable
///         <c>LIKE</c> is case-sensitive even on ASCII, so a consumer who develops against the in-memory leg
///         ships a case-sensitive <c>$ilike</c> without <c>.UsePostgreSql()</c> and sees no error anywhere. That
///         half is documented rather than asserted, because the suite has no PostgreSQL leg to assert it on.
///     </para>
/// </summary>
public sealed class CaseParityTests(AccentFixture fixture) : IClassFixture<AccentFixture> {

	private const int UpperAccented = 10;
	private const int LowerAccented = 11;

	internal static List<Product> Rows() {

		var rows = TestData.Products();
		rows.Add(new Product { Id = UpperAccented, Name = "Äpfel", Status = ProductStatus.Active, Rank = 100 });
		rows.Add(new Product { Id = LowerAccented, Name = "äpfel", Status = ProductStatus.Active, Rank = 110 });

		return rows;

	}

	private static async Task<int[]> Ids(IQueryable<Product> source, PaginateQuery request) {
		return [.. (await source.PageAsync<ProductDto>(request)).Items.Select(item => item.Id).Order()];
	}

	private Task<int[]> InMemory(PaginateQuery request) { return Ids(Rows().AsQueryable(), request); }

	private async Task<int[]> Sqlite(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Ids(context.Products.AsNoTracking(), request);
	}

	[Theory]
	[InlineData("$ilike:apple")]
	[InlineData("$ilike:APPLE")]
	[InlineData("$sw:apple")]
	public async Task On_ascii_both_legs_match_regardless_of_case(string criterion) {

		// APPLE and "apple pie" differ only in case, so a case-sensitive leg would answer with one of them.
		int[] inMemory = await this.InMemory(Query.Filter("name", criterion));
		int[] sqlite = await this.Sqlite(Query.Filter("name", criterion));

		Assert.Equal([7, 8], inMemory);
		Assert.Equal([7, 8], sqlite);

	}

	[Fact]
	public async Task On_non_ascii_the_in_memory_leg_folds_case_and_sqlite_does_not() {

		// OrdinalIgnoreCase folds the whole of Unicode; SQLite's built-in LIKE folds ASCII only, which is a
		// documented limit of the provider rather than anything the engine chose. A consumer developing against
		// the in-memory leg therefore sees a match the database will not produce.
		int[] inMemory = await this.InMemory(Query.Filter("name", "$ilike:äpfel"));
		int[] sqlite = await this.Sqlite(Query.Filter("name", "$ilike:äpfel"));

		Assert.Equal([UpperAccented, LowerAccented], inMemory);
		Assert.Equal([LowerAccented], sqlite);

	}

	[Fact]
	public async Task The_same_split_applies_to_free_text_search() {

		// search routes through the same two branches, so it inherits the same divergence -- worth pinning
		// separately because the two paths reach BuildLike from different call sites.
		int[] inMemory = await this.InMemory(Query.Search("äpfel", "name"));
		int[] sqlite = await this.Sqlite(Query.Search("äpfel", "name"));

		Assert.Equal([UpperAccented, LowerAccented], inMemory);
		Assert.Equal([LowerAccented], sqlite);

	}

}
