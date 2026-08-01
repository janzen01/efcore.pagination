namespace Janzen.Pagination.EntityFrameworkCore.Links;

/// <summary>
///     Framework-agnostic input for building pagination links: the request path and its query parameters.
///     The ASP.NET Core package builds this from an <c>HttpRequest</c>.
/// </summary>
/// <param name="Path">The request path the links are built on, emitted verbatim before the <c>?</c>.</param>
/// <param name="QueryParameters">
///     The request's other query parameters, carried onto every link so filters and sorting survive navigation.
///     Supply keys and values <b>raw</b> — the builder percent-encodes both, so pre-escaping double-encodes them.
///     Any <c>page</c> entry is dropped and re-added per link; repeat a key to carry a multi-valued parameter.
/// </param>
public sealed record PaginateLinkContext(string Path, IReadOnlyList<KeyValuePair<string, string>> QueryParameters);
