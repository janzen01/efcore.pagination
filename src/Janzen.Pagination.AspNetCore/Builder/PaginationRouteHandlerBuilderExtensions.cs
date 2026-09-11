using Janzen.Pagination.AspNetCore.Filters;
using Janzen.Pagination.AspNetCore.OpenApi;
using Janzen.Pagination.EntityFrameworkCore;
using Janzen.Pagination.EntityFrameworkCore.Configuration;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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

	/// <summary>
	///     Marks every endpoint in a route group as paginated: the same metadata and filter as the
	///     <see cref="RouteHandlerBuilder" /> overload, applied once to the group that <c>MapGroup</c> returns.
	///     Without it a grouped endpoint carries neither, so its invalid input escapes the filter as an unhandled
	///     exception rather than a <c>400</c> and the OpenAPI document describes none of its pagination
	///     parameters.
	/// </summary>
	// RouteGroupBuilder rather than IEndpointConventionBuilder, which is the type the decision named: the
	// interface loses IEndpointRouteBuilder, so `MapGroup(...).WithPagination<T>()` would return something no
	// endpoint can be mapped onto and the group would have to be declared in two statements. The concrete type
	// is also an unambiguous overload against the one above, which no interface-typed receiver would be.
	[RequiresUnreferencedCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	[RequiresDynamicCode(PaginateQueryableExtensions.AotIncompatibleMessage)]
	public static RouteGroupBuilder WithPagination<
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfigProvider>(
		this RouteGroupBuilder builder)
		where TConfigProvider : IPaginateConfigProvider {
		ArgumentNullException.ThrowIfNull(builder);

		builder.WithMetadata(new PaginatedQueryAttribute<TConfigProvider>());
		builder.AddEndpointFilter<RouteGroupBuilder, PaginateExceptionEndpointFilter>();
		return builder;
	}

}
