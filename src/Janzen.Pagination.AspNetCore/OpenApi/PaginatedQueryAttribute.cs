using Janzen.Pagination.EntityFrameworkCore.Configuration;

namespace Janzen.Pagination.AspNetCore.OpenApi;

/// <summary>
///     Non-generic base of <see cref="PaginatedQueryAttribute{TConfigProvider}" />, so
///     <see cref="PaginatedQueryOperationTransformer" /> can find the metadata on an endpoint without knowing the
///     provider type. The constructor is <c>private protected</c> — apply the generic attribute, not this one.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public abstract class PaginatedQueryAttribute : Attribute {

	/// <summary>
	///     The <see cref="IPaginateConfigProvider" /> supplying this operation's config.
	///     <see cref="PaginatedQueryOperationTransformer" /> activates it from the application services and documents
	///     the pagination parameters from <see cref="IPaginateConfigProvider.GetConfig" />.
	/// </summary>
	public Type ConfigProviderType { get; }

	private protected PaginatedQueryAttribute(Type configProviderType) => ConfigProviderType = configProviderType;

}

/// <summary>
///     Marks a controller action as paginated for OpenAPI: <see cref="PaginatedQueryOperationTransformer" /> documents
///     the pagination query parameters from <typeparamref name="TConfigProvider" />'s config, plus a <c>400</c> Problem
///     Details response. Write it as <c>[PaginatedQuery&lt;TProvider&gt;]</c>; Minimal API endpoints get it from
///     <c>WithPagination&lt;TProvider&gt;()</c> instead. Metadata only — no runtime effect on the query.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PaginatedQueryAttribute<TConfigProvider>() : PaginatedQueryAttribute(typeof(TConfigProvider)) where TConfigProvider : IPaginateConfigProvider;
