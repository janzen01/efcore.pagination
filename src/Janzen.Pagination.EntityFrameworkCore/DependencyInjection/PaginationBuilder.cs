using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

internal sealed class PaginationBuilder(IServiceCollection services) : IPaginationBuilder {

	public IServiceCollection Services { get; } = services;

}
