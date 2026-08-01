using Janzen.Pagination.NodaTime;

namespace Janzen.Pagination.EntityFrameworkCore.DependencyInjection;

/// <summary>NodaTime opt-in for the <c>AddPagination</c> builder: <see cref="UseNodaTime" />.</summary>
public static class PaginationBuilderNodaTimeExtensions {

	/// <summary>
	///     Registers NodaTime (<c>Instant</c> / <c>LocalDate</c>) support with the pagination engine. Call inside the
	///     <c>AddPagination(...)</c> callback: <c>services.AddPagination(p =&gt; p.UseNodaTime())</c>.
	/// </summary>
	public static IPaginationBuilder UseNodaTime(this IPaginationBuilder builder) {
		ArgumentNullException.ThrowIfNull(builder);

		PaginateNodaTime.Register();
		return builder;
	}

}
