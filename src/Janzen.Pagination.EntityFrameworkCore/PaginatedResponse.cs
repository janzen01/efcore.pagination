namespace Janzen.Pagination.EntityFrameworkCore;

public sealed record PaginatedResponse<T>(
	IReadOnlyList<T> Items,
	PaginatedMeta Meta,
	PaginatedLinks Links
);

public sealed record PaginatedMeta(
	int TotalItems,
	int ItemCount,
	int ItemsPerPage,
	int TotalPages,
	int CurrentPage
);

public sealed record PaginatedLinks(
	string First,
	string Previous,
	string Next,
	string Last
);
