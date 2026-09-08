namespace Janzen.Pagination.EntityFrameworkCore.Configuration;

/// <summary>
///     The limits and guards a configuration falls back to for anything it does not set itself. Every member is
///     optional; an unset one keeps the engine's own default.
/// </summary>
/// <remarks>
///     Two ways to use it, and they compose. Pass one explicitly to
///     <see cref="PaginateConfig{TEntity}.Create(PaginateConfigDefaults, System.Action{PaginateConfigBuilder{TEntity}})" />
///     and only the configurations naming it are affected; assign <see cref="Shared" /> once at startup and every
///     configuration built afterwards picks it up, including those built outside dependency injection.
///     Resolution runs outward from the most specific: a <c>WithLimits</c> / <c>WithGuards</c> / <c>WithX</c> call
///     on the builder beats the object handed to <c>Create</c>, which beats <see cref="Shared" />, which beats the
///     engine's constant. So a shared default never silently overrides a value someone wrote down, and any
///     configuration can set its own. What it cannot do is <i>remove</i> one: "unset" and "explicitly none" are
///     both absence here, so a shared <see cref="MaxOffset" /> can be raised per configuration but not lifted —
///     put a ceiling that some resources must not have on those resources instead of in the shared object.
///     The values here are validated when a configuration is built rather than when they are assigned: an init
///     accessor cannot reject the way a builder method does.
///     Note what is deliberately absent: there is no shared <c>AllowUnlimited</c>. An unbounded read is a promise
///     about one resource's size, and a default that turned it on everywhere would be exactly the promise nobody
///     can make.
/// </remarks>
public sealed record PaginateConfigDefaults {

	/// <summary>
	///     Applies to every configuration built after it is assigned, unless that configuration passes its own
	///     defaults object or sets the value directly. Assign it once during startup, before the first
	///     configuration is built: a configuration resolves its values at <c>Build()</c> time and does not
	///     re-read this afterwards. Assigning <see langword="null" /> throws; assign a new instance rather than
	///     mutating, which is what a <c>with</c> expression on this record is for.
	/// </summary>
	public static PaginateConfigDefaults Shared {
		get;
		set {
			ArgumentNullException.ThrowIfNull(value);
			field = value;
		}
	} = new();

	/// <summary>Page size for a request that omits <c>limit</c>. Pairs with <see cref="MaxLimit" />: a configuration needs both from somewhere, or <c>Build()</c> throws.</summary>
	public int? DefaultLimit { get; init; }

	/// <summary>Upper bound on the request's <c>limit</c>. Pairs with <see cref="DefaultLimit" />.</summary>
	public int? MaxLimit { get; init; }

	/// <summary>Maximum comma-separated values in one filter list. The engine's own default is 100.</summary>
	public int? MaxFilterValues { get; init; }

	/// <summary>Maximum filter criteria in one request, counted across every field. The engine's own default is 20.</summary>
	public int? MaxFilterConditions { get; init; }

	/// <summary>Maximum <c>sortBy</c> values one request may send. The engine's own default is 5.</summary>
	public int? MaxSortFields { get; init; }

	/// <summary>Maximum characters in the <c>search</c> term. The engine's own default is 256.</summary>
	public int? MaxSearchLength { get; init; }

	/// <summary>Minimum characters in the <c>search</c> term. The engine's own default is 1 — any non-blank term runs.</summary>
	public int? MinSearchLength { get; init; }

	/// <summary>Maximum rows a request may skip. The engine's own default is no ceiling at all.</summary>
	public int? MaxOffset { get; init; }

}
