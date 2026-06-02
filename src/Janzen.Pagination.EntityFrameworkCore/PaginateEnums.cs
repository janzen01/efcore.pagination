namespace Janzen.Pagination.EntityFrameworkCore;

public enum PaginateSortDirection {

	Asc,
	Desc

}

public enum PaginateFilterOperator {

	Eq,
	In,
	Null,
	ILike,
	StartsWith,
	Contains,
	LessThan,
	LessThanOrEqual,
	GreaterThan,
	GreaterThanOrEqual,
	Between

}
