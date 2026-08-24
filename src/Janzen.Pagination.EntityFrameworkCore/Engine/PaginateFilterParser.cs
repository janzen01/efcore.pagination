using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Collections.Frozen;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal enum PaginateFilterConnector {

	And,
	Or

}

internal sealed record PaginateFilterCriterion(
	PaginateFilterOperator Operator,
	string Value,
	bool Not,
	PaginateFilterConnector Connector
);

internal static class PaginateFilterParser {

	private readonly static FrozenDictionary<string, PaginateFilterOperator> Operators = new Dictionary<string, PaginateFilterOperator>(StringComparer.OrdinalIgnoreCase) {
		["$eq"] = PaginateFilterOperator.Eq,
		["$in"] = PaginateFilterOperator.In,
		["$null"] = PaginateFilterOperator.Null,
		["$ilike"] = PaginateFilterOperator.ILike,
		["$sw"] = PaginateFilterOperator.StartsWith,
		["$contains"] = PaginateFilterOperator.Contains,
		["$lt"] = PaginateFilterOperator.LessThan,
		["$lte"] = PaginateFilterOperator.LessThanOrEqual,
		["$gt"] = PaginateFilterOperator.GreaterThan,
		["$gte"] = PaginateFilterOperator.GreaterThanOrEqual,
		["$btw"] = PaginateFilterOperator.Between
	}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	// Inverted from Operators at startup so the two directions cannot drift; two tokens accidentally mapped to the
	// same operator make ToFrozenDictionary throw at type initialization. (The reverse mistake — one token listed
	// twice in Operators — silently last-wins in the indexer initializer, so keep the token keys unique.)
	private readonly static FrozenDictionary<PaginateFilterOperator, string> OperatorTokens =
		Operators.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

	public static PaginateFilterCriterion Parse(string field, string raw) {

		if (string.IsNullOrWhiteSpace(raw)) throw new PaginateQueryException($"Filter '{field}' must not be empty.");

		string remaining = raw;
		bool not = false;
		var connector = PaginateFilterConnector.And;

		while (TryReadToken(remaining, out string token, out string afterToken)) {

			if (string.Equals(token, "$not", StringComparison.OrdinalIgnoreCase)) {
				not = true;
				remaining = afterToken;
				continue;
			}

			if (string.Equals(token, "$and", StringComparison.OrdinalIgnoreCase)) {
				connector = PaginateFilterConnector.And;
				remaining = afterToken;
				continue;
			}

			if (string.Equals(token, "$or", StringComparison.OrdinalIgnoreCase)) {
				connector = PaginateFilterConnector.Or;
				remaining = afterToken;
				continue;
			}

			if (!Operators.TryGetValue(token, out var filterOperator)) {
				throw new PaginateQueryException($"Filter '{field}' uses unknown operator '{token}'.");
			}

			// $null is documented as valueless and PaginateFilterField drops whatever follows it, so `$null:false`
			// used to behave as a bare `$null` — the opposite of what the caller wrote.
			if (filterOperator == PaginateFilterOperator.Null && afterToken.Length > 0) {
				throw new PaginateQueryException($"Filter '{field}' does not take a value for '$null'.");
			}

			return new PaginateFilterCriterion(filterOperator, afterToken, not, connector);

		}

		if (Operators.TryGetValue(remaining, out var terminalOperator)) {
			return terminalOperator != PaginateFilterOperator.Null
				? throw new PaginateQueryException($"Filter '{field}' must use the format '$operator:value'.")
				: new PaginateFilterCriterion(terminalOperator, string.Empty, not, connector);
		}

		throw new PaginateQueryException($"Filter '{field}' must use the format '$operator:value'.");

	}

	public static string GetOperatorToken(PaginateFilterOperator filterOperator) {
		return OperatorTokens.TryGetValue(filterOperator, out string? token)
			? token
			: throw new ArgumentOutOfRangeException(nameof(filterOperator), filterOperator, null);
	}

	private static bool TryReadToken(string value, out string token, out string remaining) {

		int separator = value.IndexOf(':', StringComparison.Ordinal);

		if (separator <= 0) {
			token = string.Empty;
			remaining = string.Empty;
			return false;
		}

		token = value[..separator];
		remaining = value[(separator + 1)..];

		return true;

	}

}
