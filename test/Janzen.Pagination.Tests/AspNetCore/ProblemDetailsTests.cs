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
	public async Task The_two_pipelines_produce_the_same_payload_when_the_factory_is_registered() {

		// The endpoint filter used to call Results.Problem directly, so the same error came back with a different set
		// of members depending on whether the endpoint was a controller action or a Minimal API handler.
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
		Assert.Equal(expected.Extensions.Keys, actual.Extensions.Keys);

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
