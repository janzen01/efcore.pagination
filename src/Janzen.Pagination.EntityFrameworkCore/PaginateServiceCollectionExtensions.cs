using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore;

public static class PaginateServiceCollectionExtensions {

    /// <summary>
    ///     Registers pagination and configures provider/integration packages through the builder callback, e.g.
    ///     <c>services.AddPagination(p => { p.UsePostgreSql(); p.AddAspNetCore(); })</c>.
    ///     The builder is only available inside <paramref name="configure" />, so <c>UsePostgreSql()</c> /
    ///     <c>AddAspNetCore()</c> cannot be called anywhere else.
    /// </summary>
    public static IServiceCollection AddPagination(this IServiceCollection services, Action<IPaginationBuilder> configure) {
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		PaginateLike.Strategy = new PortableLikeStrategy();
		configure(new PaginationBuilder(services));
		return services;
	}

}
