namespace Janzen.Pagination.Tests;

/// <summary>
///     The records a consumer reads back off <see cref="IPaginateConfig" />. They are records, so they advertise
///     value equality — but <see cref="PaginateFilterFieldMetadata.Operators" /> is a collection, which a
///     synthesized <c>Equals</c> compares by reference. The set is materialised fresh per field per build, so two
///     builds of one config can never share it and a consumer snapshotting the metadata sees a false "changed"
///     on every rebuild. These pin the hand-written equality that makes the advertisement true.
/// </summary>
public sealed class MetadataEqualityTests {

	private static IPaginateConfig Build(params PaginateFilterOperator[] statusOperators) {
		return PaginateConfig<Product>.Create(b => b
			.WithLimits(10, 50)
			.Sortable("rank", p => p.Rank)
			.WithTieBreaker(p => p.Id)
			.Searchable("name", p => p.Name)
			.Filterable("status", p => p.Status, statusOperators.Length > 0 ? statusOperators : [PaginateFilterOperator.Eq, PaginateFilterOperator.In]));
	}

	[Fact]
	public void Two_builds_of_one_config_report_equal_sortable_fields() {
		// The sibling record carries no collection, so this passed before the filterable one did. It is here to
		// say what the expectation is, not because it was ever in doubt.
		Assert.Equal(Build().SortableFields, Build().SortableFields);
	}

	[Fact]
	public void Two_builds_of_one_config_report_equal_filterable_fields() {
		Assert.Equal(Build().FilterableFields, Build().FilterableFields);
	}

	[Fact]
	public void Equal_filterable_fields_hash_equal() {
		Assert.Equal(Build().FilterableFields[0].GetHashCode(), Build().FilterableFields[0].GetHashCode());
	}

	[Fact]
	public void The_operator_set_is_compared_by_value_not_by_order() {

		var first = Build(PaginateFilterOperator.Eq, PaginateFilterOperator.In).FilterableFields[0];
		var second = Build(PaginateFilterOperator.In, PaginateFilterOperator.Eq).FilterableFields[0];

		// A set has no order, so the hash has to be commutative too -- the half that is easy to get wrong.
		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());

	}

	[Fact]
	public void A_different_operator_set_is_not_equal() {

		var narrow = Build(PaginateFilterOperator.Eq).FilterableFields[0];
		var wide = Build(PaginateFilterOperator.Eq, PaginateFilterOperator.In).FilterableFields[0];

		Assert.NotEqual(narrow, wide);

	}

	[Fact]
	public void A_different_name_is_still_not_equal() {

		var status = Build().FilterableFields[0];
		var renamed = status with { Name = "state" };

		// Hand-written equality drops whatever the author forgets, and only an inequality isolating a member
		// can catch that. One per member the record carries.
		Assert.NotEqual(status, renamed);
		Assert.NotEqual(status, status with { Type = typeof(string) });
		Assert.NotEqual(status, status with { Badge = new PaginateBadge("Admin", null) });

	}

}
