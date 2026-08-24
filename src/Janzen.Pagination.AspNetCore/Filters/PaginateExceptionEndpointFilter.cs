using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.AspNetCore.Filters;

/// <summary>
///     Minimal API counterpart of <see cref="PaginateExceptionFilter" />: translates a
///     <see cref="PaginateQueryException" /> thrown while handling an endpoint into a consistent 400 Problem Details
///     response. Attached automatically by <c>WithPagination&lt;TProvider&gt;()</c>.
/// </summary>
public sealed class PaginateExceptionEndpointFilter : IEndpointFilter {

	/// <summary>
	///     Runs the rest of the endpoint pipeline; a <see cref="PaginateQueryException" /> becomes a 400 titled
	///     <c>Invalid query</c>, every other exception passes through untouched. The payload is built by the app's
	///     <see cref="ProblemDetailsFactory" /> when one is registered, so a request that reaches a Minimal API
	///     handler comes back with the same members (<c>type</c>, <c>traceId</c>) as one that reaches a controller.
	/// </summary>
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		try {
			return await next(context);
		} catch (PaginateQueryException exception) {
			// GetService, not GetRequiredService: the factory comes with the MVC services, and a Minimal-API-only app
			// has none — there the bare overload is the right answer rather than a 500 about a missing service. The
			// null-conditional covers a hand-built HttpContext, whose RequestServices is unset despite the type.
			var factory = context.HttpContext.RequestServices?.GetService<ProblemDetailsFactory>();

			return factory is null
				? Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest, title: PaginateExceptionFilter.Title)
				: Results.Problem(factory.CreateProblemDetails(context.HttpContext, StatusCodes.Status400BadRequest, PaginateExceptionFilter.Title, detail: exception.Message));
		}
	}

}
