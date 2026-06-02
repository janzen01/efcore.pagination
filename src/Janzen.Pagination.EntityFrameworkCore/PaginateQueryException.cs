namespace Janzen.Pagination.EntityFrameworkCore;

public sealed class PaginateQueryException(string message) : Exception(message);
