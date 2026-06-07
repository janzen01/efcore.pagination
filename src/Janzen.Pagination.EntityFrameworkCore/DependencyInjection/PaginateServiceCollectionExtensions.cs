using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

public static class PaginateServiceCollectionExtensions {

	/// <summary>
	///     Registers pagination integration packages through the builder callback, e.g.
	///     <c>services.AddPagination(p => p.AddAspNetCore())</c>. The provider pattern-match strategy is configured
	///     per resource on the <c>PaginateConfig</c> builder (e.g. <c>b.UsePostgreSql()</c>), not here.
	/// </summary>
	public static IServiceCollection AddPagination(this IServiceCollection services, Action<IPaginationBuilder> configure) {

		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		configure(new PaginationBuilder(services));

		return services;

	}

}
