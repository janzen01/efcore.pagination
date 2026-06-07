using Microsoft.Extensions.DependencyInjection;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

/// <summary>
///     Configuration surface handed to the <c>AddPagination</c> callback. Integration packages extend this with
///     methods such as <c>AddAspNetCore()</c>. (Provider pattern-match strategies like <c>UsePostgreSql()</c> are
///     configured per resource on the <c>PaginateConfig</c> builder instead.)
/// </summary>
public interface IPaginationBuilder {

	IServiceCollection Services { get; }

}
