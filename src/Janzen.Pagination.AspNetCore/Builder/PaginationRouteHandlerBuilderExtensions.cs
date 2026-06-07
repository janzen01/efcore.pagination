using Janzen.Pagination.AspNetCore.Filters;
using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore.Configuration;

using Microsoft.AspNetCore.Http;

// Declared in Microsoft.AspNetCore.Builder so `.WithPagination<T>()` is discoverable next to MapGet/MapPost
// without an extra using directive.
namespace Microsoft.AspNetCore.Builder;

public static class PaginationRouteHandlerBuilderExtensions {

	/// <summary>
	///     Marks a Minimal API endpoint as paginated: attaches the <c>[PaginatedQuery]</c> metadata so the OpenAPI
	///     operation transformer documents the pagination query parameters and the 400 response, and adds the
	///     <see cref="PaginateExceptionEndpointFilter" /> so invalid input becomes a 400 Problem Details.
	/// </summary>
	public static RouteHandlerBuilder WithPagination<TConfigProvider>(this RouteHandlerBuilder builder)
		where TConfigProvider : IPaginateConfigProvider {
		ArgumentNullException.ThrowIfNull(builder);

		builder.WithMetadata(new PaginatedQueryAttribute<TConfigProvider>());
		builder.AddEndpointFilter<PaginateExceptionEndpointFilter>();
		return builder;
	}

}
