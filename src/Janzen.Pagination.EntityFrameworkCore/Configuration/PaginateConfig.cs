using Janzen.Pagination.EntityFrameworkCore.Engine;
using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Collections.Frozen;
using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore.Configuration;

public interface IPaginateConfig {

	int DefaultLimit { get; }

	int MaxLimit { get; }

	int MaxFilterValues { get; }

	int MaxFilterConditions { get; }

	int MaxSortFields { get; }

	int MaxSearchLength { get; }

	IReadOnlyList<PaginateSort> DefaultSortBy { get; }

	IReadOnlyList<PaginateFieldMetadata> SortableFields { get; }

	IReadOnlyList<PaginateFieldMetadata> SearchableFields { get; }

	IReadOnlyList<PaginateFilterFieldMetadata> FilterableFields { get; }

	bool IgnoreSearchByInQueryParam { get; }

}

public interface IPaginateConfigProvider {

	IPaginateConfig GetConfig();

}

public interface IPaginateConfigProvider<TEntity> : IPaginateConfigProvider {

	new PaginateConfig<TEntity> GetConfig();

	/// <summary>
	///     Fulfils the non-generic <see cref="IPaginateConfigProvider.GetConfig" /> by delegating to the typed overload,
	///     so implementers only write the typed <c>GetConfig()</c> — the base member is provided for free. The <c>new</c>
	///     typed member hides the base one in this scope, so this delegates to the implementer's method, not itself.
	/// </summary>
	IPaginateConfig IPaginateConfigProvider.GetConfig() => GetConfig();

}

public sealed record PaginateSort(string Field, PaginateSortDirection Direction);

/// <summary>
///     An optional presentation badge attached to a field: a <paramref name="Name" /> label and an optional
///     <paramref name="CssClass" />. Surfaced in the OpenAPI metadata and rendered as a chip by the API reference UI;
///     the class is how you color it, via the renderer's custom CSS. When set it must start with <c>language-</c>
///     (see <see cref="PaginateConfigBuilder{TEntity}.ShowBadge" />).
/// </summary>
public sealed record PaginateBadge(string Name, string? CssClass);

public sealed record PaginateFieldMetadata(string Name, Type Type, PaginateBadge? Badge = null);

public sealed record PaginateFilterFieldMetadata(string Name, Type Type, IReadOnlySet<PaginateFilterOperator> Operators, PaginateBadge? Badge = null);

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

	public int DefaultLimit { get; }

	public int MaxLimit { get; }

	public int MaxFilterValues { get; }

	public int MaxFilterConditions { get; }

	public int MaxSortFields { get; }

	public int MaxSearchLength { get; }

	public IReadOnlyList<PaginateSort> DefaultSortBy { get; }

	public IReadOnlyList<PaginateFieldMetadata> SortableFields { get; }

	public IReadOnlyList<PaginateFieldMetadata> SearchableFields { get; }

	public IReadOnlyList<PaginateFilterFieldMetadata> FilterableFields { get; }

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
