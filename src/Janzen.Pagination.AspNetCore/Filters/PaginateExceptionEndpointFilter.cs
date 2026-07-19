using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.AspNetCore.Filters;

/// <summary>
///     Minimal API counterpart of <see cref="PaginateExceptionFilter" />: translates a
///     <see cref="PaginateQueryException" /> thrown while handling an endpoint into a consistent 400 Problem Details
///     response. Attached automatically by <c>WithPagination&lt;TProvider&gt;()</c>.
/// </summary>
public sealed class PaginateExceptionEndpointFilter : IEndpointFilter {

	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) {
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(next);

		try {
			return await next(context);
		} catch (PaginateQueryException exception) {
			return Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status400BadRequest, title: PaginateExceptionFilter.Title);
		}
	}

}
