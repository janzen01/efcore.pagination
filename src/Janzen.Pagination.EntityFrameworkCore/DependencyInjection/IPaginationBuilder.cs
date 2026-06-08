using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

/// <summary>
///     Configuration surface handed to the <c>AddPagination</c> callback. Integration packages extend this with
///     methods such as <c>AddAspNetCore()</c>, <c>UseLikeStrategy(...)</c>, and — from the PostgreSql package —
///     <c>UsePostgreSql()</c>, which sets the global pattern-match strategy for all configurations.
/// </summary>
public interface IPaginationBuilder {

	IServiceCollection Services { get; }

}
