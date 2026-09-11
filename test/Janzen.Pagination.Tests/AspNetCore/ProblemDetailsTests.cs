using Janzen.Pagination.AspNetCore.Filters;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.Tests.AspNetCore;

/// <summary>
///     Both pipelines turn a <see cref="PaginateQueryException" /> into the same 400, so a consumer sees one
///     error shape whether the endpoint is a controller action or a Minimal API handler.
/// </summary>
public sealed class ProblemDetailsTests {

	/// <summary>A request context carrying the MVC services, which is where <c>ProblemDetailsFactory</c> comes from.</summary>
	private static DefaultHttpContext HttpContextWithMvcServices() {

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddControllers();

		return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

	}

	private static ExceptionContext MvcContext(Exception exception) {
		return new ExceptionContext(new ActionContext(HttpContextWithMvcServices(), new RouteData(), new ActionDescriptor()), []) {
			Exception = exception
		};
	}

	[Fact]
	public void The_mvc_filter_maps_the_exception_to_a_400() {

		var context = MvcContext(new PaginateQueryException("Filter 'price' does not support operator '$ilike'."));

		new PaginateExceptionFilter().OnException(context);

		var result = Assert.IsType<ObjectResult>(context.Result);
		var problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);

		Assert.Equal(400, result.StatusCode);
		Assert.Equal(400, problem.Status);
		Assert.Equal("Invalid query", problem.Title);
		Assert.Equal("Filter 'price' does not support operator '$ilike'.", problem.Detail);
		Assert.True(context.ExceptionHandled);

		// The media type is declared on the result rather than left to content negotiation, matching what the
		// framework's own ProblemDetailsClientErrorFactory does with the identical construction, what the
		// OpenAPI document this package emits advertises, and what RFC 9457 reserves for this payload.
		Assert.Equal(["application/problem+json"], result.ContentTypes);

	}

	[Fact]
	public void The_mvc_filter_leaves_other_exceptions_alone() {

		var context = MvcContext(new InvalidOperationException("something else"));

		new PaginateExceptionFilter().OnException(context);

		Assert.Null(context.Result);
		Assert.False(context.ExceptionHandled);

	}

	[Fact]
	public async Task The_endpoint_filter_maps_the_exception_to_the_same_400() {

		// No RequestServices at all: a hand-built context, and the shape a Minimal-API-only app without MVC gets.
		var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());

		object? result = await new PaginateExceptionEndpointFilter()
			.InvokeAsync(context, _ => throw new PaginateQueryException("Sort direction 'UP' is not supported."));

		var problem = Assert.IsType<ProblemHttpResult>(result);

		Assert.Equal(400, problem.StatusCode);
		Assert.Equal("Invalid query", problem.ProblemDetails.Title);
		Assert.Equal("Sort direction 'UP' is not supported.", problem.ProblemDetails.Detail);

	}

	[Fact]
	public async Task The_endpoint_filter_leaves_enrichment_to_the_framework() {

		// The filter used to pre-build the payload through ProblemDetailsFactory even when it was going to hand it
		// to Results.Problem, and ProblemHttpResult routes that through IProblemDetailsService -- so
		// CustomizeProblemDetails ran twice, and Microsoft's own canonical sample (Extensions.Add) threw on the
		// second pass, turning the documented 400 into a 500. The payload now carries only what this library
		// decides; everything the host adds (traceId, its own customizer) is applied once, by the writer, at
		// execution. MvcPipelineTests asserts the resulting wire parity between the two pipelines.
		const string Message = "Sort direction 'UP' is not supported.";

		var mvc = MvcContext(new PaginateQueryException(Message));
		new PaginateExceptionFilter().OnException(mvc);

		var expected = Assert.IsAssignableFrom<ProblemDetails>(Assert.IsType<ObjectResult>(mvc.Result).Value);

		object? result = await new PaginateExceptionEndpointFilter().InvokeAsync(
			new DefaultEndpointFilterInvocationContext(HttpContextWithMvcServices()),
			_ => throw new PaginateQueryException(Message));

		var actual = Assert.IsType<ProblemHttpResult>(result).ProblemDetails;

		Assert.Equal(expected.Type, actual.Type);
		Assert.Equal(expected.Title, actual.Title);
		Assert.Equal(expected.Status, actual.Status);
		Assert.Equal(expected.Detail, actual.Detail);

		// The factory's own contribution -- traceId -- must NOT be here: its presence is the double enrichment.
		Assert.DoesNotContain("traceId", actual.Extensions.Keys);

	}

	[Fact]
	public async Task The_endpoint_filter_lets_other_exceptions_through() {

		var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());

		await Assert.ThrowsAsync<InvalidOperationException>(() => new PaginateExceptionEndpointFilter()
			.InvokeAsync(context, _ => throw new InvalidOperationException("something else"))
			.AsTask());

	}

	[Fact]
	public async Task The_endpoint_filter_passes_a_successful_result_through() {

		var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());

		object? result = await new PaginateExceptionEndpointFilter().InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

		Assert.Equal("ok", result);

	}

}
