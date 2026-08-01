namespace Janzen.Pagination.Tests;

/// <summary>What the builder refuses to produce, and what it exposes about what it did produce.</summary>
public sealed class ConfigurationTests {

	private static PaginateConfig<Product> Build(Action<PaginateConfigBuilder<Product>> configure) { return PaginateConfig<Product>.Create(configure); }

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
	public void A_filterable_field_needs_at_least_one_operator() {

		var exception = Assert.Throws<ArgumentException>(() => Build(b => b
			.WithLimits(10, 10)
			.Filterable("id", p => p.Id)));

		Assert.StartsWith("At least one filter operator must be configured.", exception.Message);

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
