namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     Thrown when a pagination request is invalid (unknown field, malformed filter or sort, out-of-range
///     page or limit). The ASP.NET Core integration translates it to a 400 ProblemDetails response, so the
///     message must stay client-safe.
/// </summary>
public sealed class PaginateQueryException : Exception {

	/// <summary>Creates the exception with a client-safe message.</summary>
	public PaginateQueryException(string message) : base(message) { }

	/// <summary>Creates the exception with a client-safe message and the underlying cause (never surfaced to clients).</summary>
	public PaginateQueryException(string message, Exception innerException) : base(message, innerException) { }

}
