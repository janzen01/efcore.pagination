using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using Janzen.Pagination.EntityFrameworkCore;

namespace Janzen.Pagination.AspNetCore;

/// <summary>
///     Translates <see cref="PaginateQueryException" /> thrown anywhere during a controller action into a
///     consistent 400 Problem Details response, removing the need for per-action try/catch blocks.
/// </summary>
public sealed class PaginateExceptionFilter : IExceptionFilter {

	private const string Title = "Invalid query";

	public void OnException(ExceptionContext context) {

		if (context.Exception is not PaginateQueryException exception) return;

		var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();

		var problemDetails = factory.CreateProblemDetails(
			context.HttpContext,
			StatusCodes.Status400BadRequest,
			Title,
			detail: exception.Message);

		problemDetails.Extensions.Remove("traceId");

		context.Result = new ObjectResult(problemDetails) {
			StatusCode = StatusCodes.Status400BadRequest
		};

		context.ExceptionHandled = true;

	}

}
