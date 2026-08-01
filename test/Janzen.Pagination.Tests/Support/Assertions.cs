using Janzen.Pagination.EntityFrameworkCore.Model;

namespace Janzen.Pagination.Tests.Support;

public static class Assertions {

	/// <summary>
	///     Asserts the page holds exactly these ids, in this order. The canonical config always ends its ordering
	///     with the id tie-breaker, so the order is defined even when the primary sort ties.
	/// </summary>
	public static void HasIds(PaginatedResponse<ProductDto> page, params int[] expected) {
		Assert.Equal(expected, page.Items.Select(item => item.Id).ToArray());
	}

	/// <summary>Asserts the page holds these ids in any order — for filters where the ordering is not the point.</summary>
	public static void HasIdsInAnyOrder(PaginatedResponse<ProductDto> page, params int[] expected) {
		Assert.Equal([.. expected.Order()], page.Items.Select(item => item.Id).Order().ToArray());
	}

	/// <summary>Runs the query and returns the <see cref="PaginateQueryException" /> message it rejects with.</summary>
	public static async Task<string> RejectsAsync(Func<Task> act) {
		var exception = await Assert.ThrowsAsync<PaginateQueryException>(act);
		return exception.Message;
	}

}
