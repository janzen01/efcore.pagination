using System.Linq.Expressions;

namespace Janzen.Pagination.Tests;

/// <summary>What the builder refuses to produce, and what it exposes about what it did produce.</summary>
public sealed class ConfigurationTests {

	// Every config needs a tie-breaker now, and none of these tests is about that rule, so the helper supplies
	// one -- the tests that ARE about it call Create directly.
	private static PaginateConfig<Product> Build(Action<PaginateConfigBuilder<Product>> configure) {
		return PaginateConfig<Product>.Create(b => {
			b.WithTieBreaker(p => p.Id);
			configure(b);
		});
	}

	[Fact]
	public void A_tie_breaker_is_mandatory() {

		var exception = Assert.Throws<InvalidOperationException>(() => PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 10)
			.Sortable("id", p => p.Id)
			.DefaultSortBy("id")));

		// Required outright, not "a default sort or a tie-breaker": a DefaultSortBy field can be disabled by
		// When(false) for a given caller, so the weaker rule would pass here and still leave nothing to order by.
		Assert.StartsWith("A pagination configuration requires WithTieBreaker(...):", exception.Message);

	}

	[Fact]
	public void Limits_are_mandatory() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b.Sortable("id", p => p.Id)));

		Assert.Equal("Pagination limits must be configured explicitly via WithLimits(defaultLimit, maxLimit).", exception.Message);

	}

	[Theory]
	[InlineData(0, 10)]
	[InlineData(-1, 10)]
	[InlineData(10, 0)]
	public void Limits_must_be_positive(int defaultLimit, int maxLimit) {
		Assert.Throws<ArgumentOutOfRangeException>(() => Build(b => b.WithLimits(defaultLimit, maxLimit)));
	}

	[Fact]
	public void The_default_limit_may_not_exceed_the_maximum() {
		Assert.Throws<ArgumentException>(() => Build(b => b.WithLimits(50, 10)));
	}

	[Fact]
	public void A_default_sort_must_name_a_sortable_field() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b
			.WithLimits(10, 10)
			.DefaultSortBy("nope")));

		Assert.Equal("Default sort field 'nope' is not sortable.", exception.Message);

	}

	[Fact]
	public void An_explicit_operator_list_may_not_be_empty() {

		// Only the explicit signature raises this now: the no-operator call binds to the shorthand overload and
		// derives a set. Handing that overload an array that happens to be empty stays an error, because
		// "derive" is a signature the caller chooses, never a silent fallback for a list that came out empty.
		var exception = Assert.Throws<ArgumentException>(() => Build(b => b
			.WithLimits(10, 10)
			.Filterable("id", p => p.Id, [])));

		Assert.StartsWith("At least one filter operator must be configured.", exception.Message);

	}

	[Fact]
	public void A_comparison_operator_the_field_type_cannot_carry_is_refused_at_build_time() {

		// PaginateFilterOperators.For<bool>() returns Eq alone, so a range on a bool can only arrive through the
		// explicit signature. It used to build and then answer 400 to every request -- a configuration defect
		// reported to a caller who cannot act on it.
		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b
			.WithLimits(10, 10)
			.Filterable("isFeatured", p => p.IsFeatured, PaginateFilterOperator.Eq, PaginateFilterOperator.GreaterThan)));

		Assert.Equal(
			"Filter 'isFeatured' allows operator '$gt', which the engine cannot build for type 'Boolean'. Drop the operator, or declare the field without an explicit list to take the operators its type supports.",
			exception.Message);

	}

	[Fact]
	public void A_pattern_operator_on_a_field_that_is_not_a_string_is_refused_at_build_time() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b
			.WithLimits(10, 10)
			.Filterable("rank", p => p.Rank, PaginateFilterOperator.ILike)));

		Assert.Equal(
			"Filter 'rank' allows operator '$ilike', which the engine cannot build for type 'Int32'. Drop the operator, or declare the field without an explicit list to take the operators its type supports.",
			exception.Message);

	}

	[Fact]
	public void Contains_on_a_field_that_is_neither_a_string_nor_a_collection_is_refused_at_build_time() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b
			.WithLimits(10, 10)
			.Filterable("rank", p => p.Rank, PaginateFilterOperator.Contains)));

		Assert.StartsWith("Filter 'rank' allows operator '$contains'", exception.Message);

	}

	[Fact]
	public void Contains_stays_allowed_on_a_collection_field() {
		Assert.Single(Build(b => b.WithLimits(10, 10).Filterable("tags", p => p.Tags, PaginateFilterOperator.Contains)).FilterableFields);
	}

	[Fact]
	public void The_build_time_check_accepts_every_type_the_comparison_builder_handles() {

		// The guard mirrors what BuildComparison does, which is wider than any single reflection probe: the
		// integral primitives declare no op_LessThan at all, char is in neither the derived Comparable set nor
		// the ordering probe, and enums, strings and Guids reach a CompareTo stand-in rather than an operator.
		AcceptsRanges(p => (sbyte)p.Rank);
		AcceptsRanges(p => (byte)p.Rank);
		AcceptsRanges(p => (short)p.Rank);
		AcceptsRanges(p => (ushort)p.Rank);
		AcceptsRanges(p => p.Rank);
		AcceptsRanges(p => (uint)p.Rank);
		AcceptsRanges(p => (long)p.Rank);
		AcceptsRanges(p => (ulong)p.Rank);
		AcceptsRanges(p => (float)p.Rank);
		AcceptsRanges(p => (double)p.Rank);
		AcceptsRanges(p => p.Price);
		AcceptsRanges(p => p.Name[0]);
		AcceptsRanges(p => p.Name);
		AcceptsRanges(p => p.ExternalId);
		AcceptsRanges(p => p.Status);
		AcceptsRanges(p => p.CreatedAt);
		AcceptsRanges(p => p.DiscontinuedAt);
		AcceptsRanges(p => p.ReleasedOn);
		AcceptsRanges(p => p.OpensAt);
		AcceptsRanges(p => p.Warranty);

	}

	private static void AcceptsRanges<TValue>(Expression<Func<Product, TValue>> selector) {
		Assert.Single(Build(b => b
			.WithLimits(10, 10)
			.Filterable("value", selector, PaginateFilterOperator.GreaterThan, PaginateFilterOperator.GreaterThanOrEqual,
				PaginateFilterOperator.LessThan, PaginateFilterOperator.LessThanOrEqual, PaginateFilterOperator.Between)).FilterableFields);
	}

	[Fact]
	public void A_badge_must_follow_a_field() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b
			.WithLimits(10, 10)
			.ShowBadge("Orphan")));

		Assert.Equal("ShowBadge must be called immediately after a Sortable, Searchable, or Filterable field.", exception.Message);

	}

	[Fact]
	public void A_badge_class_must_carry_the_language_prefix() {

		// It is the only prefix an API reference's markdown sanitizer keeps on an inline code element, so a
		// class that does not start with it would silently render unstyled.
		var exception = Assert.Throws<ArgumentException>(() => Build(b => b
			.WithLimits(10, 10)
			.Sortable("id", p => p.Id).ShowBadge("Admin", "admin-chip")));

		Assert.StartsWith("Badge cssClass must start with \"language-\"", exception.Message);

	}

	[Fact]
	public void A_badge_without_a_class_is_neutral() {

		var config = Build(b => b.WithLimits(10, 10).Sortable("id", p => p.Id).ShowBadge("Beta"));

		var field = Assert.Single(config.SortableFields);
		Assert.Equal("Beta", field.Badge?.Name);
		Assert.Null(field.Badge?.CssClass);

	}

	[Fact]
	public void A_condition_must_follow_a_field() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b.WithLimits(10, 10).When(true)));

		Assert.Equal("When must be called immediately after a Sortable, Searchable, or Filterable field.", exception.Message);

	}

	[Fact]
	public void A_condition_must_be_documented_by_a_badge() {

		var exception = Assert.Throws<InvalidOperationException>(() => Build(b => b
			.WithLimits(10, 10)
			.Sortable("id", p => p.Id).When(false)));

		Assert.Equal("A field configured with .When(...) must also declare .ShowBadge(...) so the condition is documented in the OpenAPI output.",
			exception.Message);

	}

	[Theory]
	[InlineData(0, 20, 5, 256)]
	[InlineData(100, 0, 5, 256)]
	[InlineData(100, 20, 0, 256)]
	[InlineData(100, 20, 5, 0)]
	public void Guards_must_be_positive(int values, int conditions, int sortFields, int searchLength) {
		Assert.Throws<ArgumentOutOfRangeException>(() => Build(b => b
			.WithLimits(10, 10)
			.WithGuards(values, conditions, sortFields, searchLength)));
	}

	[Fact]
	public void Redeclaring_a_field_name_replaces_the_earlier_declaration() {

		var config = Build(b => b
			.WithLimits(10, 10)
			.Sortable("key", p => p.Id)
			.Sortable("key", p => p.Rank));

		var field = Assert.Single(config.SortableFields);
		Assert.Equal("key", field.Name);

	}

	[Fact]
	public void The_configuration_describes_its_own_surface() {

		var config = TestData.Config;

		Assert.Equal(3, config.DefaultLimit);
		Assert.Equal(50, config.MaxLimit);
		Assert.Equal([new PaginateSort("rank", PaginateSortDirection.Asc)], config.DefaultSortBy);
		Assert.Contains(config.SortableFields, f => f.Name == "rank" && f.Type == typeof(int));
		Assert.Contains(config.SearchableFields, f => f.Name == "name" && f.Type == typeof(string));

		var status = Assert.Single(config.FilterableFields, f => f.Name == "status");
		Assert.Equal(typeof(ProductStatus), status.Type);
		Assert.Equal([PaginateFilterOperator.Eq, PaginateFilterOperator.In], status.Operators.Order());

	}

	[Fact]
	public void A_nullable_filterable_reports_its_underlying_type() {

		var field = Assert.Single(TestData.Config.FilterableFields, f => f.Name == "discontinuedAt");

		Assert.Equal(typeof(DateTimeOffset), field.Type);

	}

}
