using Microsoft.EntityFrameworkCore;

using System.Globalization;

namespace Janzen.Pagination.Tests;

/// <summary>
///     How a filter criterion turns into a comparison: what an empty value means, which ordering the in-memory leg
///     applies to a string range, and what happens when the parsed value has no equality operator. Each of these
///     used to answer something other than a clean 400 — the null subset, the host culture's answer, or a 500.
/// </summary>
public sealed class FilterComparisonSemanticsTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>Only <c>Eq</c> on a nullable FK, so "an empty value reached $null without being granted it" is visible.</summary>
	private readonly static PaginateConfig<Product> EmptyValueConfig = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.Filterable("categoryId", p => p.CategoryId, PaginateFilterOperator.Eq)
		.Filterable("categoryKey", p => p.Category!.Id, PaginateFilterOperator.Eq)
		.Filterable("externalId", p => p.ExternalId, PaginateFilterOperator.Between));

	private static IQueryable<Product> InMemory() { return TestData.Products().AsQueryable(); }

	#region PAR-06 — an empty value is not an implicit $null

	[Fact]
	public async Task An_empty_value_on_a_nullable_field_is_rejected_rather_than_matching_null() {

		Assert.Equal(
			"Filter 'categoryId' requires a value; use '$null' to match rows with no value.",
			await Assertions.RejectsAsync(() => InMemory().PageAsync<ProductDto>(Query.Filter("categoryId", "$eq:"), EmptyValueConfig)));

	}

	[Fact]
	public async Task An_empty_value_is_rejected_on_the_database_leg_too() {

		await using var context = fixture.CreateContext();

		Assert.Equal(
			"Filter 'categoryId' requires a value; use '$null' to match rows with no value.",
			await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(Query.Filter("categoryId", "$eq:"), EmptyValueConfig)));

	}

	/// <summary>
	///     The two legs used to disagree here: in memory the null-safe rewriter lifts <c>Category.Id</c> to
	///     <see cref="Nullable{T}" />, so the empty value converted to null and answered 200 with the parentless
	///     rows, while every relational provider converted against <c>int</c> and answered 400.
	/// </summary>
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task An_empty_value_on_a_nested_non_nullable_member_answers_the_same_on_both_legs(bool database) {

		await using var context = fixture.CreateContext();
		var source = database ? SqliteFixture.Products(context) : InMemory();

		Assert.Equal(
			"Filter 'categoryKey' requires a value; use '$null' to match rows with no value.",
			await Assertions.RejectsAsync(() => source.PageAsync<ProductDto>(Query.Filter("categoryKey", "$eq:"), EmptyValueConfig)));

	}

	#endregion

	#region EF-01 — the in-memory leg orders strings invariantly

	/// <summary>
	///     <c>Å</c> sorts with <c>A</c> in the invariant culture and after <c>Z</c> in Swedish, which is what makes
	///     these two assert anything: before the fix the in-memory leg ran <c>String.CompareTo</c> and
	///     <c>Comparer&lt;string&gt;.Default</c>, both of which answer from <see cref="CultureInfo.CurrentCulture" />.
	/// </summary>
	private readonly static PaginateConfig<Product> OrderingConfig = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.Sortable("name", p => p.Name)
		// Every row shares a rank, so ordering by it first leaves the string key deciding as a ThenBy.
		.Sortable("rank", p => p.Rank)
		.WithTieBreaker(p => p.Id)
		.Filterable("name", p => p.Name, PaginateFilterOperator.GreaterThan));

	private static IQueryable<Product> Letters() {
		return new List<Product> {
			new() { Id = 1, Name = "Zebra" },
			new() { Id = 2, Name = "Ångstrom" },
			new() { Id = 3, Name = "Apple" }
		}.AsQueryable();
	}

	private static async Task<T> UnderSwedishCulture<T>(Func<Task<T>> act) {

		var original = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("sv-SE");

		try {
			return await act();
		} finally {
			CultureInfo.CurrentCulture = original;
		}

	}

	[Fact]
	public async Task A_string_range_does_not_depend_on_the_host_culture() {

		var page = await UnderSwedishCulture(() => Letters().PageAsync<ProductDto>(Query.Filter("name", "$gt:Z"), OrderingConfig));

		// Swedish puts Ångstrom after Z and would return it too.
		Assertions.HasIds(page, 1);

	}

	[Theory]
	[InlineData("name:ASC")]
	[InlineData("rank:ASC", "name:ASC")]
	public async Task A_string_sort_does_not_depend_on_the_host_culture(params string[] sortBy) {

		// The second case makes the string key a ThenBy, which is the other half of the ordering branch.
		var page = await UnderSwedishCulture(() => Letters().PageAsync<ProductDto>(Query.Sort(sortBy), OrderingConfig));

		// Swedish would answer Apple, Zebra, Ångstrom.
		Assertions.HasIds(page, 2, 3, 1);

	}

	#endregion

	#region PAR-16 — $eq on a value type with no equality operator

	/// <summary>A plain struct: no <c>op_Equality</c>, unlike the <c>record struct</c> the compiler writes one for.</summary>
	public struct Weight {

		public decimal Kilograms { get; set; }

	}

	public sealed class Crate {

		public int Id { get; set; }

		public Weight Weight { get; set; }

	}

	public sealed record CrateDto(int Id);

	private readonly static PaginateConfig<Crate> CrateConfig = PaginateConfig<Crate>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(c => c.Id)
		.Filterable("weight", c => c.Weight, PaginateFilterOperator.Eq));

	[Fact]
	public async Task Equality_on_a_type_without_an_equality_operator_is_rejected_rather_than_failing_the_request() {

		PaginateTypeSupport.RegisterValueParser(typeof(Weight), value => new Weight { Kilograms = decimal.Parse(value, CultureInfo.InvariantCulture) });

		IQueryable<Crate> crates = new List<Crate> { new() { Id = 1, Weight = new Weight { Kilograms = 10 } } }.AsQueryable();

		Assert.Equal(
			"Filter 'weight' does not support operator '$eq' for type 'Weight'.",
			await Assertions.RejectsAsync(() => crates.PaginateAsync<Crate, CrateDto>(
				Query.Filter("weight", "$eq:10"), CrateConfig, null, TestContext.Current.CancellationToken)));

	}

	#endregion

	#region PERF-R04 — the hoisted CompareTo resolution still emits the same SQL

	[Fact]
	public void A_guid_range_parameterises_both_bounds() {

		using var context = fixture.CreateContext();

		string sql = SqliteFixture.Products(context)
			.ApplyPaginateFilters(Query.Filter("externalId", $"$btw:{TestData.ExternalId(3)},{TestData.ExternalId(6)}"), EmptyValueConfig)
			.Query
			.ToQueryString();

		string statement = string.Join(' ', sql
			.Split('\n')
			.Where(line => !line.TrimStart().StartsWith(".param", StringComparison.Ordinal))
			.SelectMany(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

		Assert.Contains("\"ExternalId\" >= @", statement, StringComparison.Ordinal);
		Assert.Contains("\"ExternalId\" <= @", statement, StringComparison.Ordinal);
		Assert.DoesNotContain(TestData.ExternalId(3).ToString(), statement, StringComparison.OrdinalIgnoreCase);

	}

	#endregion

}
