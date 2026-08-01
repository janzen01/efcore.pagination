using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Janzen.Pagination.Tests.Support;

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options) {

	public DbSet<Product> Products => this.Set<Product>();

	public DbSet<Category> Categories => this.Set<Category>();

	public DbSet<Review> Reviews => this.Set<Review>();

}

/// <summary>
///     A SQLite in-memory database seeded once per test class. This is the leg that exercises the engine's
///     <c>UseDatabaseFunctions</c> path — real SQL translation, <c>EF.Functions.Like</c> and <c>EF.Parameter</c> —
///     which an <see cref="IQueryable" /> over a list cannot reach.
/// </summary>
/// <remarks>
///     Two provider limits shape what may be asserted here, and neither is the library's doing (both reproduce
///     with a plain <c>Where</c> and no engine involved):
///     <list type="bullet">
///         <item>EF Core's SQLite provider cannot translate <c>DateTimeOffset</c> comparisons at all, so every
///         date filter lives in <see cref="InMemoryTests" />.</item>
///         <item>Decimals are stored as TEXT and compare lexically, so <c>Price</c> is only ever tested for
///         equality — ordering and ranges use <c>Rank</c>.</item>
///     </list>
/// </remarks>
public sealed class SqliteFixture : IAsyncLifetime {

	private SqliteConnection _connection = null!;
	private DbContextOptions<TestDbContext> _options = null!;

	public async ValueTask InitializeAsync() {

		// The :memory: database lives as long as its connection, so the fixture owns one for its lifetime.
		_connection = new SqliteConnection("Filename=:memory:");
		await _connection.OpenAsync();

		_options = new DbContextOptionsBuilder<TestDbContext>()
			.UseSqlite(_connection)
			// Price exists for equality only; SQLite compares decimals lexically, so nothing orders or
			// ranges over it and the warning about that is noise here.
			.ConfigureWarnings(w => w.Ignore(SqliteEventId.CompositeKeyWithValueGeneration))
			.Options;

		await using var context = this.CreateContext();
		await context.Database.EnsureCreatedAsync();
		context.Products.AddRange(TestData.Products());
		await context.SaveChangesAsync();

	}

	public TestDbContext CreateContext() { return new TestDbContext(_options); }

	/// <summary>A fresh, untracked query root — every test starts from the same eight rows.</summary>
	public IQueryable<Product> Products(TestDbContext context) { return context.Products.AsNoTracking(); }

	public async ValueTask DisposeAsync() { await _connection.DisposeAsync(); }

}
