using System.Collections.Frozen;
using System.Linq.Expressions;

namespace Janzen.Pagination.EntityFrameworkCore;

public interface IPaginateConfig {

	int DefaultLimit { get; }

	int MaxLimit { get; }

	int MaxFilterValues { get; }

	int MaxFilterConditions { get; }

	int MaxSortFields { get; }

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

}

public sealed record PaginateSort(string Field, PaginateSortDirection Direction);

public sealed record PaginateFieldMetadata(string Name, Type Type);

public sealed record PaginateFilterFieldMetadata(string Name, Type Type, IReadOnlySet<PaginateFilterOperator> Operators);

public sealed class PaginateConfig<TEntity> : IPaginateConfig {

	private readonly FrozenDictionary<string, PaginateFilterField> _filterableFields;
	private readonly FrozenDictionary<string, PaginateSearchField<TEntity>> _searchableFields;

	private readonly FrozenDictionary<string, PaginateSortField> _sortableFields;

	internal PaginateConfig(
		int defaultLimit,
		int maxLimit,
		int maxFilterValues,
		int maxFilterConditions,
		int maxSortFields,
		IReadOnlyList<PaginateSort> defaultSortBy,
		FrozenDictionary<string, PaginateSortField> sortableFields,
		FrozenDictionary<string, PaginateSearchField<TEntity>> searchableFields,
		FrozenDictionary<string, PaginateFilterField> filterableFields,
		bool ignoreSearchByInQueryParam
	) {

		DefaultLimit = defaultLimit;
		MaxLimit = maxLimit;
		MaxFilterValues = maxFilterValues;
		MaxFilterConditions = maxFilterConditions;
		MaxSortFields = maxSortFields;
		DefaultSortBy = defaultSortBy;
		_sortableFields = sortableFields;
		_searchableFields = searchableFields;
		_filterableFields = filterableFields;
		IgnoreSearchByInQueryParam = ignoreSearchByInQueryParam;

		SortableFields = sortableFields.Values
			.Select(field => new PaginateFieldMetadata(field.Name, field.Type))
			.ToArray();

		SearchableFields = searchableFields.Values
			.Select(field => new PaginateFieldMetadata(field.Name, typeof(string)))
			.ToArray();

		FilterableFields = filterableFields.Values
			.Select(field => new PaginateFilterFieldMetadata(field.Name, field.Type, field.Operators))
			.ToArray();

	}

	public int DefaultLimit { get; }

	public int MaxLimit { get; }

	public int MaxFilterValues { get; }

	public int MaxFilterConditions { get; }

	public int MaxSortFields { get; }

	public IReadOnlyList<PaginateSort> DefaultSortBy { get; }

	public IReadOnlyList<PaginateFieldMetadata> SortableFields { get; }

	public IReadOnlyList<PaginateFieldMetadata> SearchableFields { get; }

	public IReadOnlyList<PaginateFilterFieldMetadata> FilterableFields { get; }

	public bool IgnoreSearchByInQueryParam { get; }

	internal bool TryGetSortableField(string name, out PaginateSortField field) { return _sortableFields.TryGetValue(name, out field!); }

	internal bool TryGetSearchableField(string name, out PaginateSearchField<TEntity> field) { return _searchableFields.TryGetValue(name, out field!); }

	internal bool TryGetFilterableField(string name, out PaginateFilterField field) { return _filterableFields.TryGetValue(name, out field!); }

	internal IReadOnlyList<PaginateSearchField<TEntity>> GetDefaultSearchFields() { return _searchableFields.Values.ToArray(); }

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

	public PaginateConfigBuilder<TEntity> WithLimits(int defaultLimit, int maxLimit) {
		if (defaultLimit <= 0) throw new ArgumentOutOfRangeException(nameof(defaultLimit), "Default limit must be greater than zero.");
		if (maxLimit <= 0) throw new ArgumentOutOfRangeException(nameof(maxLimit), "Max limit must be greater than zero.");
		if (defaultLimit > maxLimit) throw new ArgumentException("Default limit must not be greater than max limit.", nameof(defaultLimit));

		_defaultLimit = defaultLimit;
		_maxLimit = maxLimit;
		return this;
	}

	public PaginateConfigBuilder<TEntity> WithGuards(
		int maxFilterValues = DefaultMaxFilterValues,
		int maxFilterConditions = DefaultMaxFilterConditions,
		int maxSortFields = DefaultMaxSortFields
	) {
		if (maxFilterValues <= 0) throw new ArgumentOutOfRangeException(nameof(maxFilterValues), "Max filter values must be greater than zero.");
		if (maxFilterConditions <= 0) throw new ArgumentOutOfRangeException(nameof(maxFilterConditions), "Max filter conditions must be greater than zero.");
		if (maxSortFields <= 0) throw new ArgumentOutOfRangeException(nameof(maxSortFields), "Max sort fields must be greater than zero.");

		_maxFilterValues = maxFilterValues;
		_maxFilterConditions = maxFilterConditions;
		_maxSortFields = maxSortFields;
		return this;
	}

	public PaginateConfigBuilder<TEntity> Sortable<TValue>(string name, Expression<Func<TEntity, TValue>> selector) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(selector);

		_sortableFields[name] = new PaginateSortField(name, selector, typeof(TValue));
		return this;
	}

	public PaginateConfigBuilder<TEntity> DefaultSortBy(string field, PaginateSortDirection direction = PaginateSortDirection.Asc) {
		ArgumentException.ThrowIfNullOrWhiteSpace(field);

		_defaultSortBy.Add(new PaginateSort(field, direction));
		return this;
	}

	public PaginateConfigBuilder<TEntity> IgnoreSearchByInQueryParam(bool ignore = true) {
		_ignoreSearchByInQueryParam = ignore;
		return this;
	}

	public PaginateConfigBuilder<TEntity> Searchable(string name, Expression<Func<TEntity, string?>> selector) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(selector);

		_searchableFields[name] = new PaginateSearchField<TEntity>(name, selector);
		return this;
	}

	public PaginateConfigBuilder<TEntity> Filterable<TValue>(
		string name,
		Expression<Func<TEntity, TValue>> selector,
		params PaginateFilterOperator[] operators
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(selector);

		_filterableFields[name] = new PaginateScalarFilterField<TEntity, TValue>(name, selector, typeof(TValue), BuildOperatorSet(operators));
		return this;
	}

	public PaginateConfigBuilder<TEntity> FilterableMany<TElement, TValue>(
		string name,
		Expression<Func<TEntity, IEnumerable<TElement>>> collectionSelector,
		Expression<Func<TElement, TValue>> valueSelector,
		params PaginateFilterOperator[] operators
	) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(collectionSelector);
		ArgumentNullException.ThrowIfNull(valueSelector);

		_filterableFields[name] = new PaginateCollectionFilterField<TEntity, TElement>(name, collectionSelector, valueSelector, typeof(TValue), BuildOperatorSet(operators));
		return this;
	}

	internal PaginateConfig<TEntity> Build() {

		if (_defaultLimit is not { } defaultLimit || _maxLimit is not { } maxLimit) {
			throw new InvalidOperationException("Pagination limits must be configured explicitly via WithLimits(defaultLimit, maxLimit).");
		}

		foreach (var sort in _defaultSortBy.Where(sort => !_sortableFields.ContainsKey(sort.Field))) {
			throw new InvalidOperationException($"Default sort field '{sort.Field}' is not sortable.");
		}

		var defaultSortBy = _defaultSortBy.Count == 0 ? [] : _defaultSortBy.ToArray();

		return new PaginateConfig<TEntity>(
			defaultLimit,
			maxLimit,
			_maxFilterValues,
			_maxFilterConditions,
			_maxSortFields,
			defaultSortBy,
			_sortableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_searchableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_filterableFields.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
			_ignoreSearchByInQueryParam
		);

	}

	private static HashSet<PaginateFilterOperator> BuildOperatorSet(PaginateFilterOperator[] operators) {
		if (operators.Length == 0) throw new ArgumentException("At least one filter operator must be configured.", nameof(operators));

		return operators.ToHashSet();
	}

}
