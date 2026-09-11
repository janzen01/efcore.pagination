using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.AspNetCore.Filters;

/// <summary>
///     Translates <see cref="PaginateQueryException" /> thrown anywhere during a controller action into a
///     consistent 400 Problem Details response, removing the need for per-action try/catch blocks.
/// </summary>
public sealed class PaginateExceptionFilter : IExceptionFilter {

	// Shared with PaginateExceptionEndpointFilter so both pipelines report the identical title.
	internal const string Title = "Invalid query";

	// The media type RFC 9457 reserves for this payload, and the only one the OpenAPI transformer publishes the
	// 400 under. Shared with PaginatedQueryOperationTransformer so the served response and the document cannot
	// name different types.
	internal const string ProblemJson = "application/problem+json";

	// The ProblemDetails extension carrying PaginateQueryException.Code, so a client can branch on the cause
	// without matching the detail prose. Written as the member name rather than the numeric value: the name is
	// what the documentation and the generated schema name, and it survives members being added.
	internal const string CodeExtension = "code";

	/// <summary>
	///     Translates a <see cref="PaginateQueryException" /> into a 400 Problem Details result built by the app's
	///     registered <see cref="ProblemDetailsFactory" />, and marks it handled. Any other exception is left unhandled.
	///     The result declares <c>application/problem+json</c> instead of leaving the media type to content
	///     negotiation, matching the sibling Minimal API pipeline, the 400 this package publishes in the OpenAPI
	///     document, and the media type RFC 9457 reserves for this payload.
	/// </summary>
	public void OnException(ExceptionContext context) {

		if (context.Exception is not PaginateQueryException exception) return;

		var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();

		var problemDetails = factory.CreateProblemDetails(
			context.HttpContext,
			StatusCodes.Status400BadRequest,
			Title,
			detail: exception.Message);

		problemDetails.Extensions[CodeExtension] = exception.Code.ToString();

		context.Result = new ObjectResult(problemDetails) {
			StatusCode = problemDetails.Status,
			ContentTypes = { ProblemJson }
		};

		context.ExceptionHandled = true;

	}

}
