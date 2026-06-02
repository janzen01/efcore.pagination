using Janzen.Pagination.EntityFrameworkCore;

namespace Janzen.Pagination.AspNetCore;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PaginatedQueryAttribute(Type configProviderType) : Attribute {

	public Type ConfigProviderType { get; } = typeof(IPaginateConfigProvider).IsAssignableFrom(configProviderType)
		? configProviderType
		: throw new ArgumentException("Paginated query config provider must implement IPaginateConfigProvider.", nameof(configProviderType));

}
