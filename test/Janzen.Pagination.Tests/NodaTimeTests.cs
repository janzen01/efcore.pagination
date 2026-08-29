using Janzen.Pagination.NodaTime;

using NodaTime;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The NodaTime add-on package, which shipped without a single test until 10.0.3. Everything here runs on the
///     in-memory leg: SQLite has no mapping for these types, and the registrations under test are the engine's own
///     (parsing, leaf classification, projection conversion) rather than anything provider-specific.
/// </summary>
/// <remarks>
///     <see cref="PaginateNodaTime.Register" /> is process-wide and idempotent, so calling it here is safe and is
///     what every other test in this class relies on having happened.
/// </remarks>
public sealed class NodaTimeTests {

	public sealed class Event {

		public int Id { get; set; }

		public Instant OccurredAt { get; set; }

		public Instant? ArchivedAt { get; set; }

		public LocalDate Day { get; set; }

		public LocalDateTime DayTime { get; set; }

		public LocalTime OpensAt { get; set; }

		public OffsetDateTime Scheduled { get; set; }

		public Duration Length { get; set; }

		public YearMonth Period { get; set; }

	}

	public sealed record EventDto(int Id);

	/// <summary>Every non-nullable NodaTime → BCL pair the package registers, in one projection.</summary>
	public sealed record EventConvertedDto(int Id, DateTimeOffset OccurredAt, DateOnly Day, DateTime DayTime, TimeOnly OpensAt, DateTimeOffset Scheduled);

	/// <summary>Non-nullable source onto a nullable target — the second of the three legal combinations.</summary>
	public sealed record EventWidenedDto(int Id, DateTimeOffset? OccurredAt);

	/// <summary>Nullable source onto a nullable target — the third.</summary>
	public sealed record EventNullableDto(int Id, DateTimeOffset? ArchivedAt);

	/// <summary>Nullable source onto a non-nullable target — deliberately refused, so the engine explains it.</summary>
	public sealed record EventNarrowedDto(int Id, DateTimeOffset ArchivedAt);

	private readonly static PaginateConfig<Event> Config = PaginateConfig<Event>.Create(b => b
		.WithLimits(50, 50)
		.Sortable("occurredAt", e => e.OccurredAt)
		.DefaultSortBy("occurredAt")
		.WithTieBreaker(e => e.Id)
		.Filterable("occurredAt", e => e.OccurredAt,
			PaginateFilterOperator.Eq, PaginateFilterOperator.Between,
			PaginateFilterOperator.GreaterThan, PaginateFilterOperator.LessThan)
		.Filterable("archivedAt", e => e.ArchivedAt, PaginateFilterOperator.Null, PaginateFilterOperator.Eq)
		.Filterable("day", e => e.Day, PaginateFilterOperator.Eq)
		.Filterable("dayTime", e => e.DayTime, PaginateFilterOperator.Eq)
		.Filterable("opensAt", e => e.OpensAt, PaginateFilterOperator.Eq)
		.Filterable("scheduled", e => e.Scheduled, PaginateFilterOperator.Eq)
		.Filterable("length", e => e.Length, PaginateFilterOperator.Eq)
		.Filterable("period", e => e.Period, PaginateFilterOperator.Eq));

	static NodaTimeTests() { PaginateNodaTime.Register(); }

	/// <summary>Three events one day apart, so a range assertion can name the middle one.</summary>
	private static IQueryable<Event> Events() {

		return new List<Event> {
			new() {
				Id = 1, OccurredAt = Instant.FromUtc(2026, 8, 1, 10, 0), ArchivedAt = null,
				Day = new LocalDate(2026, 8, 1), DayTime = new LocalDateTime(2026, 8, 1, 10, 0),
				OpensAt = new LocalTime(9, 0), Scheduled = new OffsetDateTime(new LocalDateTime(2026, 8, 1, 10, 0), Offset.FromHours(2)),
				Length = Duration.FromMinutes(150), Period = new YearMonth(2026, 8)
			},
			new() {
				Id = 2, OccurredAt = Instant.FromUtc(2026, 8, 2, 10, 0), ArchivedAt = Instant.FromUtc(2026, 9, 1, 0, 0),
				Day = new LocalDate(2026, 8, 2), DayTime = new LocalDateTime(2026, 8, 2, 10, 0),
				OpensAt = new LocalTime(10, 0), Scheduled = new OffsetDateTime(new LocalDateTime(2026, 8, 2, 10, 0), Offset.FromHours(2)),
				Length = Duration.FromMinutes(60), Period = new YearMonth(2026, 9)
			},
			new() {
				Id = 3, OccurredAt = Instant.FromUtc(2026, 8, 3, 10, 0), ArchivedAt = null,
				Day = new LocalDate(2026, 8, 3), DayTime = new LocalDateTime(2026, 8, 3, 10, 0),
				OpensAt = new LocalTime(11, 0), Scheduled = new OffsetDateTime(new LocalDateTime(2026, 8, 3, 10, 0), Offset.FromHours(2)),
				Length = Duration.FromMinutes(30), Period = new YearMonth(2026, 10)
			}
		}.AsQueryable();

	}

	private static Task<PaginatedResponse<TResult>> PageAsync<TResult>(PaginateQuery request) {
		return Events().PaginateAsync<Event, TResult>(request, Config, null, TestContext.Current.CancellationToken);
	}

	private static Task<PaginatedResponse<EventDto>> FilterAsync(string field, string criterion) {
		return PageAsync<EventDto>(new PaginateQuery {
			Filters = new Dictionary<string, IReadOnlyList<string>> { [field] = [criterion] }
		});
	}

	private static async Task<string> RejectsAsync(string field, string criterion) {
		var exception = await Assert.ThrowsAsync<PaginateQueryException>(() => FilterAsync(field, criterion));
		return exception.Message;
	}

	[Theory]
	[InlineData("occurredAt", "$eq:2026-08-02T10:00:00Z", 2)]
	[InlineData("day", "$eq:2026-08-02", 2)]
	[InlineData("dayTime", "$eq:2026-08-02T10:00:00", 2)]
	[InlineData("opensAt", "$eq:10:00:00", 2)]
	[InlineData("scheduled", "$eq:2026-08-02T10:00:00+02:00", 2)]
	[InlineData("period", "$eq:2026-09", 2)]
	public async Task Every_registered_type_parses_and_filters(string field, string criterion, int expected) {
		Assert.Equal([expected], (await FilterAsync(field, criterion)).Items.Select(item => item.Id));
	}

	[Theory]
	[InlineData("occurredAt", "instant")]
	[InlineData("day", "local date")]
	[InlineData("dayTime", "local date-time")]
	[InlineData("opensAt", "local time")]
	[InlineData("scheduled", "offset date-time")]
	[InlineData("period", "year-month")]
	[InlineData("length", "duration")]
	public async Task Every_registered_type_reports_its_own_name_when_the_text_is_wrong(string field, string displayName) {
		Assert.Equal($"Value 'nope' is not a valid {displayName}.", await RejectsAsync(field, "$eq:nope"));
	}

	[Fact]
	public async Task An_instant_also_accepts_an_offset_form() {

		// 12:00+02:00 is the same instant as 10:00Z — unambiguous, and a 400 before 10.0.3.
		Assert.Equal([2], (await FilterAsync("occurredAt", "$eq:2026-08-02T12:00:00+02:00")).Items.Select(item => item.Id));

	}

	[Fact]
	public async Task An_instant_still_refuses_a_bare_date() {

		// Accepting it would silently mean midnight; explicit day semantics are a separate feature.
		Assert.Equal("Value '2026-08-02' is not a valid instant.", await RejectsAsync("occurredAt", "$eq:2026-08-02"));

	}

	[Theory]
	[InlineData("2:30:00")]
	[InlineData("PT2H30M")]
	public async Task A_duration_reads_both_the_roundtrip_and_the_iso_form(string value) {
		Assert.Equal([1], (await FilterAsync("length", $"$eq:{value}")).Items.Select(item => item.Id));
	}

	[Fact]
	public async Task An_empty_value_on_a_nullable_instant_matches_null() {
		Assert.Equal([1, 3], (await FilterAsync("archivedAt", "$eq:")).Items.Select(item => item.Id));
	}

	[Fact]
	public async Task Instants_compare_with_range_operators() {
		Assert.Equal([2, 3], (await FilterAsync("occurredAt", "$gt:2026-08-01T10:00:00Z")).Items.Select(item => item.Id));
	}

	[Fact]
	public async Task Every_conversion_projects_onto_its_bcl_counterpart() {

		var first = (await PageAsync<EventConvertedDto>(new PaginateQuery { Limit = 1 })).Items.Single();

		Assert.Equal(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero), first.OccurredAt);
		Assert.Equal(new DateOnly(2026, 8, 1), first.Day);
		Assert.Equal(new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Unspecified), first.DayTime);
		Assert.Equal(new TimeOnly(9, 0), first.OpensAt);
		Assert.Equal(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(2)), first.Scheduled);

	}

	[Fact]
	public async Task A_non_nullable_source_projects_onto_a_nullable_target() {

		var first = (await PageAsync<EventWidenedDto>(new PaginateQuery { Limit = 1 })).Items.Single();

		Assert.Equal(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero), first.OccurredAt);

	}

	[Fact]
	public async Task A_nullable_source_keeps_its_null_through_the_projection() {

		var items = (await PageAsync<EventNullableDto>(new PaginateQuery())).Items;

		Assert.Null(items.Single(item => item.Id == 1).ArchivedAt);
		Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), items.Single(item => item.Id == 2).ArchivedAt);

	}

	[Fact]
	public async Task A_nullable_source_onto_a_non_nullable_target_is_refused_with_a_clear_message() {

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => PageAsync<EventNarrowedDto>(new PaginateQuery()));

		// The engine renders an open generic by its CLR name, so 'Nullable`1' is what a reader sees here.
		Assert.Equal("Cannot automatically project 'Event.ArchivedAt' from 'Nullable`1' to 'DateTimeOffset'.", exception.Message);

	}

	[Fact]
	public async Task Registering_twice_changes_nothing() {

		PaginateNodaTime.Register();
		PaginateNodaTime.Register();

		Assert.Equal([2], (await FilterAsync("day", "$eq:2026-08-02")).Items.Select(item => item.Id));

	}

}
