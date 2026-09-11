namespace Janzen.Pagination.EntityFrameworkCore.Model;

/// <summary>
///     Machine-readable cause of a <see cref="PaginateQueryException" />, carried by
///     <see cref="PaginateQueryException.Code" /> and emitted as the <c>code</c> member of the 400 Problem Details
///     response. It exists so a client can branch on the cause without matching the <c>detail</c> prose, which pins
///     the wording permanently and cannot be localised.
/// </summary>
/// <remarks>
///     The members group by the stage the engine rejects at, in the order it works in: paging, filters, values,
///     search, sorting. One member can stand for several messages — the message says which field and which
///     ceiling, the code says what kind of thing went wrong. New members are added as the engine grows new
///     rejections, so treat an unrecognised value the way you would treat <see cref="Unspecified" />.
/// </remarks>
public enum PaginateQueryError {

	/// <summary>No cause was recorded — an exception constructed outside the engine, or one the engine has not classified.</summary>
	Unspecified = 0,

	/// <summary><c>page</c> is not a positive integer.</summary>
	PageOutOfRange,

	/// <summary><c>limit</c> is not a positive integer, or is above the configured maximum.</summary>
	LimitOutOfRange,

	/// <summary>The requested page would skip more rows than <c>WithMaxOffset</c> allows.</summary>
	MaxOffsetExceeded,

	/// <summary><c>limit=-1</c> was sent with a page other than the first; an unlimited read is a single page.</summary>
	UnlimitedReadRequiresFirstPage,

	/// <summary>An unlimited read matched more rows than the ceiling passed to <c>AllowUnlimited</c>.</summary>
	UnlimitedReadTooLarge,

	/// <summary>The request filters on a field the configuration does not declare, or disabled for this caller.</summary>
	FilterFieldNotConfigured,

	/// <summary>The request carries more filter conditions, across every field, than <c>MaxFilterConditions</c> allows.</summary>
	TooManyFilterConditions,

	/// <summary>A filter criterion is not in the <c>$operator:value</c> form — empty, unterminated, or carrying a value where none is taken.</summary>
	FilterCriterionMalformed,

	/// <summary>A filter criterion names an operator token the grammar has no member for.</summary>
	FilterOperatorUnknown,

	/// <summary>A real operator that this field's allow-list does not grant.</summary>
	FilterOperatorNotAllowed,

	/// <summary>An operator member with no implementation behind it — an engine-internal guard, not reachable from a request.</summary>
	FilterOperatorUnsupported,

	/// <summary>The operator does not apply to this field's type: a pattern operator off a string, or a comparison off an unordered type.</summary>
	FilterOperatorTypeMismatch,

	/// <summary>The operator was given the wrong number of values — an empty list, or a <c>$btw</c> without exactly two bounds.</summary>
	FilterValueCountInvalid,

	/// <summary>One criterion carries more values than <c>MaxFilterValues</c> allows.</summary>
	TooManyFilterValues,

	/// <summary>An empty value was sent for a target that cannot be empty.</summary>
	ValueEmpty,

	/// <summary>The text after the operator cannot become the field's type — unparseable, out of range, or not a defined enum member.</summary>
	ValueInvalid,

	/// <summary>The field's type has no registered parser, so no value can be converted for it.</summary>
	ValueTypeNotSupported,

	/// <summary><c>search</c> was sent to a resource that declares no searchable field.</summary>
	SearchNotConfigured,

	/// <summary><c>searchBy</c> names a field that is not searchable.</summary>
	SearchFieldNotConfigured,

	/// <summary>The same <c>searchBy</c> field was sent more than once.</summary>
	DuplicateSearchField,

	/// <summary>Two spellings of one filter field resolve to the same field, so one criterion would be lost.</summary>
	DuplicateFilterField,

	/// <summary>The search term is shorter than <c>WithMinSearchLength</c> allows, measured after trimming.</summary>
	SearchTermTooShort,

	/// <summary>The search term is longer than <c>MaxSearchLength</c> allows.</summary>
	SearchTermTooLong,

	/// <summary>A <c>sortBy</c> value is not in the <c>field:ASC</c> / <c>field:DESC</c> form.</summary>
	SortValueMalformed,

	/// <summary>A <c>sortBy</c> value names a direction other than <c>ASC</c> or <c>DESC</c>.</summary>
	SortDirectionUnknown,

	/// <summary><c>sortBy</c> names a field the configuration does not declare sortable.</summary>
	SortFieldNotConfigured,

	/// <summary>One <c>sortBy</c> field is named more than once, so one of the orderings would be discarded.</summary>
	DuplicateSortField,

	/// <summary>The request carries more <c>sortBy</c> fields than <c>MaxSortFields</c> allows.</summary>
	TooManySortFields

}
