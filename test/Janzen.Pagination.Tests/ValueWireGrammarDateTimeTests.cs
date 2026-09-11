namespace Janzen.Pagination.Tests;

/// <summary>
///     Pinned ISO forms for <see cref="DateTime" /> / <see cref="DateTimeOffset" /> and pinned colon patterns
///     for <see cref="TimeSpan" /> — the same reasoning that already governs <see cref="DateOnly" /> and
///     <see cref="TimeOnly" />: a parser that completes a partial value answers a question the caller did not
///     ask. In memory, because SQLite translates no <see cref="DateTimeOffset" /> comparison at all.
/// </summary>
public sealed class ValueWireGrammarDateTimeTests {

	public sealed class Event {

		public int Id { get; set; }

		public DateTime StartsAt { get; set; }

		public DateTimeOffset ObservedAt { get; set; }

		public TimeSpan Elapsed { get; set; }

	}

	public sealed record EventDto(int Id);

	private readonly static PaginateConfig<Event> Config = PaginateConfig<Event>.Create(b => b
		.WithLimits(10, 10)
		.WithTieBreaker(e => e.Id)
		.Filterable("startsAt", e => e.StartsAt,
			PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThanOrEqual)
		.Filterable("observedAt", e => e.ObservedAt,
			PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThanOrEqual)
		.Filterable("elapsed", e => e.Elapsed,
			PaginateFilterOperator.Eq, PaginateFilterOperator.LessThanOrEqual));

	/// <remarks>
	///     Row 1 is the instant every accepted spelling below resolves to. Row 2 carries five days, which is what
	///     <c>$lte:24:00:00</c> reaches today by reading the hour component as a day count. Row 3 is negative, so
	///     the sign the exact patterns cannot express on their own stays testable.
	/// </remarks>
	private static Task<PaginatedResponse<EventDto>> PageAsync(string field, string criterion) {

		IQueryable<Event> events = new List<Event> {
			new() {
				Id = 1, StartsAt = new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc),
				ObservedAt = new DateTimeOffset(2026, 1, 3, 10, 0, 0, TimeSpan.Zero), Elapsed = TimeSpan.FromHours(2)
			},
			new() {
				Id = 2, StartsAt = new DateTime(2026, 6, 30, 23, 30, 0, DateTimeKind.Utc),
				ObservedAt = new DateTimeOffset(2026, 6, 30, 23, 30, 0, TimeSpan.Zero), Elapsed = TimeSpan.FromDays(5)
			},
			new() {
				Id = 3, StartsAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				ObservedAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), Elapsed = TimeSpan.FromHours(-2)
			}
		}.AsQueryable();

		return events.PaginateAsync<Event, EventDto>(
			new PaginateQuery { Filters = new Dictionary<string, IReadOnlyList<string>> { [field] = [criterion] } },
			Config, null, TestContext.Current.CancellationToken
		);

	}

	private static async Task<string> RejectsAsync(string field, string criterion) {
		var exception = await Assert.ThrowsAsync<PaginateQueryException>(() => PageAsync(field, criterion));
		return exception.Message;
	}

	private static async Task<int[]> IdsAsync(string field, string criterion) {
		return [.. (await PageAsync(field, criterion)).Items.Select(item => item.Id)];
	}

	/// <remarks>
	///     The value the caller stores in a bookmark means a different instant tomorrow: <c>Parse</c> completes
	///     the missing date from the current clock, so <c>$gte:10:00</c> is "10:00 today" and nothing says so.
	/// </remarks>
	[Theory]
	[InlineData("startsAt")]
	[InlineData("observedAt")]
	public async Task A_timestamp_refuses_a_value_with_no_date(string field) {
		Assert.Equal($"Value '10:00' is not valid for '{field}'.", await RejectsAsync(field, "$gte:10:00"));
	}

	[Theory]
	[InlineData("12/31/2026")]
	[InlineData("2026-01-03 10:00:00")]
	[InlineData("2026-01-03T9:00")]
	[InlineData("2026")]
	public async Task A_timestamp_refuses_the_non_iso_invariant_forms(string value) {
		Assert.Equal($"Value '{value}' is not valid for 'startsAt'.", await RejectsAsync("startsAt", $"$eq:{value}"));
	}

	[Theory]
	[InlineData("2026-01-03T10:00")]
	[InlineData("2026-01-03T10:00:00")]
	[InlineData("2026-01-03T10:00:00.0000000")]
	[InlineData("2026-01-03T10:00:00Z")]
	[InlineData("2026-01-03T12:00:00+02:00")]
	[InlineData("2026-01-03T05:00:00-05:00")]
	public async Task A_timestamp_accepts_the_pinned_iso_forms(string value) {

		int[] local = await IdsAsync("startsAt", $"$eq:{value}");
		int[] offset = await IdsAsync("observedAt", $"$eq:{value}");

		Assert.Equal([1], local);
		Assert.Equal([1], offset);

	}

	[Fact]
	public async Task A_timestamp_accepts_a_bare_iso_date_as_midnight() {

		int[] ids = await IdsAsync("startsAt", "$gte:2026-01-03");

		Assert.Equal([1, 2, 3], ids);

	}

	/// <remarks>
	///     <c>TimeSpan.TryParse</c> reads the colon form as <c>d.hh:mm:ss</c> once the first component exceeds 23,
	///     so <c>$lte:24:00:00</c> selects everything within twenty-four <b>days</b> — a set twenty-four times
	///     wider than the caller asked for — while <c>$lte:25:30:00</c> is already a <c>400</c>.
	/// </remarks>
	[Theory]
	[InlineData("24:00:00")]
	[InlineData("25:30:00")]
	[InlineData("99:00")]
	public async Task A_duration_refuses_an_hour_component_above_23(string value) {
		Assert.Equal($"Value '{value}' is not valid for 'elapsed'.", await RejectsAsync("elapsed", $"$lte:{value}"));
	}

	/// <remarks>Days keep their spelling in the ISO leg, where <c>P5D</c> says so without overloading the hour slot.</remarks>
	[Fact]
	public async Task A_duration_refuses_the_day_carrying_colon_form() {

		string message = await RejectsAsync("elapsed", "$eq:5.00:00:00");
		int[] ids = await IdsAsync("elapsed", "$eq:P5D");

		Assert.Equal("Value '5.00:00:00' is not valid for 'elapsed'.", message);
		Assert.Equal([2], ids);

	}

	[Theory]
	[InlineData("2:00", 1)]
	[InlineData("02:00", 1)]
	[InlineData("2:00:00", 1)]
	[InlineData("2:0:0", 1)]
	[InlineData("2:00:00.0000000", 1)]
	[InlineData(" 2:00:00 ", 1)]
	[InlineData("PT2H", 1)]
	[InlineData("-2:00:00", 3)]
	[InlineData("-PT2H", 3)]
	public async Task A_duration_keeps_the_colon_and_iso_forms(string value, int expected) {

		int[] ids = await IdsAsync("elapsed", $"$eq:{value}");

		Assert.Equal([expected], ids);

	}

}
