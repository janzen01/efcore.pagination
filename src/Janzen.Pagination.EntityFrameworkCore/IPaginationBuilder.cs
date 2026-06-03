using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore;

/// <summary>
///     Configuration surface handed to the <c>AddPagination</c> callback. Provider and integration packages
///     extend this with methods such as <c>UsePostgreSql()</c> and <c>AddAspNetCore()</c>.
/// </summary>
public interface IPaginationBuilder {

	IServiceCollection Services { get; }

}
