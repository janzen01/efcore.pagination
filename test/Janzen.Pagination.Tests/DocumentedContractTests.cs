using Janzen.Pagination.AspNetCore;
using Janzen.Pagination.AspNetCore.Filters;
using Janzen.Pagination.EntityFrameworkCore.Engine;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Janzen.Pagination.Tests;

/// <summary>
///     Behaviour a published page now states precisely and nothing else asserted. Each of these was true before
///     this class existed — they pin a sentence, they do not change one — so the value is that a later edit to
///     the engine cannot quietly make the documentation wrong while the suite stays green.
/// </summary>
public sealed class DocumentedContractTests {

	private static PaginateQuery Parse(string queryString) {
		var context = new DefaultHttpContext();
		context.Request.QueryString = new QueryString(queryString);
		return context.Request.ToPaginateQuery();
	}

	/// <summary>
	///     <c>reference/response/</c>, the request echo: <c>sortBy</c> and <c>searchBy</c> report canonical field
	///     names, <c>filter</c> reports the spelling the request used.
	/// </summary>
	[Fact]
	public async Task The_filter_echo_keeps_the_requests_own_spelling_where_sortBy_does_not() {

		var page = await TestData.Products().AsQueryable()
			.PageAsync<ProductDto>(Parse("?sortBy=RANK:desc&filter.STATUS=$eq:Active"));

		Assert.Equal(["rank:DESC"], page.Meta.SortBy);

		// The key TEXT, not a lookup: the echo dictionary is case-insensitive, so looking "status" up would
		// succeed either way and prove nothing about the spelling a client renders.
		Assert.Equal("STATUS", Assert.Single(page.Meta.Filter).Key);

	}

	/// <summary>
	///     <c>reference/query-string/</c>, <c>page</c>: the rule is "no surrounding whitespace", which leading
	///     zeros do not break, and a blank value reads as absent rather than as a rejection.
	/// </summary>
	[Theory]
	[InlineData("?page=007", 7)]
	[InlineData("?page=", 1)]
	[InlineData("?page=%20", 1)]
	public void A_padded_or_blank_page_is_not_a_rejection(string queryString, int expected) {
		Assert.Equal(expected, Parse(queryString).Page);
	}

	/// <summary>
	///     <c>reference/errors/</c>, filter dispatch: case-variant keys land on one field, and each criterion
	///     value still consumes a condition — the saving is the field, not the count.
	/// </summary>
	[Fact]
	public async Task Case_variant_filter_keys_are_one_field_but_still_two_conditions() {

		var request = Parse("?filter.Status=$eq:Active&filter.status=$eq:Draft");

		Assert.Equal(2, Assert.Single(request.Filters).Value.Count);

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(3, Query.All)
			.WithGuards(maxFilterConditions: 1)
			.WithTieBreaker(p => p.Id)
			.Filterable("status", p => p.Status));

		string message = await Assertions.RejectsAsync(
			() => TestData.Products().AsQueryable().PageAsync<ProductDto>(request, config));

		Assert.Equal("Too many filter conditions; at most 1 are allowed.", message);

	}

	/// <summary>
	///     <c>reference/query-string/</c>, value formats: the fractional-second specifier is optional-width, so
	///     one to seven digits are accepted and a caller does not have to pad to seven.
	/// </summary>
	[Theory]
	[InlineData("10:30:00.5")]
	[InlineData("10:30:00.5000000")]
	[InlineData("10:30:00")]
	[InlineData("10:30")]
	public void A_TimeOnly_takes_any_number_of_fractional_digits(string value) {
		Assert.NotNull(PaginateValueConverter.Convert(value, typeof(TimeOnly), "opensAt"));
	}

	/// <summary>
	///     <c>reference/configuration/#allowunlimited</c>: an unlimited read that matches nothing reports
	///     <c>itemsPerPage: 0</c>, which is the row count rather than the requested <c>-1</c> — the value the
	///     shipped XML doc now warns against dividing by.
	/// </summary>
	[Fact]
	public async Task An_unlimited_read_that_matches_nothing_reports_zero_rather_than_the_requested_limit() {

		var config = PaginateConfig<Product>.Create(b => b
			.WithLimits(3, Query.All)
			.AllowUnlimited(100)
			.WithTieBreaker(p => p.Id)
			.Filterable("name", p => p.Name));

		var page = await TestData.Products().AsQueryable().PageAsync<ProductDto>(
			new PaginateQuery { Limit = -1, Filters = new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["$eq:nothing matches this"] } },
			config);

		Assert.Empty(page.Items);
		Assert.Equal(0, page.Meta.ItemsPerPage);
		Assert.Equal(0, page.Meta.ItemCount);
		Assert.Equal(0, page.Meta.TotalItems);
		Assert.Equal(0, page.Meta.TotalPages);
		Assert.False(page.Meta.HasNextPage);
		Assert.False(page.Meta.HasPreviousPage);

	}

	/// <summary>
	///     <c>integrations/aspnetcore/</c>: the Minimal API fallback is not a thinner payload. The framework's
	///     problem-details defaults fill <c>type</c> even with no services registered at all, which is what the
	///     page says and what an app without MVC and without <c>AddProblemDetails()</c> actually receives.
	/// </summary>
	[Fact]
	public async Task The_endpoint_filters_fallback_still_carries_a_type() {

		var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());

		object? result = await new PaginateExceptionEndpointFilter()
			.InvokeAsync(context, _ => throw new PaginateQueryException("Sort direction 'UP' is not supported."));

		Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Type);

	}

}
