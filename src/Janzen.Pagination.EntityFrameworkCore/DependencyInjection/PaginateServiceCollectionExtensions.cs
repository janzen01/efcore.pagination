using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

/// <summary>
///     The <c>AddPagination</c> entry point on <see cref="IServiceCollection" />: it opens an
///     <see cref="IPaginationBuilder" /> and hands it to the callback — the calls made on that builder do the
///     registering, not <c>AddPagination</c> itself. Integration packages plug in there as builder extensions.
/// </summary>
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
