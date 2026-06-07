using Janzen.Pagination.EntityFrameworkCore.Model;

using Microsoft.AspNetCore.Http;

namespace Janzen.Pagination.AspNetCore;

public static class PaginateHttpResponseExtensions {

	/// <summary>
	///     Writes an opt-in RFC 8288 <c>Link</c> response header (rel <c>first</c>/<c>prev</c>/<c>next</c>/<c>last</c>)
	///     from the page's <see cref="PaginatedLinks" />. Absent links are skipped; if none are present, no header
	///     is written. Call after paginating, e.g. <c>HttpContext.Response.AddPaginationLinkHeader(result.Links)</c>.
	/// </summary>
	public static void AddPaginationLinkHeader(this HttpResponse response, PaginatedLinks links) {
		ArgumentNullException.ThrowIfNull(response);
		ArgumentNullException.ThrowIfNull(links);

		var parts = new List<string>(4);

		if (links.First is not null) parts.Add($"<{links.First}>; rel=\"first\"");
		if (links.Previous is not null) parts.Add($"<{links.Previous}>; rel=\"prev\"");
		if (links.Next is not null) parts.Add($"<{links.Next}>; rel=\"next\"");
		if (links.Last is not null) parts.Add($"<{links.Last}>; rel=\"last\"");

		if (parts.Count > 0) response.Headers.Link = string.Join(", ", parts);
	}

}
