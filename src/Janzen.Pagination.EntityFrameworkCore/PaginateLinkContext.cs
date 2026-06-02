namespace Janzen.Pagination.EntityFrameworkCore;

/// <summary>
///     Framework-agnostic input for building pagination links: the request path and its query parameters.
///     The ASP.NET Core package builds this from an <c>HttpRequest</c>.
/// </summary>
public sealed record PaginateLinkContext(string Path, IReadOnlyList<KeyValuePair<string, string>> QueryParameters);
