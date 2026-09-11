using Janzen.Pagination.EntityFrameworkCore.Links;
using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.AspNetCore;

/// <summary>Opt-in RFC 8288 <c>Link</c> header for a paginated response — nothing writes it automatically.</summary>
public static class PaginateHttpResponseExtensions {

	/// <summary>
	///     Writes an opt-in RFC 8288 <c>Link</c> response header (rel <c>first</c>/<c>prev</c>/<c>next</c>/<c>last</c>)
	///     from the page's <see cref="PaginatedLinks" />. Absent links are skipped; if none are present — or the
	///     page was produced without a link context, leaving <paramref name="links" /> <see langword="null" /> —
	///     no header is written. Call after paginating, e.g.
	///     <c>HttpContext.Response.AddPaginationLinkHeader(result.Links)</c>.
	/// </summary>
	/// <remarks>
	///     The rels are <b>appended</b> as a further <c>Link</c> header field rather than assigned, so a relation
	///     the response already carries — a <c>describedby</c> written by the handler or by middleware — survives:
	///     RFC 8288 §3 permits several <c>Link</c> fields and conforming clients read them as one set. Calling this
	///     twice for the same response therefore emits the pagination rels twice. The header repeats the request's
	///     whole query string once per rel; see the size note on
	///     <see cref="PaginateLinkContext.QueryParameters" />.
	/// </remarks>
	public static void AddPaginationLinkHeader(this HttpResponse response, PaginatedLinks? links) {
		ArgumentNullException.ThrowIfNull(response);

		if (links is null) return;

		var parts = new List<string>(4);

		if (links.First is not null) parts.Add($"<{links.First}>; rel=\"first\"");
		if (links.Previous is not null) parts.Add($"<{links.Previous}>; rel=\"prev\"");
		if (links.Next is not null) parts.Add($"<{links.Next}>; rel=\"next\"");
		if (links.Last is not null) parts.Add($"<{links.Last}>; rel=\"last\"");

		// Append, not assign: the IHeaderDictionary.Link setter replaces the whole header, which silently dropped
		// any relation a consumer or middleware had already written.
		if (parts.Count > 0) response.Headers.Append("Link", string.Join(", ", parts));
	}

}
