using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.AspNetCore.Filters;

/// <summary>
///     Minimal API counterpart of <see cref="PaginateExceptionFilter" />: translates a
///     <see cref="PaginateQueryException" /> thrown while handling an endpoint into a consistent 400 Problem Details
///     response. Attached automatically by <c>WithPagination&lt;TProvider&gt;()</c>.
/// </summary>
public sealed class PaginateExceptionEndpointFilter : IEndpointFilter {

	/// <summary>
	///     Runs the rest of the endpoint pipeline; a <see cref="PaginateQueryException" /> becomes a 400 titled
	///     <c>Invalid query</c>, every other exception passes through untouched. Only the members this library
	///     decides are set here: everything the host contributes — <c>traceId</c>, and its own
	///     <c>CustomizeProblemDetails</c> — is applied once, by the framework's problem-details writer, when the
	///     result executes. Building the payload here as well would run that customizer twice, and the documented
	///     sample for it adds a key to a dictionary, so the second pass threw and the request came back a 500.
	/// </summary>
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		try {
			return await next(context);
		} catch (PaginateQueryException exception) {
			return Results.Problem(
				detail: exception.Message,
				statusCode: StatusCodes.Status400BadRequest,
				title: PaginateExceptionFilter.Title,
				extensions: new Dictionary<string, object?> { [PaginateExceptionFilter.CodeExtension] = exception.Code.ToString() });
		}
	}

}
