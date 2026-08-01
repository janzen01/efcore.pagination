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
	///     Sorts applied, in declaration order, when the request sends no <c>sortBy</c> — empty when none was declared.
	///     A request that does send <c>sortBy</c> replaces these entirely; they never merge. Each field must also be
	///     declared sortable or <see cref="PaginateConfig{TEntity}.Create" /> throws, and an entry disabled by
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
///     <see cref="PaginateConfig{TEntity}.Create" /> throws otherwise — and one disabled by <c>When(false)</c> is
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
///     is internal: build it with <see cref="Create" />.
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
		int defaultLimit,
		int maxLimit,
		int maxFilterValues,
		int maxFilterConditions,
		int maxSortFields,
		int maxSearchLength,
		IReadOnlyList<PaginateSort> defaultSortBy,
		FrozenDictionary<string, PaginateSortField> sortableFields,
		FrozenDictionary<string, PaginateSearchField<TEntity>> searchableFields,
		FrozenDictionary<string, PaginateFilterField> filterableFields,
		bool ignoreSearchByInQueryParam,
		LambdaExpression? tieBreakerSelector,
		PaginateSortDirection tieBreakerDirection
	) {

		DefaultLimit = defaultLimit;
		MaxLimit = maxLimit;
		MaxFilterValues = maxFilterValues;
		MaxFilterConditions = maxFilterConditions;
		MaxSortFields = maxSortFields;
		MaxSearchLength = maxSearchLength;
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

	/// <summary>Builds an immutable <see cref="PaginateConfig{TEntity}" /> for an entity using the fluent builder.</summary>
	public static PaginateConfig<TEntity> Create(Action<PaginateConfigBuilder<TEntity>> configure) {
		ArgumentNullException.ThrowIfNull(configure);

		var builder = new PaginateConfigBuilder<TEntity>();
		configure(builder);
		return builder.Build();
	}

}

/// <summary>
///     The fluent builder handed to the <see cref="PaginateConfig{TEntity}.Create" /> callback — every limit, guard and
///     sortable, searchable or filterable field is declared on it. <see cref="WithLimits" /> is the one required
///     call — <c>Build()</c> throws without it, and throws too when a <see cref="DefaultSortBy" /> field is not also
///     <c>Sortable</c>, or a field marked <see cref="When" /> carries no <see cref="ShowBadge" />. An order is still
///     required at query time — from <c>sortBy</c>, <see cref="DefaultSortBy" /> or <see cref="WithTieBreaker" /> — or
///     every request is rejected.
/// </summary>
public sealed class PaginateConfigBuilder<TEntity> {

	private const int DefaultMaxFilterValues = 100;
	private const int DefaultMaxFilterConditions = 20;
	private const int DefaultMaxSortFields = 5;
	private const int DefaultMaxSearchLength = 256;
	private readonly List<PaginateSort> _defaultSortBy = [];
	private readonly Dictionary<string, PaginateFilterField> _filterableFields = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PaginateSearchField<TEntity>> _searchableFields = new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, PaginateSortField> _sortableFields = new(StringComparer.OrdinalIgnoreCase);

	private int? _defaultLimit;
	private bool _ignoreSearchByInQueryParam;
	private int _maxFilterConditions = DefaultMaxFilterConditions;
	private int _maxFilterValues = DefaultMaxFilterValues;
	private int? _maxLimit;
	private int _maxSortFields = DefaultMaxSortFields;
	private int _maxSearchLength = DefaultMaxSearchLength;
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
	///     and maximum search-term length. Sensible defaults apply when not configured.
	/// </summary>
	public PaginateConfigBuilder<TEntity> WithGuards(
		int maxFilterValues = DefaultMaxFilterValues,
		int maxFilterConditions = DefaultMaxFilterConditions,
		int maxSortFields = DefaultMaxSortFields,
		int maxSearchLength = DefaultMaxSearchLength
	) {
		if (maxFilterValues <= 0) throw new ArgumentOutOfRangeException(nameof(maxFilterValues), "Max filter values must be greater than zero.");
		if (maxFilterConditions <= 0) throw new ArgumentOutOfRangeException(nameof(maxFilterConditions), "Max filter conditions must be greater than zero.");
		if (maxSortFields <= 0) throw new ArgumentOutOfRangeException(nameof(maxSortFields), "Max sort fields must be greater than zero.");
		if (maxSearchLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxSearchLength), "Max search length must be greater than zero.");

		_maxFilterValues = maxFilterValues;
		_maxFilterConditions = maxFilterConditions;
		_maxSortFields = maxSortFields;
		_maxSearchLength = maxSearchLength;
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
	///     offset paging stays deterministic even when the primary sort is absent or non-unique. Strongly recommended:
	///     paging an unordered or ambiguously ordered set yields non-deterministic page boundaries.
	/// </summary>
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

	internal PaginateConfig<TEntity> Build() {

		if (_defaultLimit is not { } defaultLimit || _maxLimit is not { } maxLimit) {
			throw new InvalidOperationException("Pagination limits must be configured explicitly via WithLimits(defaultLimit, maxLimit).");
		}

		foreach (var sort in _defaultSortBy.Where(sort => !_sortableFields.ContainsKey(sort.Field))) {
			throw new InvalidOperationException($"Default sort field '{sort.Field}' is not sortable.");
		}

		var allFields = _sortableFields.Values.Cast<IPaginateFieldTarget>().Concat(_searchableFields.Values).Concat(_filterableFields.Values);
		if (allFields.Any(field => field.Condition.HasValue && field.Badge is null)) {
			throw new InvalidOperationException("A field configured with .When(...) must also declare .ShowBadge(...) so the condition is documented in the OpenAPI output.");
		}

		var defaultSortBy = _defaultSortBy.Count == 0 ? [] : _defaultSortBy.ToArray();

		return new PaginateConfig<TEntity>(
			defaultLimit,
			maxLimit,
			_maxFilterValues,
			_maxFilterConditions,
			_maxSortFields,
			_maxSearchLength,
			defaultSortBy,
			_sortableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_searchableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_filterableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_ignoreSearchByInQueryParam,
			_tieBreakerSelector,
			_tieBreakerDirection
		);

	}

	private static HashSet<PaginateFilterOperator> BuildOperatorSet(PaginateFilterOperator[] operators) {
		return operators.Length > 0 ? operators.ToHashSet() : throw new ArgumentException("At least one filter operator must be configured.", nameof(operators));
	}

}
