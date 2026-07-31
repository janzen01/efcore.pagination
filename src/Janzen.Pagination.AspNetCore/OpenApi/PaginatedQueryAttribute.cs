using Janzen.Pagination.EntityFrameworkCore.Configuration;

namespace Janzen.Pagination.AspNetCore.OpenApi;

[AttributeUsage(AttributeTargets.Method)]
public abstract class PaginatedQueryAttribute : Attribute {

	public Type ConfigProviderType { get; }

	private protected PaginatedQueryAttribute(Type configProviderType) => ConfigProviderType = configProviderType;

}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PaginatedQueryAttribute<TConfigProvider>() : PaginatedQueryAttribute(typeof(TConfigProvider)) where TConfigProvider : IPaginateConfigProvider;
