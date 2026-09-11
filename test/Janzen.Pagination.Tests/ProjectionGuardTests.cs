using Janzen.Pagination.EntityFrameworkCore.Engine;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>A self-referencing entity, the shape an org chart or a category tree has.</summary>
public sealed class Employee {

	public int Id { get; set; }

	public Employee? Manager { get; set; }

}

/// <summary>Mirrors <see cref="Employee" />, so building it naively recurses without end.</summary>
public sealed record EmployeeDto(int Id, EmployeeDto? Manager);

/// <summary>A sub-collection member, which only <c>PaginateSelectAsync</c> can produce.</summary>
public sealed record ProductWithReviewsDto(int Id, string Name, List<ReviewDto> Reviews);

/// <summary>Carries no information by construction: there is nothing for the builder to project into it.</summary>
public sealed record EmptyProjectionDto();

/// <summary>Two public constructors of equal maximum arity, both of which the source type could satisfy.</summary>
public sealed class TwoCtorDto {

	public TwoCtorDto(int id, string name) {
		this.Id = id;
		this.Name = name;
	}

	public TwoCtorDto(int rank, decimal price) {
		this.Rank = rank;
		this.Price = price;
	}

	public int Id { get; }

	public string Name { get; } = "";

	public int Rank { get; }

	public decimal Price { get; }

}

public class HiddenCodeBase {

	public string Code { get; set; } = "";

}

/// <summary>Hides the base <c>Code</c> with a member of a different type — legal C#, and the derived one is what a DTO names.</summary>
public sealed class HiddenCodeRow : HiddenCodeBase {

	public int Id { get; set; }

	public new int Code;

}

public sealed record HiddenCodeDto(int Id, int Code);

public sealed class Customer {

	public int Id { get; set; }

	public string Name { get; set; } = "";

}

/// <summary>
///     EF scaffolding's own default for an optional relationship: the FK is nullable, the navigation is not.
///     The CLR annotation therefore says "never null" for a row the database is free to leave without a parent.
/// </summary>
public sealed class Order {

	public int Id { get; set; }

	public int? CustomerId { get; set; }

	public Customer Customer { get; set; } = null!;

}

public sealed record CustomerDto(int Id, string Name);

public sealed record OrderDto(int Id, CustomerDto? Customer);

public sealed class OrphanDbContext(DbContextOptions<OrphanDbContext> options) : DbContext(options) {

	public DbSet<Order> Orders => this.Set<Order>();

	public DbSet<Customer> Customers => this.Set<Customer>();

}

/// <summary>Two orders, one of them without a customer — the row a seeded suite and a fresh staging database never have.</summary>
public sealed class OrphanFixture : IAsyncLifetime {

	private SqliteConnection _connection = null!;
	private DbContextOptions<OrphanDbContext> _options = null!;

	public async ValueTask InitializeAsync() {

		_connection = new SqliteConnection("Filename=:memory:");
		await _connection.OpenAsync();

		_options = new DbContextOptionsBuilder<OrphanDbContext>().UseSqlite(_connection).Options;

		await using var context = this.CreateContext();
		await context.Database.EnsureCreatedAsync();
		context.Orders.AddRange(Rows());
		await context.SaveChangesAsync();

	}

	public static List<Order> Rows() {
		return [
			new Order { Id = 1, CustomerId = 1, Customer = new Customer { Id = 1, Name = "ann" } },
			new Order { Id = 2 }
		];
	}

	public OrphanDbContext CreateContext() { return new OrphanDbContext(_options); }

	public async ValueTask DisposeAsync() { await _connection.DisposeAsync(); }

}

/// <summary>
///     What the automatic projection builder refuses, and the two shapes it used to resolve by reflection order.
///     Every case here produced a wrong answer rather than an error: an always-empty collection, an all-default
///     row, a crashed host, a column set chosen by source-file order, or a 500 on the first parentless row.
///     <para>
///         The rejections go through the builder directly rather than through an entry point. They are pure
///         build-time decisions with no query to run, and one of them terminated the test host before the guard
///         existed — <see cref="ProjectionTests" /> already covers that a rejection reaches the caller unwrapped.
///     </para>
/// </summary>
public sealed class ProjectionGuardTests(OrphanFixture fixture) : IClassFixture<OrphanFixture> {

	private readonly static PaginateConfig<Order> OrderConfig = PaginateConfig<Order>.Create(b => b
		.WithLimits(defaultLimit: 10, maxLimit: 50)
		.Sortable("id", o => o.Id)
		.DefaultSortBy("id")
		.WithTieBreaker(o => o.Id));

	private static string Rejects(Action act) { return Assert.Throws<InvalidOperationException>(act).Message; }

	[Fact]
	public void A_collection_member_is_refused_rather_than_built_empty() {

		// List<T>'s widest public constructor is (int capacity), whose parameter name resolves against the
		// navigation's own Capacity -- so the builder produced new List<ReviewDto>(item.Reviews.Capacity), an
		// always-empty list, and the query still paid a LEFT JOIN to read the capacity it then discarded.
		string message = Rejects(() => PaginateProjectionBuilder.Build<Product, ProductWithReviewsDto>());

		Assert.Equal("Cannot automatically project 'Product.Reviews' into a collection. Use PaginateSelectAsync for sub-collections.", message);

	}

	[Fact]
	public void A_target_with_no_constructor_parameters_is_refused() {

		// new TResult() is buildable and translates to SELECT 1, so the page came back with correct metadata
		// and every row all-default. Nothing in the SQL, the envelope or an analyzer says otherwise.
		string message = Rejects(() => PaginateProjectionBuilder.Build<Product, EmptyProjectionDto>());

		Assert.Equal("Type 'EmptyProjectionDto' exposes no public constructor with parameters for automatic projection.", message);

	}

	[Fact]
	public void A_recursive_target_is_refused_rather_than_recursed_forever() {

		// EmployeeDto.Manager is an EmployeeDto, which is neither assignable, nor convertible, nor a leaf -- so
		// the builder recursed into the same (Employee, EmployeeDto) pair until the stack ran out. A
		// StackOverflowException cannot be caught on .NET Core: the host process dies with no message naming
		// the DTO, and it dies on the first request that names the pair.
		string message = Rejects(() => PaginateProjectionBuilder.Build<Employee, EmployeeDto>());

		Assert.Equal("Cannot automatically project 'Employee.Manager' into 'EmployeeDto': the type is recursive.", message);

	}

	[Fact]
	public void Equal_arity_constructors_are_refused_rather_than_picked_by_declaration_order() {

		// Reflection does not promise an order, so which columns the API returns depended on which constructor
		// was written first in the DTO's source file.
		string message = Rejects(() => PaginateProjectionBuilder.Build<Product, TwoCtorDto>());

		Assert.Equal("Type 'TwoCtorDto' exposes 2 public constructors with 2 parameters; automatic projection needs exactly one.", message);

	}

	[Fact]
	public void A_hidden_member_binds_to_the_most_derived_declaration() {

		// Properties were enumerated before fields regardless of where they were declared, so the base
		// declaration won and the request failed with a type pair the consumer never wrote.
		var projection = PaginateProjectionBuilder.Build<HiddenCodeRow, HiddenCodeDto>();
		var row = new HiddenCodeRow { Id = 7, Code = 42 };

		var dto = projection.Compile().Invoke(row);

		Assert.Equal(7, dto.Id);
		Assert.Equal(42, dto.Code);

	}

	[Fact]
	public async Task An_orphan_row_projects_to_a_null_member_on_the_database_leg() {

		await using var context = fixture.CreateContext();

		var page = await context.Orders.AsNoTracking()
			.PaginateAsync<Order, OrderDto>(new PaginateQuery(), OrderConfig, null, TestContext.Current.CancellationToken);

		Assert.Equal("ann", page.Items[0].Customer?.Name);
		Assert.Null(page.Items[1].Customer);

	}

	[Fact]
	public async Task An_orphan_row_projects_to_a_null_member_in_memory() {

		var page = await OrphanFixture.Rows().AsQueryable()
			.PaginateAsync<Order, OrderDto>(new PaginateQuery(), OrderConfig, null, TestContext.Current.CancellationToken);

		Assert.Equal("ann", page.Items[0].Customer?.Name);
		// The two legs threw different exception types here -- InvalidOperationException from the materializer,
		// NullReferenceException in memory -- so the failure could not even be caught uniformly.
		Assert.Null(page.Items[1].Customer);

	}

	[Fact]
	public void A_nullable_source_still_reaches_a_nullable_target_through_the_same_guard() {

		// The guard now follows the target parameter, so the case that already worked must keep working: a
		// nullable navigation into a nullable DTO member is still one null-propagating conditional, not two.
		var projection = PaginateProjectionBuilder.Build<Product, ProductWithCategoryDto>();

		var conditionals = new ConditionalCounter();
		conditionals.Visit(projection.Body);

		Assert.Equal(1, conditionals.Count);

	}

	private sealed class ConditionalCounter : ExpressionVisitor {

		public int Count { get; private set; }

		protected override Expression VisitConditional(ConditionalExpression node) {
			this.Count++;
			return base.VisitConditional(node);
		}

	}

}
