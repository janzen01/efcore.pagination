using Janzen.Pagination.AspNetCore.Filters;
using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Configuration;

using Microsoft.AspNetCore.Http;

using System.Diagnostics.CodeAnalysis;

// Declared in Microsoft.AspNetCore.Builder so `.WithPagination<T>()` is discoverable next to MapGet/MapPost
// without an extra using directive.
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Pagination for Minimal API endpoints: adds <c>WithPagination&lt;TConfigProvider&gt;()</c> to the
///     <see cref="RouteHandlerBuilder" /> returned by <c>MapGet</c>/<c>MapPost</c>.
/// </summary>
public static class PaginationRouteHandlerBuilderExtensions {

	/// <summary>
	///     Marks a Minimal API endpoint as paginated: attaches the <c>[PaginatedQuery]</c> metadata so the OpenAPI
	///     operation transformer documents the pagination query parameters and the 400 response, and adds the
	///     <see cref="PaginateExceptionEndpointFilter" /> so invalid input becomes a 400 Problem Details.
	/// </summary>
	// The pair the query entry points carry, for the same reason one level out: the marked endpoint's OpenAPI
	// document is generated from a config the transformer builds reflectively. Without it, silence here read as
	// "analysed and safe" beside the ten members that do warn.
	[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	public static RouteHandlerBuilder WithPagination<
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfigProvider>(
		this RouteHandlerBuilder builder)
		where TConfigProvider : IPaginateConfigProvider {
		ArgumentNullException.ThrowIfNull(builder);

		builder.WithMetadata(new PaginatedQueryAttribute<TConfigProvider>());
		builder.AddEndpointFilter<PaginateExceptionEndpointFilter>();
		return builder;
	}

}
