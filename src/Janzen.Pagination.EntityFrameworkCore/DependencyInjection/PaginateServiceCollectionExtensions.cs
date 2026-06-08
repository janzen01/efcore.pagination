using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

public static class PaginateServiceCollectionExtensions {

	/// <summary>
	///     Registers pagination integration packages through the builder callback, e.g.
	///     <c>services.AddPagination(p =&gt; p.AddAspNetCore().UsePostgreSql())</c>. The global pattern-match strategy
	///     is set here too via <c>UseLikeStrategy(...)</c> / <c>UsePostgreSql()</c>.
	/// </summary>
	public static IServiceCollection AddPagination(this IServiceCollection services, Action<IPaginationBuilder> configure) {

		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		configure(new PaginationBuilder(services));

		return services;

	}

}
