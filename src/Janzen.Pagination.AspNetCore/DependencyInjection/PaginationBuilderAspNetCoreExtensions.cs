using Janzen.Pagination.AspNetCore.Filters;
using Janzen.Pagination.AspNetCore.ModelBinding;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

// Declared in the core DI namespace so `p.AddAspNetCore()` is discoverable wherever AddPagination is in scope,
// without an extra using directive — the same placement the PostgreSql and NodaTime builder extensions use.
namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

public static class PaginationBuilderAspNetCoreExtensions {

	/// <summary>
	///     Registers the pagination query-string model binder and the 400 ProblemDetails exception filter.
	///     Use inside <c>AddPagination(...)</c>; the host must also call <c>AddControllers()</c>.
	/// </summary>
	public static IPaginationBuilder AddAspNetCore(this IPaginationBuilder builder) {

		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.Configure<MvcOptions>(options => {
			options.ModelBinderProviders.Insert(0, new PaginateQueryModelBinderProvider());
			options.Filters.Add<PaginateExceptionFilter>();
		});

		return builder;

	}

}
