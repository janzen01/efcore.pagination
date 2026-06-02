using System.Linq.Expressions;
using System.Reflection;

using Janzen.Pagination.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

namespace Janzen.Pagination.PostgreSql;

// Emits PostgreSQL's native ILIKE for true case-insensitive search.
internal sealed class NpgsqlLikeStrategy : IPaginateLikeStrategy
{
    private const string EscapeCharacter = "\\";

    private static readonly MethodInfo ILikeMethod = typeof(NpgsqlDbFunctionsExtensions)
        .GetMethods()
        .Single(method =>
            method.Name == nameof(NpgsqlDbFunctionsExtensions.ILike) &&
            method.GetParameters().Length == 4);

    public Expression BuildLike(Expression value, Expression pattern)
    {
        var functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
        return Expression.Call(ILikeMethod, functions, value, pattern, Expression.Constant(EscapeCharacter));
    }
}
