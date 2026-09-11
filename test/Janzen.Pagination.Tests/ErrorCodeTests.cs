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
			new PaginateExpressionContext(true, PaginateLikeDefaults.Strategy),
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

}
