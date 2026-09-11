using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Like;

using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>
///     Every rejection carries a machine-readable <see cref="PaginateQueryError" /> alongside its message, so a
///     client can branch on the cause without matching prose. The message itself is unchanged — these assert the
///     code, and the catalogue tests still assert the wording.
/// </summary>
public sealed class ErrorCodeTests {

	private static async Task<PaginateQueryException> Rejects(PaginateQuery request, PaginateConfig<Product>? config = null) {
		return await Assert.ThrowsAsync<PaginateQueryException>(
			() => TestData.Products().AsQueryable().PageAsync<ProductDto>(request, config));
	}

	[Fact]
	public async Task Paging_rejections_are_coded() {

		Assert.Equal(PaginateQueryError.PageOutOfRange, (await Rejects(new PaginateQuery { Page = 0 })).Code);
		Assert.Equal(PaginateQueryError.LimitOutOfRange, (await Rejects(new PaginateQuery { Limit = 9_999 })).Code);

	}

	[Fact]
	public async Task The_offset_ceiling_has_its_own_code() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 50)
			.WithMaxOffset(10)
			.Sortable("id", p => p.Id)
			.WithTieBreaker(p => p.Id));

		Assert.Equal(PaginateQueryError.MaxOffsetExceeded, (await Rejects(new PaginateQuery { Page = 5, Limit = 10 }, config)).Code);

	}

	[Fact]
	public async Task An_unlimited_read_has_its_own_two_codes() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 50)
			.AllowUnlimited(3)
			.Sortable("id", p => p.Id)
			.WithTieBreaker(p => p.Id));

		Assert.Equal(PaginateQueryError.UnlimitedReadRequiresFirstPage,
			(await Rejects(new PaginateQuery { Page = 2, Limit = PaginateQuery.UnlimitedLimit }, config)).Code);

		// Eight rows against a ceiling of three.
		Assert.Equal(PaginateQueryError.UnlimitedReadTooLarge,
			(await Rejects(new PaginateQuery { Limit = PaginateQuery.UnlimitedLimit }, config)).Code);

	}

	[Fact]
	public async Task Sort_rejections_are_coded() {

		Assert.Equal(PaginateQueryError.SortValueMalformed, (await Rejects(Query.Sort("rank"))).Code);
		Assert.Equal(PaginateQueryError.SortDirectionUnknown, (await Rejects(Query.Sort("rank:UP"))).Code);
		Assert.Equal(PaginateQueryError.SortFieldNotConfigured, (await Rejects(Query.Sort("nope:ASC"))).Code);

	}

	[Fact]
	public async Task Filter_rejections_are_coded() {

		Assert.Equal(PaginateQueryError.FilterFieldNotConfigured, (await Rejects(Query.Filter("nope", "$eq:1"))).Code);
		Assert.Equal(PaginateQueryError.FilterCriterionMalformed, (await Rejects(Query.Filter("rank", "$eq"))).Code);
		Assert.Equal(PaginateQueryError.FilterCriterionMalformed, (await Rejects(Query.Filter("rank", "$null:false"))).Code);
		Assert.Equal(PaginateQueryError.FilterCriterionMalformed, (await Rejects(Query.Filter("rank", "$null:"))).Code);
		Assert.Equal(PaginateQueryError.FilterConnectorMisplaced, (await Rejects(Query.Filter("rank", "$or:$eq:1"))).Code);
		Assert.Equal(PaginateQueryError.FilterOperatorUnknown, (await Rejects(Query.Filter("rank", "$nope:1"))).Code);
		Assert.Equal(PaginateQueryError.FilterOperatorNotAllowed, (await Rejects(Query.Filter("rank", "$ilike:x"))).Code);
		Assert.Equal(PaginateQueryError.FilterValueCountInvalid, (await Rejects(Query.Filter("id", "$btw:1"))).Code);

		// Two spellings of one field in an ordinal Filters map. Field lookup is case-insensitive, so both keys
		// resolve to 'rank' and one criterion would be lost; the code is what tells a client which of the two
		// keys to drop. Unreachable over HTTP, where the binder collapses the keys before the engine sees them.
		Assert.Equal(PaginateQueryError.DuplicateFilterField,
			(await Rejects(Query.Filters(("rank", "$eq:1"), ("Rank", "$eq:2")))).Code);

	}

	[Fact]
	public void An_operator_that_does_not_fit_the_field_type_is_coded() {

		// The expression builder's own type guard: $contains needs a string or a collection, and rank is an
		// int. Build() now refuses that declaration outright, so the configuration route can no longer reach
		// the guard -- the filter field is constructed directly instead, the same pattern FilterOperatorTests
		// uses for the same reason. The guard is kept as defence in depth, so its code still has to be right.
		var field = new PaginateScalarFilterField<Product, int>(
			"rank", p => p.Rank, typeof(int), new HashSet<PaginateFilterOperator> { PaginateFilterOperator.Contains });

		var criterion = new PaginateFilterCriterion(PaginateFilterOperator.Contains, "1", false, PaginateFilterConnector.And);

		var rejection = Assert.Throws<PaginateQueryException>(() => field.BuildExpression(
			Expression.Parameter(typeof(Product), "p"),
			criterion,
			new PaginateExpressionContext(true, false, PaginateLikeDefaults.Strategy, 1, 256),
			20));

		Assert.Equal(PaginateQueryError.FilterOperatorTypeMismatch, rejection.Code);

	}

	[Fact]
	public async Task Value_conversion_rejections_are_coded() {

		Assert.Equal(PaginateQueryError.ValueInvalid, (await Rejects(Query.Filter("rank", "$eq:abc"))).Code);
		Assert.Equal(PaginateQueryError.ValueEmpty, (await Rejects(Query.Filter("rank", "$eq:"))).Code);

	}

	[Fact]
	public async Task Search_rejections_are_coded() {

		Assert.Equal(PaginateQueryError.SearchFieldNotConfigured, (await Rejects(Query.Search("widget", "nope"))).Code);
		Assert.Equal(PaginateQueryError.DuplicateSearchField, (await Rejects(Query.Search("widget", "name", "name"))).Code);

	}

	[Fact]
	public async Task The_pattern_length_guards_have_their_own_two_codes() {

		// The same two numbers as the search term, but a different parameter and a different message, so a client
		// can tell which of the two paths to LIKE '%...%' it tripped.
		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 50)
			.WithGuards(maxSearchLength: 4)
			.WithMinSearchLength(2)
			.Sortable("id", p => p.Id)
			.WithTieBreaker(p => p.Id)
			.Filterable("name", p => p.Name));

		Assert.Equal(PaginateQueryError.FilterPatternTooShort, (await Rejects(Query.Filter("name", "$ilike:a"), config)).Code);
		Assert.Equal(PaginateQueryError.FilterPatternTooLong, (await Rejects(Query.Filter("name", "$ilike:widget"), config)).Code);

	}

	[Fact]
	public async Task The_code_travels_with_the_message_it_belongs_to() {

		// The prose is still the published contract; the code is additional, not a replacement.
		var exception = await Rejects(Query.Sort("rank:UP"));

		Assert.Equal("Sort direction 'UP' is not supported.", exception.Message);
		Assert.Equal(PaginateQueryError.SortDirectionUnknown, exception.Code);

	}

	[Fact]
	public void An_exception_constructed_outside_the_engine_is_unspecified() {
		Assert.Equal(PaginateQueryError.Unspecified, new PaginateQueryException("hand-built").Code);
	}

	/// <summary>
	///     The eight codes no focused test above provokes, so the coverage assertion below has a producer for
	///     every member rather than a list that happens to be complete today.
	/// </summary>
	[Fact]
	public async Task The_remaining_codes_are_reachable() {

		var guards = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 50)
			.WithGuards(maxFilterValues: 2, maxFilterConditions: 2, maxSortFields: 1, maxSearchLength: 4)
			.WithMinSearchLength(3)
			.Sortable("id", p => p.Id)
			.Sortable("rank", p => p.Rank)
			.WithTieBreaker(p => p.Id)
			.Searchable("name", p => p.Name)
			.Filterable("rank", p => p.Rank, PaginateFilterOperator.In, PaginateFilterOperator.Eq));

		Assert.Equal(PaginateQueryError.TooManyFilterConditions,
			(await Rejects(Query.Filter("rank", "$eq:10", "$eq:20", "$eq:30"), guards)).Code);

		Assert.Equal(PaginateQueryError.TooManyFilterValues,
			(await Rejects(Query.Filter("rank", "$in:10,20,30"), guards)).Code);

		// The switch's default arm, reachable only by an operator value that is not a declared member — a cast
		// integer here, a member added to the enum without an arm in production. Build() refuses an unbuildable
		// pair now, so the field is constructed directly, the same way the type-mismatch test above does.
		var bogus = new PaginateScalarFilterField<Product, int>(
			"rank", p => p.Rank, typeof(int), new HashSet<PaginateFilterOperator> { (PaginateFilterOperator)999 });

		var unsupported = Assert.Throws<PaginateQueryException>(() => bogus.BuildExpression(
			Expression.Parameter(typeof(Product), "p"),
			new PaginateFilterCriterion((PaginateFilterOperator)999, "1", false, PaginateFilterConnector.And),
			new PaginateExpressionContext(true, false, PaginateLikeDefaults.Strategy, 1, 256),
			maxFilterValues: 100));

		Assert.Equal(PaginateQueryError.FilterOperatorUnsupported, unsupported.Code);

		Assert.Equal(PaginateQueryError.SearchTermTooShort, (await Rejects(new PaginateQuery { Search = "ab" }, guards)).Code);
		Assert.Equal(PaginateQueryError.SearchTermTooLong, (await Rejects(new PaginateQuery { Search = "abcde" }, guards)).Code);
		Assert.Equal(PaginateQueryError.TooManySortFields, (await Rejects(Query.Sort("id:ASC", "rank:DESC"), guards)).Code);

		// Its own config: the ceiling of one above is counted before the duplicate is looked for, so the two
		// cannot be provoked through the same one.
		var twoSorts = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 50)
			.WithGuards(maxSortFields: 4)
			.Sortable("id", p => p.Id)
			.WithTieBreaker(p => p.Id));

		Assert.Equal(PaginateQueryError.DuplicateSortField, (await Rejects(Query.Sort("id:ASC", "id:DESC"), twoSorts)).Code);

		var searchless = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 50)
			.Sortable("id", p => p.Id)
			.WithTieBreaker(p => p.Id));

		Assert.Equal(PaginateQueryError.SearchNotConfigured, (await Rejects(new PaginateQuery { Search = "widget" }, searchless)).Code);

	}

	/// <summary>
	///     Every member of the enum is produced by something. <c>ValueTypeNotSupported</c> was declared,
	///     documented and assigned nowhere for a whole release line, which no test could see because no test
	///     asked the question — the instance was fixed, the class was not. A member added without a producer
	///     fails here rather than shipping as dead public surface that cannot be removed after the next stable
	///     release.
	/// </summary>
	[Fact]
	public void Every_code_has_a_producer() {

		var provoked = typeof(ErrorCodeTests)
			.GetMethods()
			.SelectMany(method => method.GetCustomAttributes(typeof(FactAttribute), inherit: false).Length > 0
				? CodesAssertedBy(method.Name)
				: [])
			.ToHashSet();

		// Asserted elsewhere and named here, so the guard counts them rather than pretending this class is the
		// only place a code can be pinned: the connector one in FilterGrammarTests, the unsupported-type one in
		// ValueConversionTests, each beside the message it belongs to.
		provoked.Add(PaginateQueryError.FilterConnectorMisplaced);
		provoked.Add(PaginateQueryError.ValueTypeNotSupported);

		var declared = Enum.GetValues<PaginateQueryError>().Where(code => code != PaginateQueryError.Unspecified);

		var orphaned = declared.Except(provoked).ToArray();

		Assert.True(orphaned.Length == 0,
			$"No test provokes: {string.Join(", ", orphaned)}. A code with no producer is dead public surface.");

	}

	// The map the assertion above rests on: which codes each test in this class provokes. Kept beside the tests
	// rather than derived, because a code is provoked by a *request shape* and no reflection can read that.
	private static PaginateQueryError[] CodesAssertedBy(string test) {
		return test switch {
			nameof(Paging_rejections_are_coded) => [PaginateQueryError.PageOutOfRange, PaginateQueryError.LimitOutOfRange],
			nameof(The_offset_ceiling_has_its_own_code) => [PaginateQueryError.MaxOffsetExceeded],
			nameof(An_unlimited_read_has_its_own_two_codes) => [PaginateQueryError.UnlimitedReadRequiresFirstPage, PaginateQueryError.UnlimitedReadTooLarge],
			nameof(Sort_rejections_are_coded) => [PaginateQueryError.SortValueMalformed, PaginateQueryError.SortDirectionUnknown, PaginateQueryError.SortFieldNotConfigured],
			nameof(Search_rejections_are_coded) => [PaginateQueryError.SearchFieldNotConfigured, PaginateQueryError.DuplicateSearchField],
			nameof(Filter_rejections_are_coded) => [
				PaginateQueryError.FilterFieldNotConfigured, PaginateQueryError.FilterCriterionMalformed,
				PaginateQueryError.FilterOperatorUnknown, PaginateQueryError.FilterOperatorNotAllowed,
				PaginateQueryError.FilterValueCountInvalid, PaginateQueryError.DuplicateFilterField],
			nameof(An_operator_that_does_not_fit_the_field_type_is_coded) => [PaginateQueryError.FilterOperatorTypeMismatch],
			nameof(Value_conversion_rejections_are_coded) => [PaginateQueryError.ValueInvalid, PaginateQueryError.ValueEmpty],
			nameof(The_pattern_length_guards_have_their_own_two_codes) => [PaginateQueryError.FilterPatternTooShort, PaginateQueryError.FilterPatternTooLong],
			nameof(The_code_travels_with_the_message_it_belongs_to) => [PaginateQueryError.SortDirectionUnknown],
			nameof(The_remaining_codes_are_reachable) => [
				PaginateQueryError.TooManyFilterConditions, PaginateQueryError.TooManyFilterValues,
				PaginateQueryError.FilterOperatorUnsupported, PaginateQueryError.SearchTermTooShort,
				PaginateQueryError.SearchTermTooLong, PaginateQueryError.TooManySortFields,
				PaginateQueryError.DuplicateSortField, PaginateQueryError.SearchNotConfigured],
			_ => []
		};
	}

}
