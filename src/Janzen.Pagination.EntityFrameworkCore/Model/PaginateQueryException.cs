namespace Janzen.Pagination.EntityFrameworkCore.Model;

public sealed class PaginateQueryException(string message) : Exception(message);
