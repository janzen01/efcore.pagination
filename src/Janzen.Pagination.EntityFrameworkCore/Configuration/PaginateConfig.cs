using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Collections.Frozen;
using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore.Configuration;

/// <summary>
///     The entity-agnostic, read-only view of a <see cref="PaginateConfig{TEntity}" />: the page-size limits, the DoS
///     guards, the default sort, the field metadata and the <c>searchBy</c> opt-out. The ASP.NET Core OpenAPI
///     transformer reads a config through this interface, and it is equally available to you — via
///     <see cref="IPaginateConfigProvider.GetConfig" /> — for a <c>/meta</c> endpoint, an admin UI or a contract test.
///     Immutable, so a config is safe to hold in a static field.
/// </summary>
public interface IPaginateConfig {

	/// <summary>
	///     Page size applied when the request omits <c>limit</c>. Set by
	///     <see cref="PaginateConfigBuilder{TEntity}.WithLimits" />.
	/// </summary>
	int DefaultLimit { get; }

	/// <summary>
	///     Upper bound on the request's <c>limit</c>; a larger value is rejected with a 400, never clamped. Set by
	///     <see cref="PaginateConfigBuilder{TEntity}.WithLimits" />, which is required — there is no implicit page size.
	/// </summary>
	int MaxLimit { get; }

	/// <summary>
	///     Maximum number of comma-separated values in one filter list — <c>$in</c>, <c>$btw</c> and <c>$contains</c> on
	///     a collection field. Counted per criterion, so it bounds one list rather than the request; a longer list is a
	///     400. Defaults to 100, set by <see cref="PaginateConfigBuilder{TEntity}.WithGuards" />.
	/// </summary>
	int MaxFilterValues { get; }

	/// <summary>
	///     Maximum number of filter criteria in one request, counted across every <c>filter.&lt;field&gt;</c> value of
	///     every field — 20 in total by default, not 20 per field. Exceeding it is a 400. Set by
	///     <see cref="PaginateConfigBuilder{TEntity}.WithGuards" />.
	/// </summary>
	int MaxFilterConditions { get; }

	/// <summary>
	///     Maximum number of <c>sortBy</c> values one request may send; more is a 400. Only request-supplied sorts count
	///     — <see cref="DefaultSortBy" /> entries and the configured tie-breaker are not measured against it. Defaults
	///     to 5, set by <see cref="PaginateConfigBuilder{TEntity}.WithGuards" />.
	/// </summary>
	int MaxSortFields { get; }

	/// <summary>
	///     Maximum number of characters in the <c>search</c> term; a longer term is a 400 before the query runs.
	///     Defaults to 256, set by <see cref="PaginateConfigBuilder{TEntity}.WithGuards" />.
	/// </summary>
	int MaxSearchLength { get; }

	/// <summary>
	///     Minimum number of characters in the <c>search</c> term, measured after trimming; a shorter term is a 400
	///     before the query runs. Defaults to 1 — any non-blank term runs. Set by
	///     <see cref="PaginateConfigBuilder{TEntity}.WithMinSearchLength" />.
	/// </summary>
	/// <remarks>A default interface member so an existing external implementation of this interface keeps compiling.</remarks>
	int MinSearchLength => 1;

	/// <summary>
	///     Maximum number of rows a request may skip — <c>(page - 1) × limit</c> — or <see langword="null" /> for no
	///     ceiling, which is the default. A deeper page is a 400 raised before the count query. Set by
	///     <see cref="PaginateConfigBuilder{TEntity}.WithMaxOffset" />.
	/// </summary>
	/// <remarks>A default interface member so an existing external implementation of this interface keeps compiling.</remarks>
	int? MaxOffset => null;

	/// <summary>
	///     Row ceiling for <c>limit=-1</c>, or <see langword="null" /> when this resource does not accept it — the
	///     default. Set by <see cref="PaginateConfigBuilder{TEntity}.AllowUnlimited" />.
	/// </summary>
	/// <remarks>A default interface member so an existing external implementation of this interface keeps compiling.</remarks>
	int? UnlimitedMaxRows => null;

	/// <summary>
	///     Sorts applied, in declaration order, when the request sends no <c>sortBy</c> — empty when none was declared.
	///     A request that does send <c>sortBy</c> replaces these entirely; they never merge. Each field must also be
	///     declared sortable or <see cref="PaginateConfig{TEntity}.Create(System.Action{PaginateConfigBuilder{TEntity}})" /> throws, and an entry disabled by
	///     <see cref="PaginateConfigBuilder{TEntity}.When" /> is skipped rather than fatal. The configured tie-breaker
	///     is appended last either way.
	/// </summary>
	IReadOnlyList<PaginateSort> DefaultSortBy { get; }

	/// <summary>
	///     Metadata for every declared sortable field — public name, the selector's CLR type and optional
	///     <see cref="PaginateBadge" />. The ASP.NET Core OpenAPI transformer turns it into the <c>sortBy</c> enum
	///     (<c>name:ASC</c> / <c>name:DESC</c>). This is the documented surface, so a field disabled by
	///     <see cref="PaginateConfigBuilder{TEntity}.When" /> is still listed.
	/// </summary>
	IReadOnlyList<PaginateFieldMetadata> SortableFields { get; }

	/// <summary>
	///     Metadata for every declared searchable field — public name, always <c>string</c> as the type, and optional
	///     <see cref="PaginateBadge" />. The ASP.NET Core OpenAPI transformer turns it into the <c>searchBy</c> enum,
	///     which it omits entirely when <see cref="IgnoreSearchByInQueryParam" /> is set. A field disabled by
	///     <see cref="PaginateConfigBuilder{TEntity}.When" /> is still listed.
	/// </summary>
	IReadOnlyList<PaginateFieldMetadata> SearchableFields { get; }

	/// <summary>
	///     Metadata for every declared filterable field — public name, the value's CLR type (a nullable type is
	///     reported as its underlying type), the whitelisted <see cref="PaginateFilterOperator" /> set and optional
	///     <see cref="PaginateBadge" />. The ASP.NET Core OpenAPI transformer emits one <c>filter.&lt;name&gt;</c>
	///     query parameter per entry, listing that field's operator tokens plus the <c>$not</c> / <c>$and</c> /
	///     <c>$or</c> prefixes. A field disabled by <see cref="PaginateConfigBuilder{TEntity}.When" /> is still listed.
	/// </summary>
	IReadOnlyList<PaginateFilterFieldMetadata> FilterableFields { get; }

	/// <summary>
	///     Drops the <c>searchBy</c> query parameter from the contract: <c>search</c> then spans every searchable field
	///     enabled for this caller, a supplied <c>searchBy</c> is neither applied nor validated, and the ASP.NET Core
	///     OpenAPI transformer stops advertising the parameter. Set by
	///     <see cref="PaginateConfigBuilder{TEntity}.IgnoreSearchByInQueryParam" />.
	/// </summary>
	bool IgnoreSearchByInQueryParam { get; }

}

/// <summary>
///     Entity-agnostic provider of a resource's pagination configuration. It exists because the ASP.NET Core OpenAPI
///     transformer only has the provider's <c>Type</c> — from <c>[PaginatedQuery&lt;TProvider&gt;]</c> or
///     <c>WithPagination&lt;TProvider&gt;()</c>, both constrained to this interface — so it cannot name the entity and
///     reads the configuration as <see cref="IPaginateConfig" /> metadata. Consumers implement
///     <see cref="IPaginateConfigProvider{TEntity}" />, which fulfils this one.
/// </summary>
public interface IPaginateConfigProvider {

	/// <summary>
	///     Exposes the resource's limits and sortable / searchable / filterable field metadata without naming the
	///     entity. The OpenAPI transformer activates the provider type through <c>ActivatorUtilities</c> and calls this
	///     once per documented operation, so a provider with a parameterless constructor works without being registered
	///     in DI.
	/// </summary>
	IPaginateConfig GetConfig();

}

/// <summary>
///     The entity-typed provider a consumer implements: it names the entity, so the configuration comes back as
///     <see cref="PaginateConfig{TEntity}" />. This is how the ASP.NET Core integration finds a config to document; it
///     is optional for querying, which takes the config directly. Implement only the typed <c>GetConfig()</c> — the
///     non-generic <see cref="IPaginateConfigProvider" /> member is provided over it. Name the implementing type in
///     <c>[PaginatedQuery&lt;TProvider&gt;]</c> (controllers) or <c>WithPagination&lt;TProvider&gt;()</c> (Minimal
///     APIs) and the registered operation transformer documents the resource's parameters.
/// </summary>
public interface IPaginateConfigProvider<TEntity> : IPaginateConfigProvider {

	/// <summary>
	///     Supplies the resource's <see cref="PaginateConfig{TEntity}" /> — the one member an implementer writes. Its
	///     return value is what the ASP.NET Core OpenAPI integration documents; querying does not go through it, since
	///     the entry points take a config directly.
	/// </summary>
	/// <remarks>
	///     Building a config validates the declared fields and freezes three dictionaries, so build it once and return
	///     the same instance — a static field or a DI singleton. Rebuilding per request works but is wasted allocation;
	///     the exception is per-user gating with <see cref="PaginateConfigBuilder{TEntity}.When" />, where one cached
	///     config per role is the cheap route.
	/// </remarks>
	new PaginateConfig<TEntity> GetConfig();

	/// <summary>
	///     Fulfils the non-generic <see cref="IPaginateConfigProvider.GetConfig" /> by delegating to the typed overload,
	///     so implementers only write the typed <c>GetConfig()</c> — the base member is provided for free. The <c>new</c>
	///     typed member hides the base one in this scope, so this delegates to the implementer's method, not itself.
	/// </summary>
	IPaginateConfig IPaginateConfigProvider.GetConfig() => GetConfig();

}

/// <summary>
///     One sort entry: a <paramref name="Field" /> name and the <paramref name="Direction" /> to order it in.
///     <see cref="IPaginateConfig.DefaultSortBy" /> is a list of these, added via
///     <see cref="PaginateConfigBuilder{TEntity}.DefaultSortBy" /> and applied in that order when the request carries
///     no <c>sortBy</c>. A default sort field must also be sortable —
///     <see cref="PaginateConfig{TEntity}.Create(System.Action{PaginateConfigBuilder{TEntity}})" /> throws otherwise — and one disabled by <c>When(false)</c> is
///     skipped rather than failing the query.
/// </summary>
/// <param name="Field">Name of a field that must also be declared <c>Sortable</c>.</param>
/// <param name="Direction">Ascending or descending.</param>
public sealed record PaginateSort(string Field, PaginateSortDirection Direction);

/// <summary>
///     An optional presentation badge attached to a field: a <paramref name="Name" /> label and an optional
///     <paramref name="CssClass" />. Surfaced in the OpenAPI metadata and rendered as a chip by the API reference UI;
///     the class is how you color it, via the renderer's custom CSS. When set it must start with <c>language-</c>
///     (see <see cref="PaginateConfigBuilder{TEntity}.ShowBadge" />).
/// </summary>
/// <param name="Name">The chip's label text.</param>
/// <param name="CssClass">Optional CSS class to color the chip. When set it <b>must</b> start with <c>language-</c>; <see langword="null" /> gives a neutral chip.</param>
public sealed record PaginateBadge(string Name, string? CssClass);

/// <summary>
///     Read-only view of one declared sortable or searchable field, as listed by
///     <see cref="IPaginateConfig.SortableFields" /> and <see cref="IPaginateConfig.SearchableFields" />:
///     <paramref name="Name" /> is the field name as used in <c>sortBy</c> or <c>searchBy</c>,
///     <paramref name="Type" /> the sort selector's value type — always <c>typeof(string)</c> on a searchable field —
///     and <paramref name="Badge" /> the optional <see cref="PaginateBadge" />. Use when documenting or introspecting
///     a config yourself, the way the OpenAPI transformer does. Conditional fields are listed whatever their
///     <c>When(...)</c> condition.
/// </summary>
/// <param name="Name">The field name as used in <c>sortBy</c> or <c>searchBy</c>. Matched case-insensitively.</param>
/// <param name="Type">The sort selector's value type; always <c>typeof(string)</c> for a searchable field.</param>
/// <param name="Badge">Optional presentation chip for the generated docs, or <see langword="null" />.</param>
public sealed record PaginateFieldMetadata(string Name, Type Type, PaginateBadge? Badge = null);

/// <summary>
///     Read-only view of one declared filterable field, as listed by <see cref="IPaginateConfig.FilterableFields" />:
///     <paramref name="Name" /> is the token in <c>filter.&lt;name&gt;=$op:value</c>, <paramref name="Type" /> the
///     filtered value's type (a nullable column reports its underlying type), <paramref name="Operators" /> the
///     allow-list of <see cref="PaginateFilterOperator" /> values granted for it — any other operator is a 400 — and
///     <paramref name="Badge" /> the optional <see cref="PaginateBadge" />. Use when documenting or introspecting a
///     config yourself, the way the OpenAPI transformer does. Conditional fields are listed whatever their
///     <c>When(...)</c> condition.
/// </summary>
/// <param name="Name">The field token in <c>filter.&lt;name&gt;=$op:value</c>. Matched case-insensitively.</param>
/// <param name="Type">The filtered value's type; a nullable column reports its <b>underlying</b> type (<c>int?</c> is reported as <c>int</c>).</param>
/// <param name="Operators">The operators granted for this field — any other operator in a request is a 400.</param>
/// <param name="Badge">Optional presentation chip for the generated docs, or <see langword="null" />.</param>
public sealed record PaginateFilterFieldMetadata(string Name, Type Type, IReadOnlySet<PaginateFilterOperator> Operators, PaginateBadge? Badge = null);

/// <summary>
///     The immutable, per-entity pagination contract: page-size and guard limits, the sortable, searchable and
///     filterable fields, the default sort and the tie-breaker. Every <c>Paginate*Async</c> entry point takes one, and
///     it reads back as <see cref="IPaginateConfig" /> metadata for OpenAPI or a <c>/meta</c> endpoint. The constructor
///     is internal: build it with <see cref="Create(System.Action{PaginateConfigBuilder{TEntity}})" />.
/// </summary>
/// <remarks>
///     Building freezes the field dictionaries and projects the metadata lists, so build it once — a static field or a
///     DI singleton (typically behind an <see cref="IPaginateConfigProvider{TEntity}" />) is the intended home.
/// </remarks>
public sealed class PaginateConfig<TEntity> : IPaginateConfig {

	private readonly FrozenDictionary<string, PaginateFilterField> _filterableFields;
	private readonly FrozenDictionary<string, PaginateSearchField<TEntity>> _searchableFields;
	private readonly IReadOnlyList<PaginateSearchField<TEntity>> _defaultSearchFields;

	private readonly FrozenDictionary<string, PaginateSortField> _sortableFields;

	internal PaginateConfig(
		PaginateLimits limits,
		IReadOnlyList<PaginateSort> defaultSortBy,
		FrozenDictionary<string, PaginateSortField> sortableFields,
		FrozenDictionary<string, PaginateSearchField<TEntity>> searchableFields,
		FrozenDictionary<string, PaginateFilterField> filterableFields,
		bool ignoreSearchByInQueryParam,
		LambdaExpression? tieBreakerSelector,
		PaginateSortDirection tieBreakerDirection
	) {

		DefaultLimit = limits.DefaultLimit;
		MaxLimit = limits.MaxLimit;
		MaxFilterValues = limits.MaxFilterValues;
		MaxFilterConditions = limits.MaxFilterConditions;
		MaxSortFields = limits.MaxSortFields;
		MaxSearchLength = limits.MaxSearchLength;
		MinSearchLength = limits.MinSearchLength;
		MaxOffset = limits.MaxOffset;
		UnlimitedMaxRows = limits.UnlimitedMaxRows;
		DefaultSortBy = defaultSortBy;
		_sortableFields = sortableFields;
		_searchableFields = searchableFields;
		_defaultSearchFields = searchableFields.Values.Where(field => field.Condition != false).ToArray();
		_filterableFields = filterableFields;
		IgnoreSearchByInQueryParam = ignoreSearchByInQueryParam;
		TieBreakerSelector = tieBreakerSelector;
		TieBreakerDirection = tieBreakerDirection;

		SortableFields = sortableFields.Values
			.Select(field => new PaginateFieldMetadata(field.Name, field.Type, field.Badge))
			.ToArray();

		SearchableFields = searchableFields.Values
			.Select(field => new PaginateFieldMetadata(field.Name, typeof(string), field.Badge))
			.ToArray();

		FilterableFields = filterableFields.Values
			.Select(field => new PaginateFilterFieldMetadata(field.Name, field.Type, field.Operators, field.Badge))
			.ToArray();

	}

	/// <inheritdoc />
	public int DefaultLimit { get; }

	/// <inheritdoc />
	public int MaxLimit { get; }

	/// <inheritdoc />
	public int MaxFilterValues { get; }

	/// <inheritdoc />
	public int MaxFilterConditions { get; }

	/// <inheritdoc />
	public int MaxSortFields { get; }

	/// <inheritdoc />
	public int MaxSearchLength { get; }

	/// <inheritdoc />
	public int MinSearchLength { get; }

	/// <inheritdoc />
	public int? MaxOffset { get; }

	/// <inheritdoc />
	public int? UnlimitedMaxRows { get; }

	/// <inheritdoc />
	public IReadOnlyList<PaginateSort> DefaultSortBy { get; }

	/// <inheritdoc />
	public IReadOnlyList<PaginateFieldMetadata> SortableFields { get; }

	/// <inheritdoc />
	public IReadOnlyList<PaginateFieldMetadata> SearchableFields { get; }

	/// <inheritdoc />
	public IReadOnlyList<PaginateFilterFieldMetadata> FilterableFields { get; }

	/// <inheritdoc />
	public bool IgnoreSearchByInQueryParam { get; }

	/// <summary>Optional unique key appended as the final ordering so offset paging is deterministic.</summary>
	internal LambdaExpression? TieBreakerSelector { get; }

	internal PaginateSortDirection TieBreakerDirection { get; }

	// A field disabled by When(false) is treated as if it were not configured, so a request targeting it is rejected
	// exactly like an unknown field — no information disclosure about the existence of admin-only fields.
	internal bool TryGetSortableField(string name, out PaginateSortField field) { return _sortableFields.TryGetValue(name, out field!) && field.Condition != false; }

	internal bool TryGetSearchableField(string name, out PaginateSearchField<TEntity> field) { return _searchableFields.TryGetValue(name, out field!) && field.Condition != false; }

	internal bool TryGetFilterableField(string name, out PaginateFilterField field) { return _filterableFields.TryGetValue(name, out field!) && field.Condition != false; }

	internal bool IsSortEnabled(string name) { return _sortableFields.TryGetValue(name, out var field) && field.Condition != false; }

	internal IReadOnlyList<PaginateSearchField<TEntity>> GetDefaultSearchFields() { return _defaultSearchFields; }

	/// <summary>
	///     Builds an immutable <see cref="PaginateConfig{TEntity}" /> for an entity using the fluent builder, falling
	///     back to <see cref="PaginateConfigDefaults.Shared" /> for anything the builder does not set.
	/// </summary>
	public static PaginateConfig<TEntity> Create(Action<PaginateConfigBuilder<TEntity>> configure) { return Create(PaginateConfigDefaults.Shared, configure); }

	/// <summary>
	///     Builds an immutable <see cref="PaginateConfig{TEntity}" /> against an explicit
	///     <paramref name="defaults" /> object, which is consulted for anything the builder does not set and itself
	///     takes precedence over <see cref="PaginateConfigDefaults.Shared" /> — naming the object at the call site is
	///     how a group of configurations shares limits without any of them being ambient.
	/// </summary>
	/// <param name="defaults">Limits and guards to fall back to; a builder call always wins over these.</param>
	/// <param name="configure">Declares the limits, guards and fields.</param>
	public static PaginateConfig<TEntity> Create(PaginateConfigDefaults defaults, Action<PaginateConfigBuilder<TEntity>> configure) {
		ArgumentNullException.ThrowIfNull(defaults);
		ArgumentNullException.ThrowIfNull(configure);

		var builder = new PaginateConfigBuilder<TEntity>();
		configure(builder);
		return builder.Build(defaults);
	}

}

/// <summary>
///     The fluent builder handed to the <see cref="PaginateConfig{TEntity}.Create(System.Action{PaginateConfigBuilder{TEntity}})" /> callback — every limit, guard and
///     sortable, searchable or filterable field is declared on it. <see cref="WithLimits" /> and
///     <see cref="WithTieBreaker" /> are the two required calls — <c>Build()</c> throws without either, unless a
///     <see cref="PaginateConfigDefaults" /> supplies the limits. It throws too when a
///     <see cref="DefaultSortBy" /> field is not also <c>Sortable</c>, or a field marked <see cref="When" />
///     carries no <see cref="ShowBadge" />. With the tie-breaker guaranteed there is always an ordering, so no
///     request can reach an unordered page.
/// </summary>
public sealed class PaginateConfigBuilder<TEntity> {

	private const int DefaultMaxFilterValues = 100;
	private const int DefaultMaxFilterConditions = 20;
	private const int DefaultMaxSortFields = 5;
	private const int DefaultMaxSearchLength = 256;
	private const int DefaultMinSearchLength = 1;
	private readonly List<PaginateSort> _defaultSortBy = [];
	private readonly Dictionary<string, PaginateFilterField> _filterableFields = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PaginateSearchField<TEntity>> _searchableFields = new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, PaginateSortField> _sortableFields = new(StringComparer.OrdinalIgnoreCase);

	private int? _defaultLimit;
	private bool _ignoreSearchByInQueryParam;
	// All unset rather than pre-seeded: "not configured here" is what lets the defaults object and
	// PaginateConfigDefaults.Shared be consulted before the constants above.
	private int? _maxFilterConditions;
	private int? _maxFilterValues;
	private int? _maxLimit;
	private int? _maxSortFields;
	private int? _maxSearchLength;
	private int? _minSearchLength;
	private int? _maxOffset;
	private int? _unlimitedMaxRows;
	private LambdaExpression? _tieBreakerSelector;
	private PaginateSortDirection _tieBreakerDirection = PaginateSortDirection.Asc;
	private IPaginateFieldTarget? _lastField;

	/// <summary>Sets the default and maximum page size. Required — <c>Build()</c> throws if limits are not configured.</summary>
	public PaginateConfigBuilder<TEntity> WithLimits(int defaultLimit, int maxLimit) {
		if (defaultLimit <= 0) throw new ArgumentOutOfRangeException(nameof(defaultLimit), "Default limit must be greater than zero.");
		if (maxLimit <= 0) throw new ArgumentOutOfRangeException(nameof(maxLimit), "Max limit must be greater than zero.");
		if (defaultLimit > maxLimit) throw new ArgumentException("Default limit must not be greater than max limit.", nameof(defaultLimit));

		_defaultLimit = defaultLimit;
		_maxLimit = maxLimit;
		return this;
	}

	/// <summary>
	///     Sets DoS guard limits: maximum values per filter, maximum total filter conditions, maximum sort fields,
	///     and maximum search-term length. An argument left out is not set here at all, so it falls through to the
	///     shared defaults and then to the engine's own value — naming one guard never resets the others.
	/// </summary>
	public PaginateConfigBuilder<TEntity> WithGuards(
		int? maxFilterValues = null,
		int? maxFilterConditions = null,
		int? maxSortFields = null,
		int? maxSearchLength = null
	) {
		if (maxFilterValues <= 0) throw new ArgumentOutOfRangeException(nameof(maxFilterValues), "Max filter values must be greater than zero.");
		if (maxFilterConditions <= 0) throw new ArgumentOutOfRangeException(nameof(maxFilterConditions), "Max filter conditions must be greater than zero.");
		if (maxSortFields <= 0) throw new ArgumentOutOfRangeException(nameof(maxSortFields), "Max sort fields must be greater than zero.");
		if (maxSearchLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxSearchLength), "Max search length must be greater than zero.");

		_maxFilterValues = maxFilterValues ?? _maxFilterValues;
		_maxFilterConditions = maxFilterConditions ?? _maxFilterConditions;
		_maxSortFields = maxSortFields ?? _maxSortFields;
		_maxSearchLength = maxSearchLength ?? _maxSearchLength;
		return this;
	}

	/// <summary>
	///     Sets the minimum length of a <c>search</c> term; a shorter one is rejected rather than run. The term is
	///     measured after trimming, so it counts what is actually searched for. Defaults to 1 — any non-blank term
	///     runs. Worth raising on a resource whose search spans several unindexed text columns, where a
	///     one-character term is the cheapest way to make the database read every row.
	/// </summary>
	public PaginateConfigBuilder<TEntity> WithMinSearchLength(int minSearchLength) {
		if (minSearchLength <= 0) throw new ArgumentOutOfRangeException(nameof(minSearchLength), "Min search length must be greater than zero.");

		_minSearchLength = minSearchLength;
		return this;
	}

	/// <summary>
	///     Caps how many rows a request may skip — <c>(page - 1) × limit</c>. A deeper page is rejected with a 400
	///     before anything is counted or fetched, because the check is arithmetic. Unset by default.
	/// </summary>
	/// <remarks>
	///     Deliberately a ceiling on the offset rather than on the page number: the offset is what the database
	///     pays for, and the page a given offset corresponds to moves with <c>limit</c>.
	/// </remarks>
	public PaginateConfigBuilder<TEntity> WithMaxOffset(int maxOffset) {
		if (maxOffset < 0) throw new ArgumentOutOfRangeException(nameof(maxOffset), "Max offset must not be negative.");

		_maxOffset = maxOffset;
		return this;
	}

	/// <summary>
	///     Opts this resource into <c>limit=-1</c>, which returns every matching row as one page.
	///     <paramref name="maxRows" /> is a mandatory ceiling: the engine fetches one row past it and answers 400
	///     rather than returning a set it was not promised could be held in memory. Without this call
	///     <c>limit=-1</c> stays a 400, as do <c>-2</c> and <c>0</c> with or without it.
	/// </summary>
	/// <remarks>
	///     There is no ceiling-free form, and the opt-in is per resource on purpose — it is a statement that
	///     <i>this</i> collection is bounded, which is not something a global setting could know. An unlimited
	///     request must ask for page 1; pages of an unbounded set are meaningless.
	///     A <paramref name="maxRows" /> of exactly <c>int.MaxValue</c> is accepted and means what it looks like: the
	///     fetch of <c>maxRows + 1</c> is clamped to <c>int.MaxValue</c>, so no result can exceed the ceiling and the
	///     400 can never be raised. Such a read is bounded only by the memory available to hold it.
	/// </remarks>
	public PaginateConfigBuilder<TEntity> AllowUnlimited(int maxRows) {
		if (maxRows <= 0) throw new ArgumentOutOfRangeException(nameof(maxRows), "Max rows must be greater than zero.");

		_unlimitedMaxRows = maxRows;
		return this;
	}

	/// <summary>Declares <paramref name="name" /> as sortable via <c>sortBy=name:ASC|DESC</c>.</summary>
	public PaginateConfigBuilder<TEntity> Sortable<TValue>(string name, Expression<Func<TEntity, TValue>> selector) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(selector);

		var field = new PaginateSortField(name, selector, typeof(TValue));
		_sortableFields[name] = field;
		_lastField = field;
		return this;
	}

	/// <summary>Adds a default sort applied when the request supplies no <c>sortBy</c>. The field must also be <c>Sortable</c>.</summary>
	public PaginateConfigBuilder<TEntity> DefaultSortBy(string field, PaginateSortDirection direction = PaginateSortDirection.Asc) {
		ArgumentException.ThrowIfNullOrWhiteSpace(field);

		_defaultSortBy.Add(new PaginateSort(field, direction));
		return this;
	}

	/// <summary>
	///     Configures a unique key (typically the primary key) appended as the final ordering on every query, so
	///     offset paging stays deterministic even when the primary sort is absent or non-unique. <b>Required</b>:
	///     <c>Build()</c> throws without it, because paging an ambiguously ordered set can return the same row on
	///     two pages and skip another.
	/// </summary>
	/// <remarks>
	///     Required outright rather than "a default sort or a tie-breaker": a <see cref="DefaultSortBy" /> field is
	///     filtered through <see cref="When" /> and this one is not, so the weaker rule would pass for a
	///     configuration whose only default is disabled for a caller and still leave nothing to order by.
	/// </remarks>
	public PaginateConfigBuilder<TEntity> WithTieBreaker<TValue>(Expression<Func<TEntity, TValue>> selector, PaginateSortDirection direction = PaginateSortDirection.Asc) {
		ArgumentNullException.ThrowIfNull(selector);

		_tieBreakerSelector = selector;
		_tieBreakerDirection = direction;
		return this;
	}

	/// <summary>When enabled, the <c>searchBy</c> query parameter is ignored and search always spans all searchable fields.</summary>
	public PaginateConfigBuilder<TEntity> IgnoreSearchByInQueryParam(bool ignore = true) {
		_ignoreSearchByInQueryParam = ignore;
		return this;
	}

	/// <summary>Declares a string field included in free-text <c>search</c> (and addressable via <c>searchBy</c>).</summary>
	public PaginateConfigBuilder<TEntity> Searchable(string name, Expression<Func<TEntity, string?>> selector) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(selector);

		var field = new PaginateSearchField<TEntity>(name, selector);
		_searchableFields[name] = field;
		_lastField = field;
		return this;
	}

	/// <summary>
	///     Declares a scalar field as filterable via <c>filter.&lt;name&gt;=$op:value</c>, whitelisting every operator
	///     the engine can build for <typeparamref name="TValue" /> — see <see cref="PaginateFilterOperators.For{TValue}" />
	///     for the derivation and its limits. Throws when the type has no derivation; such a field takes the overload
	///     with an explicit operator list.
	/// </summary>
	public PaginateConfigBuilder<TEntity> Filterable<TValue>(string name, Expression<Func<TEntity, TValue>> selector) { return Filterable(name, selector, PaginateFilterOperators.For<TValue>()); }

	/// <summary>
	///     Declares a collection/navigation field as filterable, whitelisting every operator the engine can build for
	///     <typeparamref name="TValue" /> — the <see cref="PaginateFilterOperators.For{TValue}" /> derivation, exactly as
	///     the scalar shorthand above. Throws when the type has no derivation.
	/// </summary>
	public PaginateConfigBuilder<TEntity> FilterableMany<TElement, TValue>(
		string name,
		Expression<Func<TEntity, IEnumerable<TElement>>> collectionSelector,
		Expression<Func<TElement, TValue>> valueSelector
	) { return FilterableMany(name, collectionSelector, valueSelector, PaginateFilterOperators.For<TValue>()); }

	/// <summary>
	///     Declares a scalar field as filterable via <c>filter.&lt;name&gt;=$op:value</c>, restricted to the supplied
	///     <paramref name="operators" /> (at least one is required).
	/// </summary>
	public PaginateConfigBuilder<TEntity> Filterable<TValue>(
		string name,
		Expression<Func<TEntity, TValue>> selector,
		params PaginateFilterOperator[] operators
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(selector);

		var field = new PaginateScalarFilterField<TEntity, TValue>(name, selector, typeof(TValue), BuildOperatorSet(operators));
		_filterableFields[name] = field;
		_lastField = field;
		return this;
	}

	/// <summary>
	///     Declares a collection/navigation field as filterable: the operator is matched against the value selected
	///     from any element (translated to an <c>Any(...)</c> predicate), e.g. filter orders by any line's product id.
	/// </summary>
	public PaginateConfigBuilder<TEntity> FilterableMany<TElement, TValue>(
		string name,
		Expression<Func<TEntity, IEnumerable<TElement>>> collectionSelector,
		Expression<Func<TElement, TValue>> valueSelector,
		params PaginateFilterOperator[] operators
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(collectionSelector);
		ArgumentNullException.ThrowIfNull(valueSelector);

		var field = new PaginateCollectionFilterField<TEntity, TElement>(name, collectionSelector, valueSelector, typeof(TValue), BuildOperatorSet(operators));
		_filterableFields[name] = field;
		_lastField = field;
		return this;
	}

	/// <summary>
	///     Attaches a <see cref="PaginateBadge" /> to the field declared immediately before this call — e.g.
	///     <c>.Sortable("slug", a =&gt; a.Slug).ShowBadge("Public", "language-public")</c>. The badge is surfaced in the
	///     generated OpenAPI metadata and rendered as a chip by the API reference UI. <paramref name="cssClass" /> is an
	///     optional CSS class you then color via the renderer's custom CSS; when set it <b>must</b> start with
	///     <c>language-</c> — the only class prefix the API reference sanitizer keeps in a description — otherwise this
	///     throws. Omit it for a neutral chip. Throws if called before any field.
	/// </summary>
	public PaginateConfigBuilder<TEntity> ShowBadge(string name, string? cssClass = null) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (_lastField is null) {
			throw new InvalidOperationException("ShowBadge must be called immediately after a Sortable, Searchable, or Filterable field.");
		}

		if (cssClass is not null && !cssClass.StartsWith("language-", StringComparison.Ordinal)) {
			throw new ArgumentException("Badge cssClass must start with \"language-\" — other classes are stripped by the API reference sanitizer.", nameof(cssClass));
		}

		_lastField.Badge = new PaginateBadge(name, cssClass);
		return this;
	}

	/// <summary>
	///     Marks the field declared immediately before this call as conditional: it stays documented in OpenAPI (the
	///     widest surface) but at query time is treated as not configured whenever <paramref name="condition" /> is
	///     <c>false</c>, so a request targeting it gets a 400. Must be paired with <see cref="ShowBadge" /> so the
	///     condition is visible in the docs — <c>Build()</c> throws otherwise. The consumer evaluates the boolean itself
	///     (e.g. from the current user's role), keeping the library auth-agnostic.
	/// </summary>
	public PaginateConfigBuilder<TEntity> When(bool condition) {
		if (_lastField is null) {
			throw new InvalidOperationException("When must be called immediately after a Sortable, Searchable, or Filterable field.");
		}

		_lastField.Condition = condition;
		return this;
	}

	internal PaginateConfig<TEntity> Build() { return Build(PaginateConfigDefaults.Shared); }

	internal PaginateConfig<TEntity> Build(PaginateConfigDefaults defaults) {

		// Outward from the most specific: this builder, then the defaults object handed to Create, then the
		// process-wide Shared one, then the engine's constant. Read once, here -- a config does not observe a
		// later assignment to Shared, which is why that property documents itself as a startup-time setting.
		var shared = PaginateConfigDefaults.Shared;
		int? Resolve(int? own, Func<PaginateConfigDefaults, int?> read) { return own ?? read(defaults) ?? read(shared); }

		if (Resolve(_defaultLimit, d => d.DefaultLimit) is not { } defaultLimit
			|| Resolve(_maxLimit, d => d.MaxLimit) is not { } maxLimit) {
			throw new InvalidOperationException("Pagination limits must be configured explicitly via WithLimits(defaultLimit, maxLimit).");
		}

		// The builder methods reject a nonsense value at the call site, but a defaults object is a plain record
		// whose init accessors cannot, so whatever survives resolution is checked once here. Skipping it would
		// let `new PaginateConfigDefaults { MaxOffset = -1 }` refuse every request including page 1.
		// Ahead of the pairing check below, so a zero maximum is reported as itself rather than as
		// "default 5 exceeds max 0", which names the wrong value.
		Positive(defaultLimit, nameof(PaginateConfigDefaults.DefaultLimit));
		Positive(maxLimit, nameof(PaginateConfigDefaults.MaxLimit));

		// Deferred to here rather than checked in WithLimits, because the two halves may now arrive from
		// different places -- a shared MaxLimit under a per-config DefaultLimit is a legitimate combination, and
		// an incompatible one is still a configuration error rather than a request error.
		if (defaultLimit > maxLimit) {
			throw new InvalidOperationException($"Default limit {defaultLimit} must not be greater than max limit {maxLimit}.");
		}

		var limits = new PaginateLimits(
			defaultLimit,
			maxLimit,
			Resolve(_maxFilterValues, d => d.MaxFilterValues) ?? DefaultMaxFilterValues,
			Resolve(_maxFilterConditions, d => d.MaxFilterConditions) ?? DefaultMaxFilterConditions,
			Resolve(_maxSortFields, d => d.MaxSortFields) ?? DefaultMaxSortFields,
			Resolve(_maxSearchLength, d => d.MaxSearchLength) ?? DefaultMaxSearchLength,
			Resolve(_minSearchLength, d => d.MinSearchLength) ?? DefaultMinSearchLength,
			Resolve(_maxOffset, d => d.MaxOffset),
			_unlimitedMaxRows
		);

		Positive(limits.MaxFilterValues, nameof(PaginateConfigDefaults.MaxFilterValues));
		Positive(limits.MaxFilterConditions, nameof(PaginateConfigDefaults.MaxFilterConditions));
		Positive(limits.MaxSortFields, nameof(PaginateConfigDefaults.MaxSortFields));
		Positive(limits.MaxSearchLength, nameof(PaginateConfigDefaults.MaxSearchLength));
		Positive(limits.MinSearchLength, nameof(PaginateConfigDefaults.MinSearchLength));

		if (limits.MaxOffset < 0) {
			throw new InvalidOperationException($"{nameof(PaginateConfigDefaults.MaxOffset)} must not be negative.");
		}

		if (limits.MinSearchLength > limits.MaxSearchLength) {
			throw new InvalidOperationException($"Min search length {limits.MinSearchLength} must not be greater than max search length {limits.MaxSearchLength}.");
		}

		foreach (var sort in _defaultSortBy.Where(sort => !_sortableFields.ContainsKey(sort.Field))) {
			throw new InvalidOperationException($"Default sort field '{sort.Field}' is not sortable.");
		}

		// Required outright, rather than "a default sort or a tie-breaker". The weaker rule does not hold: a
		// DefaultSortBy field is filtered through When(...), so a config whose only default is disabled for this
		// caller would pass the build check and still have nothing to order by at request time. One rule that is
		// always true costs one line on a config that already has to call WithLimits.
		if (_tieBreakerSelector is null) {
			throw new InvalidOperationException(
				"A pagination configuration requires WithTieBreaker(...): offset paging over a non-unique order can return the same row on two pages and skip another. Pass the entity's primary key, e.g. WithTieBreaker(x => x.Id)."
			);
		}

		var allFields = _sortableFields.Values.Cast<IPaginateFieldTarget>().Concat(_searchableFields.Values).Concat(_filterableFields.Values);
		if (allFields.Any(field => field.Condition.HasValue && field.Badge is null)) {
			throw new InvalidOperationException("A field configured with .When(...) must also declare .ShowBadge(...) so the condition is documented in the OpenAPI output.");
		}

		var defaultSortBy = _defaultSortBy.Count == 0 ? [] : _defaultSortBy.ToArray();

		return new PaginateConfig<TEntity>(
			limits,
			defaultSortBy,
			_sortableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_searchableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_filterableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_ignoreSearchByInQueryParam,
			_tieBreakerSelector,
			_tieBreakerDirection
		);

	}

	private static void Positive(int value, string name) {
		if (value <= 0) throw new InvalidOperationException($"{name} must be greater than zero.");
	}

	private static HashSet<PaginateFilterOperator> BuildOperatorSet(PaginateFilterOperator[] operators) {
		return operators.Length > 0 ? operators.ToHashSet() : throw new ArgumentException("At least one filter operator must be configured.", nameof(operators));
	}

}

/// <summary>The resolved numeric limits of one configuration, gathered so the config constructor is not nine adjacent ints.</summary>
internal readonly record struct PaginateLimits(
	int DefaultLimit,
	int MaxLimit,
	int MaxFilterValues,
	int MaxFilterConditions,
	int MaxSortFields,
	int MaxSearchLength,
	int MinSearchLength,
	int? MaxOffset,
	int? UnlimitedMaxRows
);
