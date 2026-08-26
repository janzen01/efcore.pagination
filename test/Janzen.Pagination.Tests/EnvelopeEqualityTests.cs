using System.Collections.ObjectModel;
using System.Text.Json;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The envelope records advertise value equality by being records, and three of <see cref="PaginatedMeta" />'s
///     members plus <see cref="PaginatedResponse{T}.Items" /> are collections, which a synthesized <c>Equals</c>
///     compares by reference. These pin the hand-written equality that makes the advertisement true — and the
///     <c>GetHashCode</c> contract that goes with it, which is the half that is easy to get wrong.
/// </summary>
public sealed class EnvelopeEqualityTests {

	private static PaginatedMeta Meta(
		IReadOnlyList<string>? sortBy = null,
		string? search = null,
		IReadOnlyList<string>? searchBy = null,
		IReadOnlyDictionary<string, IReadOnlyList<string>>? filter = null,
		int currentPage = 2
	) {
		return new PaginatedMeta(37, 2, 25, 19, currentPage) {
			SortBy = sortBy ?? [],
			Search = search,
			SearchBy = searchBy ?? [],
			Filter = filter ?? PaginateQuery.EmptyFilters,
			HasPreviousPage = currentPage > 1,
			HasNextPage = true
		};
	}

	private static ReadOnlyDictionary<string, IReadOnlyList<string>> Filter(StringComparer comparer, params (string Field, string[] Values)[] entries) {

		var map = new Dictionary<string, IReadOnlyList<string>>(comparer);
		foreach ((string field, string[] values) in entries) map[field] = values;

		return new ReadOnlyDictionary<string, IReadOnlyList<string>>(map);

	}

	[Fact]
	public void Two_metas_describing_the_same_page_are_equal() {

		// Separately allocated collections with the same contents — the case the synthesized equality got wrong.
		var left = Meta(sortBy: ["rank:DESC"], search: "wid", searchBy: ["name", "description"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"])));

		var right = Meta(sortBy: ["rank:DESC"], search: "wid", searchBy: ["name", "description"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"])));

		Assert.Equal(left, right);
		Assert.True(left == right);
		Assert.Equal(left.GetHashCode(), right.GetHashCode());

	}

	[Fact]
	public void A_difference_in_any_echoed_member_makes_them_unequal() {

		var baseline = Meta(sortBy: ["rank:DESC"], search: "wid", searchBy: ["name"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"])));

		Assert.NotEqual(baseline, Meta(sortBy: ["rank:ASC"], search: "wid", searchBy: ["name"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"]))));

		Assert.NotEqual(baseline, Meta(sortBy: ["rank:DESC"], search: "gadget", searchBy: ["name"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"]))));

		Assert.NotEqual(baseline, Meta(sortBy: ["rank:DESC"], search: "wid", searchBy: ["description"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"]))));

		Assert.NotEqual(baseline, Meta(sortBy: ["rank:DESC"], search: "wid", searchBy: ["name"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Draft"]))));

		Assert.NotEqual(baseline, Meta(sortBy: ["rank:DESC"], search: "wid", searchBy: ["name"],
			filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"])), currentPage: 3));

	}

	[Fact]
	public void Sort_order_is_part_of_the_value() {
		// ["a","b"] and ["b","a"] are different orderings, not the same set.
		Assert.NotEqual(Meta(sortBy: ["name:ASC", "rank:DESC"]), Meta(sortBy: ["rank:DESC", "name:ASC"]));
	}

	[Fact]
	public void Filters_are_equal_regardless_of_insertion_order() {

		var left = Meta(filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"]), ("rank", ["$gte:20"])));
		var right = Meta(filter: Filter(StringComparer.Ordinal, ("rank", ["$gte:20"]), ("status", ["$eq:Active"])));

		// A dictionary has no order, so two maps with the same entries are the same value — and must hash the same.
		Assert.Equal(left, right);
		Assert.Equal(left.GetHashCode(), right.GetHashCode());

	}

	[Fact]
	public void Filter_equality_is_symmetric_across_dictionaries_built_with_different_comparers() {

		// The model binder builds an OrdinalIgnoreCase map; PaginateQuery.EmptyFilters and a hand-built request
		// are Ordinal. Matching keys through either dictionary's own comparer would make this pair equal in one
		// direction and unequal in the other — so keys are matched ordinally, and 'Status' is not 'status'.
		var binderShaped = Meta(filter: Filter(StringComparer.OrdinalIgnoreCase, ("Status", ["$eq:Active"])));
		var handBuilt = Meta(filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"])));

		Assert.False(binderShaped.Equals(handBuilt));
		Assert.False(handBuilt.Equals(binderShaped));

	}

	[Fact]
	public void Filters_agreeing_on_their_keys_are_equal_whatever_comparer_built_them() {

		var binderShaped = Meta(filter: Filter(StringComparer.OrdinalIgnoreCase, ("status", ["$eq:Active"])));
		var handBuilt = Meta(filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"])));

		Assert.True(binderShaped.Equals(handBuilt));
		Assert.True(handBuilt.Equals(binderShaped));
		Assert.Equal(binderShaped.GetHashCode(), handBuilt.GetHashCode());

	}

	[Fact]
	public void Filters_of_the_same_size_but_different_contents_are_unequal() {
		Assert.NotEqual(
			Meta(filter: Filter(StringComparer.Ordinal, ("status", ["$eq:Active"]))),
			Meta(filter: Filter(StringComparer.Ordinal, ("rank", ["$gte:20"]))));
	}

	[Fact]
	public void An_empty_meta_equals_another_empty_one() {

		var left = new PaginatedMeta(0, 0, 25, 0, 1);
		var right = new PaginatedMeta(0, 0, 25, 0, 1);

		Assert.Equal(left, right);
		Assert.Equal(left.GetHashCode(), right.GetHashCode());

	}

	[Fact]
	public void Two_responses_with_the_same_rows_are_equal() {

		// PaginatedResponse<T>.Items has compared by reference since 10.0.0; this is the same gate, one level up.
		var left = new PaginatedResponse<ProductDto>(
			[new ProductDto(1, "Widget", ProductStatus.Active, 10)], Meta(), new PaginatedLinks("/p?page=1", null, null, "/p?page=1"));

		var right = new PaginatedResponse<ProductDto>(
			[new ProductDto(1, "Widget", ProductStatus.Active, 10)], Meta(), new PaginatedLinks("/p?page=1", null, null, "/p?page=1"));

		Assert.Equal(left, right);
		Assert.Equal(left.GetHashCode(), right.GetHashCode());

	}

	[Fact]
	public void Responses_differing_in_a_row_a_meta_or_a_link_are_unequal() {

		var items = new List<ProductDto> { new(1, "Widget", ProductStatus.Active, 10) };
		var links = new PaginatedLinks("/p?page=1", null, null, "/p?page=1");
		var baseline = new PaginatedResponse<ProductDto>(items, Meta(), links);

		Assert.NotEqual(baseline, new PaginatedResponse<ProductDto>(
			[new ProductDto(2, "Gizmo", ProductStatus.Draft, 30)], Meta(), links));

		Assert.NotEqual(baseline, new PaginatedResponse<ProductDto>(items, Meta(currentPage: 3), links));

		Assert.NotEqual(baseline, new PaginatedResponse<ProductDto>(items, Meta(), links with { Next = "/p?page=2" }));

	}

	[Fact]
	public void Row_order_is_part_of_the_value() {

		ProductDto widget = new(1, "Widget", ProductStatus.Active, 10);
		ProductDto gizmo = new(2, "Gizmo", ProductStatus.Draft, 30);

		// A page is an ordered thing — the sort is half of what was asked for.
		Assert.NotEqual(
			new PaginatedResponse<ProductDto>([widget, gizmo], Meta(), null),
			new PaginatedResponse<ProductDto>([gizmo, widget], Meta(), null));

	}

	[Fact]
	public void A_deserialized_envelope_carrying_nulls_reports_inequality_rather_than_throwing() {

		// System.Text.Json does not enforce nullable annotations, so a payload with an explicit "items": null or
		// "sortBy": null overwrites the initializer despite the declarations. The synthesized equality these
		// replace answered for that through EqualityComparer<T>.Default; hand-written equality must not start
		// throwing where it used to return false.
		string json = """{"items":null,"meta":{"totalItems":0,"itemCount":0,"itemsPerPage":25,"totalPages":0,"currentPage":1,"sortBy":null,"searchBy":null,"filter":null},"links":null}""";

		var deserialized = JsonSerializer.Deserialize<PaginatedResponse<ProductDto>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
		var wellFormed = new PaginatedResponse<ProductDto>([], new PaginatedMeta(0, 0, 25, 0, 1), null);

		Assert.NotEqual(wellFormed, deserialized);
		Assert.NotEqual(wellFormed.GetHashCode(), deserialized.GetHashCode());

		// And two equally malformed ones still answer, rather than each throwing on the way.
		var second = JsonSerializer.Deserialize<PaginatedResponse<ProductDto>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

		Assert.Equal(deserialized, second);
		Assert.Equal(deserialized.GetHashCode(), second.GetHashCode());

	}

	[Fact]
	public async Task Two_identical_requests_produce_equal_envelopes() {

		// The end-to-end version of all of the above: the same request twice, through the real engine.
		var products = TestData.Products().AsQueryable();
		var request = new PaginateQuery { Page = 1, Limit = 3, SortBy = ["rank:DESC"], Search = "i" };

		var first = await products.PageAsync<ProductDto>(request);
		var second = await products.PageAsync<ProductDto>(request);

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());

	}

}
