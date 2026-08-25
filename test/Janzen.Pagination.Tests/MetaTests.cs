using System.Text.Json;

namespace Janzen.Pagination.Tests;

/// <summary>
///     The <c>meta</c> half of the envelope: the counters' navigability booleans, and the echo of the
///     <b>effective</b> request — the sort, search fields and filters that were actually applied once the
///     configured defaults had their say. The links half lives in <see cref="LinkTests" />.
/// </summary>
public sealed class MetaTests(SqliteFixture fixture) : IClassFixture<SqliteFixture> {

	/// <summary>No default sort, no tie-breaker on the searchable-only fields — used to prove an empty echo is reachable.</summary>
	private readonly static PaginateConfig<Product> SearchByIgnored = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.WithTieBreaker(p => p.Id)
		.IgnoreSearchByInQueryParam()
		.Searchable("name", p => p.Name));

	/// <summary>A default sort whose field is switched off for this caller, so the echo has to drop it too.</summary>
	private readonly static PaginateConfig<Product> DisabledDefaultSort = PaginateConfig<Product>.Create(b => b
		.WithLimits(50, 50)
		.Sortable("rank", p => p.Rank).ShowBadge("admin").When(false)
		.Sortable("id", p => p.Id)
		.DefaultSortBy("rank")
		.DefaultSortBy("id")
		.WithTieBreaker(p => p.Id));

	/// <summary>The serializer defaults ASP.NET Core applies, so the asserted JSON is the one clients see.</summary>
	private readonly static JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

	private async Task<PaginatedResponse<ProductDto>> Page(PaginateQuery request, PaginateConfig<Product>? config = null) {
		await using var context = fixture.CreateContext();
		return await SqliteFixture.Products(context).PageAsync<ProductDto>(request, config);
	}

	[Fact]
	public async Task The_first_page_of_several_has_a_next_but_no_previous() {

		var meta = (await this.Page(new PaginateQuery())).Meta;

		Assert.False(meta.HasPreviousPage);
		Assert.True(meta.HasNextPage);

	}

	[Fact]
	public async Task A_middle_page_navigates_both_ways() {

		var meta = (await this.Page(new PaginateQuery { Page = 2 })).Meta;

		Assert.True(meta.HasPreviousPage);
		Assert.True(meta.HasNextPage);

	}

	[Fact]
	public async Task The_last_page_has_a_previous_but_no_next() {

		var meta = (await this.Page(new PaginateQuery { Page = 3 })).Meta;

		Assert.True(meta.HasPreviousPage);
		Assert.False(meta.HasNextPage);

	}

	[Fact]
	public async Task A_page_past_the_end_reports_no_next_either() {

		// currentPage is not clamped, so page 4 of 3 still has a previous — but nothing follows it.
		var meta = (await this.Page(new PaginateQuery { Page = 4 })).Meta;

		Assert.True(meta.HasPreviousPage);
		Assert.False(meta.HasNextPage);

	}

	[Fact]
	public async Task An_empty_result_set_navigates_nowhere() {

		var meta = (await this.Page(Query.Filter("id", "$eq:999"))).Meta;

		Assert.False(meta.HasPreviousPage);
		Assert.False(meta.HasNextPage);

	}

	[Fact]
	public async Task A_single_page_result_navigates_nowhere() {

		var meta = (await this.Page(new PaginateQuery { Limit = 50 })).Meta;

		Assert.False(meta.HasPreviousPage);
		Assert.False(meta.HasNextPage);

	}

	[Fact]
	public async Task The_requested_sort_is_echoed_in_wire_form() {
		Assert.Equal(["rank:DESC", "name:ASC"], (await this.Page(Query.Sort("rank:DESC", "name:ASC"))).Meta.SortBy);
	}

	[Fact]
	public async Task An_omitted_sort_echoes_the_configured_default() {
		// The whole point of the echo: the client sent nothing, so only the server knows where the arrow goes.
		Assert.Equal(["rank:ASC"], (await this.Page(new PaginateQuery())).Meta.SortBy);
	}

	[Fact]
	public async Task The_tie_breaker_is_not_echoed() {

		// The canonical config appends id as the tie-breaker; it orders the page but was never requested,
		// so a grid rendering the echo must not draw an arrow on it.
		var sortBy = (await this.Page(Query.Sort("rank:DESC"))).Meta.SortBy;

		Assert.Equal(["rank:DESC"], sortBy);

	}

	[Fact]
	public async Task The_echo_uses_the_configured_field_name_not_the_requested_spelling() {
		// Field lookup is case-insensitive, so without normalization the echo would contradict the contract.
		Assert.Equal(["rank:DESC"], (await this.Page(Query.Sort("RANK:desc"))).Meta.SortBy);
	}

	[Fact]
	public async Task A_default_sort_field_disabled_for_this_caller_is_absent_from_the_echo() {
		Assert.Equal(["id:ASC"], (await this.Page(new PaginateQuery(), DisabledDefaultSort)).Meta.SortBy);
	}

	[Fact]
	public async Task An_absent_search_is_echoed_as_null_with_no_fields() {

		var meta = (await this.Page(new PaginateQuery())).Meta;

		Assert.Null(meta.Search);
		Assert.Empty(meta.SearchBy);

	}

	[Fact]
	public async Task A_whitespace_only_term_runs_no_search_and_is_echoed_as_absent() {

		var meta = (await this.Page(Query.Search("   "))).Meta;

		Assert.Null(meta.Search);
		Assert.Empty(meta.SearchBy);

	}

	[Fact]
	public async Task An_omitted_searchBy_echoes_every_searchable_field() {

		var meta = (await this.Page(Query.Search("old"))).Meta;

		Assert.Equal("old", meta.Search);
		Assert.Equal(["name", "description"], meta.SearchBy);

	}

	[Fact]
	public async Task A_narrowed_searchBy_echoes_only_the_named_fields() {
		Assert.Equal(["description"], (await this.Page(Query.Search("old", "description"))).Meta.SearchBy);
	}

	[Fact]
	public async Task A_searchBy_the_config_ignores_echoes_the_defaults_it_actually_used() {
		// IgnoreSearchByInQueryParam drops the request's narrowing; the echo has to report what ran, not what was asked.
		Assert.Equal(["name"], (await this.Page(Query.Search("Widget", "description"), SearchByIgnored)).Meta.SearchBy);
	}

	[Fact]
	public async Task Filters_are_echoed_verbatim_and_grouped_per_field() {

		var request = new PaginateQuery {
			Limit = Query.All,
			Filters = new Dictionary<string, IReadOnlyList<string>> {
				["status"] = ["$eq:Active"],
				["rank"] = ["$gte:20", "$lt:70"]
			}
		};

		var filter = (await this.Page(request)).Meta.Filter;

		Assert.Equal(["$eq:Active"], filter["status"]);
		Assert.Equal(["$gte:20", "$lt:70"], filter["rank"]);

	}

	[Fact]
	public async Task An_unfiltered_request_echoes_an_empty_filter_map() {
		Assert.Empty((await this.Page(new PaginateQuery())).Meta.Filter);
	}

	[Fact]
	public async Task The_serialized_meta_carries_every_key_including_a_null_search() {

		string json = JsonSerializer.Serialize(await this.Page(Query.Sort("rank:DESC")), WebJson);

		// The payload shape is identical on every page, so a client never has to distinguish "absent" from "none".
		Assert.Contains("\"sortBy\":[\"rank:DESC\"]", json);
		Assert.Contains("\"search\":null", json);
		Assert.Contains("\"searchBy\":[]", json);
		Assert.Contains("\"filter\":{}", json);
		Assert.Contains("\"hasPreviousPage\":false", json);
		Assert.Contains("\"hasNextPage\":false", json);

	}

}
