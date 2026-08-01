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

	/// <summary>
	///     Translates a <see cref="PaginateQueryException" /> into a 400 Problem Details result built by the app's
	///     registered <see cref="ProblemDetailsFactory" />, and marks it handled. Any other exception is left unhandled.
	/// </summary>
	public void OnException(ExceptionContext context) {

		if (context.Exception is not PaginateQueryException exception) return;

		var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();

		var problemDetails = factory.CreateProblemDetails(
			context.HttpContext,
			StatusCodes.Status400BadRequest,
			Title,
			detail: exception.Message);

		context.Result = new ObjectResult(problemDetails) {
			StatusCode = problemDetails.Status
		};

		context.ExceptionHandled = true;

	}

}
