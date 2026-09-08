using Janzen.Pagination.AspNetCore;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>Binding the six query-string inputs, and what happens to values that do not parse.</summary>
public sealed class QueryParserTests {

	private static PaginateQuery Parse(string queryString) {
		var context = new DefaultHttpContext();
		context.Request.QueryString = new QueryString(queryString);
		return context.Request.ToPaginateQuery();
	}

	private static Task<string> Rejects(PaginateQuery request) {
		return Assertions.RejectsAsync(() => TestData.Products().AsQueryable().PageAsync<ProductDto>(request));
	}

	[Fact]
	public void All_six_inputs_are_bound() {

		var query = Parse("?page=2&limit=25&sortBy=rank:DESC&search=acme&searchBy=name&filter.status=$eq:Active");

		Assert.Equal(2, query.Page);
		Assert.Equal(25, query.Limit);
		Assert.Equal(["rank:DESC"], query.SortBy);
		Assert.Equal("acme", query.Search);
		Assert.Equal(["name"], query.SearchBy);
		Assert.Equal(["$eq:Active"], Assert.Contains("status", query.Filters));

	}

	[Fact]
	public void Missing_inputs_fall_back_to_the_defaults() {

		var query = Parse("");

		Assert.Equal(1, query.Page);
		Assert.Null(query.Limit);
		Assert.Empty(query.SortBy);
		Assert.Null(query.Search);
		Assert.Empty(query.SearchBy);
		Assert.Empty(query.Filters);

	}

	[Fact]
	public void Unknown_parameters_are_ignored() {

		var query = Parse("?page=2&offset=40&utm_source=newsletter");

		Assert.Equal(2, query.Page);
		Assert.Empty(query.Filters);

	}

	[Fact]
	public void Repeated_sortBy_keeps_the_order_from_the_url() {
		Assert.Equal(["status:ASC", "rank:DESC"], Parse("?sortBy=status:ASC&sortBy=rank:DESC").SortBy);
	}

	[Fact]
	public void Filter_field_names_collapse_regardless_of_case() {

		var query = Parse("?filter.Status=$eq:Active&filter.status=$eq:Draft");

		Assert.Single(query.Filters);

	}

	[Fact]
	public void A_filter_with_no_field_name_is_skipped() { Assert.Empty(Parse("?filter.=$eq:x").Filters); }

	[Fact]
	public void A_blank_search_term_is_treated_as_absent() { Assert.Null(Parse("?search=%20%20").Search); }

	[Fact]
	public void The_first_value_wins_for_the_single_valued_inputs() { Assert.Equal(2, Parse("?page=2&page=5").Page); }

	[Theory]
	[InlineData("?page=0")]
	[InlineData("?page=-1")]
	[InlineData("?page=abc")]
	[InlineData("?page=2.0")]
	[InlineData("?page=%2B5")]
	public async Task An_unparseable_page_is_carried_to_execution_as_a_400(string queryString) {
		// The binder cannot throw, so it records the error and the engine raises it when the query runs.
		Assert.Equal("Query parameter 'page' must be a positive integer.", await Rejects(Parse(queryString)));
	}

	[Fact]
	public async Task An_unparseable_limit_is_carried_to_execution_as_a_400() {
		Assert.Equal("Query parameter 'limit' must be a positive integer.", await Rejects(Parse("?limit=abc")));
	}

	[Theory]
	[InlineData("?page=")]
	[InlineData("?page=%20")]
	public void A_blank_page_is_not_an_error_it_is_simply_absent(string queryString) {
		Assert.Equal(1, Parse(queryString).Page);
	}

	[Fact]
	public void A_blank_limit_leaves_the_configured_default_in_force() { Assert.Null(Parse("?limit=").Limit); }

	[Fact]
	public void The_unlimited_limit_survives_binding() {

		// The binder has no configuration, so it cannot know whether this resource allows -1. Dropping it here
		// would make AllowUnlimited unreachable over HTTP while OpenAPI advertises it; the engine is where the
		// config-aware decision belongs, and it still refuses -1 for a resource that never opted in.
		Assert.Equal(-1, Parse("?limit=-1").Limit);

	}

	[Theory]
	[InlineData("?limit=-2")]
	[InlineData("?limit=-0")]
	[InlineData("?limit=+1")]
	[InlineData("?limit=abc")]
	[InlineData("?limit=1.0")]
	public async Task Every_other_malformed_limit_is_still_refused(string queryString) {
		Assert.Equal("Query parameter 'limit' must be a positive integer.", await Rejects(Parse(queryString)));
	}

	[Fact]
	public async Task A_bound_minus_one_is_answered_by_the_config_not_the_binder() {

		// Refused for a resource that did not opt in -- but with the engine's range message, which is what
		// proves the value reached it rather than dying during binding.
		string message = await Rejects(Parse("?limit=-1"));

		Assert.Equal("Query parameter 'limit' must be between 1 and 50.", message);

	}

	[Fact]
	public async Task A_bound_minus_one_pages_a_resource_that_opted_in() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(3, 50)
			.Sortable("id", p => p.Id)
			.DefaultSortBy("id")
			.WithTieBreaker(p => p.Id)
			.AllowUnlimited(100));

		var page = await TestData.Products().AsQueryable().PageAsync<ProductDto>(Parse("?limit=-1"), config);

		Assert.Equal(8, page.Items.Count);

	}

}
