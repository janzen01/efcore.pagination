using System.Globalization;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The type-derived operator sets behind the parameterless <c>Filterable</c> / <c>FilterableMany</c>
///     overloads. The derivation is the single place a later release widens a row, so each row is pinned
///     exactly rather than by "contains".
/// </summary>
public sealed class OperatorDerivationTests {

	private static PaginateFilterOperator[] Sorted<TValue>() { return [.. PaginateFilterOperators.For<TValue>().Order()]; }

	private static PaginateFilterOperator[] Sorted(params PaginateFilterOperator[] operators) { return [.. operators.Order()]; }

	[Fact]
	public void Strings_get_the_pattern_operators_and_no_ranges() {

		Assert.Equal(
			Sorted(PaginateFilterOperator.Eq, PaginateFilterOperator.In, PaginateFilterOperator.Null,
				PaginateFilterOperator.StartsWith, PaginateFilterOperator.Contains, PaginateFilterOperator.ILike),
			Sorted<string>());

	}

	[Fact]
	public void Numbers_and_dates_get_the_range_operators() {

		var expected = Sorted(PaginateFilterOperator.Eq, PaginateFilterOperator.In,
			PaginateFilterOperator.GreaterThan, PaginateFilterOperator.GreaterThanOrEqual,
			PaginateFilterOperator.LessThan, PaginateFilterOperator.LessThanOrEqual, PaginateFilterOperator.Between);

		Assert.Equal(expected, Sorted<int>());
		Assert.Equal(expected, Sorted<decimal>());
		Assert.Equal(expected, Sorted<DateTimeOffset>());
		Assert.Equal(expected, Sorted<DateOnly>());
		Assert.Equal(expected, Sorted<TimeSpan>());

	}

	[Fact]
	public void Guid_char_and_enums_get_equality_and_membership_only() {

		var expected = Sorted(PaginateFilterOperator.Eq, PaginateFilterOperator.In);

		Assert.Equal(expected, Sorted<Guid>());
		Assert.Equal(expected, Sorted<char>());
		Assert.Equal(expected, Sorted<ProductStatus>());

	}

	[Fact]
	public void Booleans_get_equality_only() {
		Assert.Equal([PaginateFilterOperator.Eq], PaginateFilterOperators.For<bool>());
	}

	[Fact]
	public void Null_joins_a_value_type_only_through_Nullable() {

		Assert.DoesNotContain(PaginateFilterOperator.Null, PaginateFilterOperators.For<int>());
		Assert.Contains(PaginateFilterOperator.Null, PaginateFilterOperators.For<int?>());
		Assert.DoesNotContain(PaginateFilterOperator.Null, PaginateFilterOperators.For<ProductStatus>());
		Assert.Contains(PaginateFilterOperator.Null, PaginateFilterOperators.For<ProductStatus?>());

		// A reference type is always nullable, NRT annotations being erased by the time this runs.
		Assert.Contains(PaginateFilterOperator.Null, PaginateFilterOperators.For<string>());

	}

	[Fact]
	public void An_underivable_type_throws_rather_than_guessing() {

		var exception = Assert.Throws<ArgumentException>(PaginateFilterOperators.For<Category>);

		Assert.StartsWith("Filter operators cannot be derived for type 'Category'.", exception.Message);

	}

	[Fact]
	public void The_shorthand_whitelists_exactly_the_derived_set() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, Query.All)
			.Sortable("id", p => p.Id)
			.DefaultSortBy("id")
			.WithTieBreaker(p => p.Id)
			.Filterable("rank", p => p.Rank)
			.Filterable("name", p => p.Name));

		IPaginateConfig meta = config;

		Assert.Equal(
			[.. PaginateFilterOperators.For<int>().Order()],
			[.. meta.FilterableFields.Single(field => field.Name == "rank").Operators.Order()]);

		Assert.Equal(
			[.. PaginateFilterOperators.For<string>().Order()],
			[.. meta.FilterableFields.Single(field => field.Name == "name").Operators.Order()]);

	}

	[Fact]
	public async Task A_derived_operator_filters_and_one_outside_the_set_is_rejected() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, Query.All)
			.Sortable("id", p => p.Id)
			.DefaultSortBy("id")
			.WithTieBreaker(p => p.Id)
			.Filterable("rank", p => p.Rank));

		var products = TestData.Products().AsQueryable();

		// $gte is in the derived set for an int.
		Assertions.HasIds(await products.PageAsync<ProductDto>(Query.Filter("rank", "$gte:70"), config), 7, 8);

		// $ilike is not -- the derived set is the whitelist, so the refusal is the ordinary one.
		string message = await Assertions.RejectsAsync(() => products.PageAsync<ProductDto>(Query.Filter("rank", "$ilike:7"), config));

		Assert.Equal("Filter 'rank' does not support operator '$ilike'.", message);

	}

	[Fact]
	public async Task FilterableMany_derives_from_the_value_selector() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(10, Query.All)
			.Sortable("id", p => p.Id)
			.DefaultSortBy("id")
			.WithTieBreaker(p => p.Id)
			.FilterableMany("rating", p => p.Reviews, r => r.Rating));

		var page = await TestData.Products().AsQueryable().PageAsync<ProductDto>(Query.Filter("rating", "$gte:5"), config);

		Assertions.HasIds(page, 1);

	}

	/// <summary>Comparable but with no relational operators -- exactly what the engine cannot build a range for.</summary>
	public readonly struct Score(int value) : IComparable<Score> {
		public int Value { get; } = value;
		public int CompareTo(Score other) { return this.Value.CompareTo(other.Value); }
	}

	[Fact]
	public void A_registered_type_without_relational_operators_gets_no_range_row() {

		// The type is keyed to this file so the process-wide registration can never be reached by another test.
		PaginateTypeSupport.RegisterSimpleType(typeof(Score));
		PaginateTypeSupport.RegisterValueParser(typeof(Score), value => new Score(int.Parse(value, CultureInfo.InvariantCulture)));

		// IComparable<T> alone is not enough: BuildComparison only reaches for its CompareTo stand-in for enums,
		// string and Guid, and sends everything else down Expression.GreaterThan. Deriving a range row here would
		// have advertised it through the metadata and OpenAPI and then answered 400 to every range request.
		var exception = Assert.Throws<ArgumentException>(PaginateFilterOperators.For<Score>);

		Assert.StartsWith("Filter operators cannot be derived for type 'Score'.", exception.Message);

	}

	[Fact]
	public void The_shorthand_names_the_type_argument_when_nothing_can_be_derived() {

		// For(Type) names its own parameter, and the shorthand has none: a consumer reading "(Parameter 'type')"
		// against .Filterable(name, selector) goes looking for an argument that is not there. The input is the
		// type argument the selector's return type inferred.
		var exception = Assert.Throws<ArgumentException>(() =>
			PaginateConfig<Product>.Create(b => b.Filterable("category", p => p.Category)));

		Assert.Equal("TValue", exception.ParamName);

	}

	[Fact]
	public void The_shorthand_checks_its_own_arguments_before_deriving() {

		// The derivation is an *argument* to the explicit overload, so it ran before that overload's guards and a
		// null name was reported as a derivation failure on an unrelated type.
		var exception = Assert.Throws<ArgumentNullException>(() =>
			PaginateConfig<Product>.Create(b => b.Filterable(null!, p => p.Category)));

		Assert.Equal("name", exception.ParamName);

	}

}
