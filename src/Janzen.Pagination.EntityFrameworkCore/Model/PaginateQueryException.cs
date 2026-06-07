namespace Janzen.Pagination.EntityFrameworkCore.Model;

public sealed class PaginateQueryException : Exception {

	public PaginateQueryException(string message) : base(message) { }

	public PaginateQueryException(string message, Exception innerException) : base(message, innerException) { }

}
