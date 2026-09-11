namespace Janzen.Pagination.Tests;

/// <summary>
///     One numeric grammar for every built-in numeric type. <c>reference/query-string/</c> promises "invariant
///     culture — <c>.</c> as the decimal separator" for the whole family, and <c>reference/errors/</c> lists a
///     number that overflows the type as a <c>400</c>; only the integer types delivered either, because the BCL
///     throws there by itself.
/// </summary>
public sealed class ValueWireGrammarNumericTests {

	public sealed class Reading {

		public int Id { get; set; }

		public decimal Amount { get; set; }

		public double Ratio { get; set; }

		public float Scale { get; set; }

	}

	public sealed record ReadingDto(int Id);

	private readonly static PaginateConfig<Reading> Config = PaginateConfig<Reading>.Create(b => b
		.WithLimits(10, 10)
		.WithTieBreaker(r => r.Id)
		.Filterable("amount", r => r.Amount, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThan)
		.Filterable("ratio", r => r.Ratio, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThan)
		.Filterable("scale", r => r.Scale, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThan));

	/// <remarks>
	///     Row 1 holds <c>1.50</c> and row 2 holds <c>15</c> — the row a European-formatted <c>1,5</c> silently
	///     lands on today, ten times the amount the caller asked about. Row 3 is the negative one <c>1234-</c>
	///     currently reaches.
	/// </remarks>
	private static Task<PaginatedResponse<ReadingDto>> PageAsync(string field, string criterion) {

		IQueryable<Reading> readings = new List<Reading> {
			new() { Id = 1, Amount = 1.50m, Ratio = 1, Scale = 1 },
			new() { Id = 2, Amount = 15m, Ratio = 2, Scale = 2 },
			new() { Id = 3, Amount = -1234m, Ratio = 3, Scale = 3 }
		}.AsQueryable();

		return readings.PaginateAsync<Reading, ReadingDto>(
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

	[Theory]
	[InlineData("1,5")]
	[InlineData("1,234.56")]
	[InlineData("1,23,4")]
	[InlineData("1234-")]
	public async Task A_decimal_refuses_a_group_separator_and_a_trailing_sign(string value) {
		Assert.Equal($"Value '{value}' is not valid for 'amount'.", await RejectsAsync("amount", $"$eq:{value}"));
	}

	[Theory]
	[InlineData("1.50", 1)]
	[InlineData("+1.50", 1)]
	[InlineData(" 1.50 ", 1)]
	[InlineData("-1234", 3)]
	public async Task A_decimal_keeps_the_invariant_forms(string value, int expected) {

		int[] ids = await IdsAsync("amount", $"$eq:{value}");

		Assert.Equal([expected], ids);

	}

	/// <remarks>
	///     <c>double.Parse</c> saturates instead of overflowing, so <c>$gt:1e400</c> compares against positive
	///     infinity and answers an empty page indistinguishable from "no rows match".
	/// </remarks>
	[Theory]
	[InlineData("$gt:1e400")]
	[InlineData("$gt:-1e400")]
	[InlineData("$eq:NaN")]
	[InlineData("$eq:Infinity")]
	[InlineData("$eq:-Infinity")]
	public async Task A_double_refuses_a_non_finite_result(string criterion) {

		string value = criterion[(criterion.IndexOf(':', StringComparison.Ordinal) + 1)..];

		Assert.Equal($"Value '{value}' is not valid for 'ratio'.", await RejectsAsync("ratio", criterion));

	}

	/// <remarks><c>1e40</c> is a perfectly ordinary <see cref="double" /> and an infinity once narrowed to a <see cref="float" />.</remarks>
	[Theory]
	[InlineData("1e40")]
	[InlineData("NaN")]
	public async Task A_float_refuses_a_magnitude_its_own_type_cannot_hold(string value) {
		Assert.Equal($"Value '{value}' is not valid for 'scale'.", await RejectsAsync("scale", $"$gt:{value}"));
	}

	[Fact]
	public async Task The_floating_point_types_keep_their_ordinary_values() {

		int[] above = await IdsAsync("ratio", "$gt:1.5");
		int[] narrow = await IdsAsync("scale", "$gt:2.5");
		int[] exact = await IdsAsync("ratio", "$eq:1");

		Assert.Equal([2, 3], above);
		Assert.Equal([3], narrow);
		Assert.Equal([1], exact);

	}

}
