namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>Sort direction. In the query string: <c>field:ASC</c> or <c>field:DESC</c>.</summary>
public enum PaginateSortDirection {

	/// <summary>Ascending.</summary>
	Asc,

	/// <summary>Descending.</summary>
	Desc

}

/// <summary>
///     Filter operators. In the query string a filter is <c>filter.&lt;field&gt;={$not:}$op:value</c>, e.g.
///     <c>filter.status=$eq:active</c> or <c>filter.age=$btw:18,65</c>.
/// </summary>
public enum PaginateFilterOperator {

	/// <summary>Equals (<c>$eq</c>).</summary>
	Eq,

	/// <summary>Value is in a comma-separated list (<c>$in</c>), e.g. <c>$in:1,2,3</c>.</summary>
	In,

	/// <summary>Is null (<c>$null</c>).</summary>
	Null,

	/// <summary>Case-insensitive pattern match (<c>$ilike</c>); native ILIKE with the PostgreSql package, otherwise portable LIKE.</summary>
	ILike,

	/// <summary>Starts with (<c>$sw</c>).</summary>
	StartsWith,

	/// <summary>Contains a substring (string fields) or contains a value (collection fields) (<c>$contains</c>).</summary>
	Contains,

	/// <summary>Less than (<c>$lt</c>).</summary>
	LessThan,

	/// <summary>Less than or equal (<c>$lte</c>).</summary>
	LessThanOrEqual,

	/// <summary>Greater than (<c>$gt</c>).</summary>
	GreaterThan,

	/// <summary>Greater than or equal (<c>$gte</c>).</summary>
	GreaterThanOrEqual,

	/// <summary>Between two comma-separated bounds, inclusive (<c>$btw</c>), e.g. <c>$btw:10,20</c>.</summary>
	Between

}
