namespace Janzen.Pagination.EntityFrameworkCore.Like;

/// <summary>
///     Process-wide default pattern-match strategy used by every pagination query. Defaults to a portable
///     <c>LIKE</c>; set it once at startup (e.g. <c>AddPagination(p =&gt; p.UsePostgreSql())</c>) to switch all
///     configurations to a provider-specific strategy such as PostgreSQL's native <c>ILIKE</c>.
/// </summary>
/// <remarks>Intended to be assigned once during startup (before requests) and read concurrently thereafter.</remarks>
public static class PaginateLikeDefaults {

	/// <summary>
	///     The character the engine escapes <c>\</c>, <c>%</c>, <c>_</c> and <c>[</c> with before handing a pattern
	///     to a strategy. A strategy must declare it as the explicit <c>ESCAPE</c> argument of the call it builds,
	///     or the escaping is read as literal text — see <see cref="IPaginateLikeStrategy.BuildLike" />.
	/// </summary>
	// Expression-bodied, not an initialized auto-property, and that is not style. Constructing
	// PortableLikeStrategy below runs PaginateLikeStrategyBase's type initializer, which reads this member back —
	// a cycle between the two classes. With a backing field the CLR breaks that cycle by handing out whatever the
	// field holds at that moment, so the correctness rested on this member being declared *above* Portable and a
	// reviewer-invisible reorder would have shipped a null ESCAPE on every escaped LIKE. With no field there is
	// nothing to observe half-initialized. Same metadata, same shipped API; don't "tidy" it back.
	public static string EscapeCharacter => "\\";

	/// <summary>
	///     The library's own portable <c>LIKE</c> strategy — what <see cref="Strategy" /> holds until something
	///     replaces it. Published so a composition root that called <c>UsePostgreSql()</c> can put a single
	///     resource, a test host or the whole process back on portable <c>LIKE</c>; the type itself stays internal,
	///     so this instance is the one way to name it.
	/// </summary>
	public static IPaginateLikeStrategy Portable { get; } = new PortableLikeStrategy();

	/// <summary>
	///     The strategy the engine uses to build case-insensitive pattern matches. Never <see langword="null" />:
	///     assigning <see langword="null" /> throws, because the engine dereferences this while composing a query
	///     and would otherwise fail on the next request rather than at the assignment. Assign
	///     <see cref="Portable" /> to go back to the library's own default.
	/// </summary>
	public static IPaginateLikeStrategy Strategy {
		get;
		set {
			ArgumentNullException.ThrowIfNull(value);
			field = value;
		}
	} = Portable;

}
