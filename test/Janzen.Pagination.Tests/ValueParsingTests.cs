using System.Globalization;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The types the parse table gained in 10.0.3, the <see cref="IParsable{TSelf}" /> fallback that needs no
///     registration at all, and the precedence the registry now has over the built-ins.
/// </summary>
/// <remarks>
///     <see cref="DateOnly" /> and <see cref="TimeOnly" /> are exercised against SQLite, which translates
///     comparisons over them (unlike <see cref="DateTimeOffset" />, see <see cref="SqliteFixture" />).
///     <see cref="TimeSpan" /> is equality-only for the same reason <c>Price</c> is: SQLite stores it as TEXT and
///     compares it lexically.
/// </remarks>
public sealed class ValueParsingTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	private readonly static PaginateConfig<Product> Config = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.Sortable("releasedOn", p => p.ReleasedOn)
		.Sortable("opensAt", p => p.OpensAt)
		.DefaultSortBy("releasedOn")
		.WithTieBreaker(p => p.Id)
		.Filterable("releasedOn", p => p.ReleasedOn,
			PaginateFilterOperator.Eq, PaginateFilterOperator.Between,
			PaginateFilterOperator.GreaterThan, PaginateFilterOperator.LessThan)
		.Filterable("retiredOn", p => p.RetiredOn, PaginateFilterOperator.Eq, PaginateFilterOperator.Null)
		.Filterable("opensAt", p => p.OpensAt, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThan)
		.Filterable("warranty", p => p.Warranty, PaginateFilterOperator.Eq));

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request, Config);
	}

	private async Task<string> Rejects(PaginateQuery request) {
		await using var context = fixture.CreateContext();
		return await Assertions.RejectsAsync(() => SqliteFixture.Products(context).PageAsync<ProductDto>(request, Config));
	}

	[Fact]
	public async Task A_date_only_value_filters() {
		Assertions.HasIds(await this.Page(Query.Filter("releasedOn", "$eq:2026-01-03")), 3);
	}

	[Fact]
	public async Task A_date_only_value_ranges() {
		Assertions.HasIds(await this.Page(Query.Filter("releasedOn", "$btw:2026-01-03,2026-01-05")), 3, 4, 5);
	}

	[Fact]
	public async Task A_date_only_value_sorts() {

		var page = await this.Page(new PaginateQuery { SortBy = ["releasedOn:DESC"] });

		Assert.Equal([8, 7, 6, 5, 4, 3, 2, 1], page.Items.Select(item => item.Id));

	}

	[Fact]
	public async Task A_time_only_value_filters() {
		Assertions.HasIds(await this.Page(Query.Filter("opensAt", "$eq:12:00:00")), 4);
	}

	[Fact]
	public async Task A_time_span_value_filters() {
		Assertions.HasIds(await this.Page(Query.Filter("warranty", "$eq:02:00:00")), 2);
	}

	[Fact]
	public async Task A_time_span_also_reads_the_iso_8601_form() {

		// Same row as the colon form above: PT2H is what survives a URL without percent-encoding.
		Assertions.HasIds(await this.Page(Query.Filter("warranty", "$eq:PT2H")), 2);

	}

	[Fact]
	public async Task An_empty_value_on_a_nullable_date_only_matches_null() {

		// Ids 1-5, 7 and 8 have no RetiredOn; only the discontinued row does.
		Assertions.HasIds(await this.Page(Query.Filter("retiredOn", "$eq:")), 1, 2, 3, 4, 5, 7, 8);

	}

	[Theory]
	[InlineData("releasedOn", "nope", "DateOnly")]
	[InlineData("opensAt", "nope", "TimeOnly")]
	[InlineData("warranty", "nope", "TimeSpan")]
	public async Task An_unparseable_value_reports_its_type(string field, string value, string typeName) {
		Assert.Equal($"Value '{value}' is not valid for type '{typeName}'.", await this.Rejects(Query.Filter(field, $"$eq:{value}")));
	}

	[Fact]
	public async Task A_date_only_rejects_a_date_time() {

		// DateOnly.Parse would accept this and silently drop the time, matching the whole day for a caller who
		// asked about one moment of it. The parse table pins the exact ISO form instead.
		Assert.Equal(
			"Value '2026-01-03T10:00:00' is not valid for type 'DateOnly'.",
			await this.Rejects(Query.Filter("releasedOn", "$eq:2026-01-03T10:00:00"))
		);

	}

	[Fact]
	public async Task A_time_only_rejects_a_date_time() {

		// The mirror image: TimeOnly.Parse reads the same string and throws the date away instead.
		Assert.Equal(
			"Value '2026-01-03T10:00:00' is not valid for type 'TimeOnly'.",
			await this.Rejects(Query.Filter("opensAt", "$eq:2026-01-03T10:00:00"))
		);

	}

	[Theory]
	[InlineData("09:00")]
	[InlineData("09:00:00")]
	[InlineData("09:00:00.000")]
	public async Task A_time_only_accepts_the_iso_time_forms(string value) {
		Assertions.HasIds(await this.Page(Query.Filter("opensAt", $"$eq:{value}")), 1);
	}

	[Fact]
	public async Task The_new_leaf_types_project_without_recursing_into_them() {

		await using var context = fixture.CreateContext();
		var page = await SqliteFixture.Products(context).PageAsync<ProductDatesDto>(new PaginateQuery { Limit = 1 }, Config);

		var first = page.Items.Single();

		Assert.Equal(new DateOnly(2026, 1, 1), first.ReleasedOn);
		Assert.Null(first.RetiredOn);
		Assert.Equal(new TimeOnly(9, 0), first.OpensAt);
		Assert.Equal(TimeSpan.FromHours(1), first.Warranty);

	}

}

/// <summary>
///     The two registry-facing behaviours, kept away from the fixture above because both are process-wide:
///     <see cref="Ticket" /> is declared here so nothing else can reach it, and the precedence test deliberately
///     overrides <see cref="sbyte" /> — a built-in no other test parses.
/// </summary>
public sealed class ValueParsingRegistryTests {

	/// <summary>A consumer value object that parses itself. Never registered anywhere — that is the point.</summary>
	public readonly record struct Ticket(int Number) : IParsable<Ticket> {

		public static Ticket Parse(string s, IFormatProvider? provider) {
			return TryParse(s, provider, out var result) ? result : throw new FormatException($"'{s}' is not a ticket.");
		}

		public static bool TryParse(string? s, IFormatProvider? provider, out Ticket result) {

			if (s is not null && s.StartsWith("T-", StringComparison.Ordinal)
			                  && int.TryParse(s.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) {
				result = new Ticket(number);
				return true;
			}

			result = default;
			return false;

		}

	}

	public sealed class Job {

		public int Id { get; set; }

		public Ticket Ticket { get; set; }

		public sbyte Priority { get; set; }

	}

	public sealed record JobDto(int Id);

	private readonly static PaginateConfig<Job> Config = PaginateConfig<Job>.Create(b => b
		.WithLimits(10, 10)
		.WithTieBreaker(j => j.Id)
		.Filterable("ticket", j => j.Ticket, PaginateFilterOperator.Eq)
		.Filterable("priority", j => j.Priority, PaginateFilterOperator.Eq));

	private static Task<PaginatedResponse<JobDto>> PageAsync(string field, string criterion) {

		IQueryable<Job> jobs = new List<Job> {
			new() { Id = 1, Ticket = new Ticket(1), Priority = 1 },
			new() { Id = 2, Ticket = new Ticket(2), Priority = 2 }
		}.AsQueryable();

		return jobs.PaginateAsync<Job, JobDto>(
			new PaginateQuery { Filters = new Dictionary<string, IReadOnlyList<string>> { [field] = [criterion] } },
			Config, null, TestContext.Current.CancellationToken
		);

	}

	[Fact]
	public async Task A_parsable_value_object_filters_with_no_registration_at_all() {

		var page = await PageAsync("ticket", "$eq:T-2");

		Assert.Equal([2], page.Items.Select(item => item.Id));

	}

	[Fact]
	public async Task A_parsable_value_object_reports_its_own_type_when_the_text_is_wrong() {

		var exception = await Assert.ThrowsAsync<PaginateQueryException>(() => PageAsync("ticket", "$eq:nope"));

		Assert.Equal("Value 'nope' is not valid for type 'Ticket'.", exception.Message);

	}

	[Fact]
	public async Task A_registered_parser_overrides_a_built_in_type() {

		// Consulted last, as it was before 10.0.3, this registration was a silent no-op and "high" would have been
		// rejected as a malformed sbyte. sbyte is used here precisely because no other test parses one — the
		// registry is process-wide, so an override of a busier type would leak across the suite.
		PaginateTypeSupport.RegisterValueParser(typeof(sbyte), value => value == "high" ? (sbyte)2 : sbyte.Parse(value, CultureInfo.InvariantCulture));

		var page = await PageAsync("priority", "$eq:high");

		Assert.Equal([2], page.Items.Select(item => item.Id));

	}

}
