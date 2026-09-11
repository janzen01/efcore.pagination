using Janzen.Pagination.EntityFrameworkCore.Model;

using System.Collections.Frozen;

namespace Janzen.Pagination.EntityFrameworkCore.Engine;

internal enum PaginateFilterConnector {

	And,
	Or

}

// Connector is null when the criterion carried no $and / $or token. The distinction is the whole point: a
// connector says how this criterion joins the one before it, so on a field's first criterion there is nothing
// for it to join to and the engine rejects it rather than reading and discarding it.
internal sealed record PaginateFilterCriterion(
	PaginateFilterOperator Operator,
	string Value,
	bool Not,
	PaginateFilterConnector? Connector
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

		if (string.IsNullOrWhiteSpace(raw)) throw new PaginateQueryException($"Filter '{field}' must not be empty.") { Code = PaginateQueryError.FilterCriterionMalformed };

		// Every filter value passes through here before an operator, a target type or a provider is chosen, which
		// is the only place one guard covers all of them: the pattern operators never reach PaginateValueConverter.
		PaginateInputGuard.RejectNul(raw, $"Filter '{field}'");

		string remaining = raw;
		bool not = false;
		PaginateFilterConnector? connector = null;

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
				throw new PaginateQueryException($"Filter '{field}' uses unknown operator '{PaginateInputGuard.Echo(token)}'.") { Code = PaginateQueryError.FilterOperatorUnknown };
			}

			// $null is documented as valueless and PaginateFilterField drops whatever follows it, so `$null:false`
			// used to behave as a bare `$null` — the opposite of what the caller wrote. Reaching here at all means
			// a colon followed the token, so a bare `$null:` is refused on the same condition: tolerating it while
			// refusing `$null:false` was an inconsistency the library invented for itself.
			if (filterOperator == PaginateFilterOperator.Null) {
				throw new PaginateQueryException($"Filter '{field}' does not take a value for '$null'.") { Code = PaginateQueryError.FilterCriterionMalformed };
			}

			return new PaginateFilterCriterion(filterOperator, afterToken, not, connector);

		}

		if (Operators.TryGetValue(remaining, out var terminalOperator)) {
			return terminalOperator != PaginateFilterOperator.Null
				? throw new PaginateQueryException($"Filter '{field}' must use the format '$operator:value'.") { Code = PaginateQueryError.FilterCriterionMalformed }
				: new PaginateFilterCriterion(terminalOperator, string.Empty, not, connector);
		}

		throw new PaginateQueryException($"Filter '{field}' must use the format '$operator:value'.") { Code = PaginateQueryError.FilterCriterionMalformed };

	}

	public static string GetConnectorToken(PaginateFilterConnector connector) { return connector == PaginateFilterConnector.Or ? "$or" : "$and"; }

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
