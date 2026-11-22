using i25Core.BusinessLayer.Parameters;
using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace i25Core.BusinessLayer.Extensions
{
    /// <summary>
    /// Builds the WHERE expression for a single <see cref="GetFilter"/>.
    /// Every expression here is translatable to SQL by EF Core.
    /// </summary>
    internal static class FilterExpressionBuilder
    {
        private const char ListSeparator = ',';

        private static readonly MethodInfo StringContains =
            typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

        private static readonly MethodInfo StringStartsWith =
            typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;

        private static readonly MethodInfo StringEndsWith =
            typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;

        private static readonly MethodInfo StringCompare =
            typeof(string).GetMethod(nameof(string.Compare), new[] { typeof(string), typeof(string) })!;

        public static Expression Build(
            MemberExpression property,
            PropertyInfo propertyInfo,
            GetFilter filter)
        {
            var op = Normalize(filter.condition);
            var column = propertyInfo.Name;

            return op switch
            {
                "=" or "eq" or "equal" or "equals" =>
                    Equality(property, column, filter.value, negate: false),

                "!=" or "<>" or "ne" or "notequal" or "notequals" =>
                    Equality(property, column, filter.value, negate: true),

                ">" or "gt" or "greaterthan" =>
                    Compare(property, column, filter.value, ExpressionType.GreaterThan),

                ">=" or "gte" or "greaterthanorequal" =>
                    Compare(property, column, filter.value, ExpressionType.GreaterThanOrEqual),

                "<" or "lt" or "lessthan" =>
                    Compare(property, column, filter.value, ExpressionType.LessThan),

                "<=" or "lte" or "lessthanorequal" =>
                    Compare(property, column, filter.value, ExpressionType.LessThanOrEqual),

                "between" =>
                    Between(property, column, filter.value),

                "contains" or "like" =>
                    StringMethod(property, column, filter.value, StringContains, op),

                "notcontains" or "notlike" =>
                    Expression.Not(StringMethod(property, column, filter.value, StringContains, op)),

                "startswith" =>
                    StringMethod(property, column, filter.value, StringStartsWith, op),

                "endswith" =>
                    StringMethod(property, column, filter.value, StringEndsWith, op),

                "in" =>
                    In(property, column, filter.value),

                "notin" =>
                    Expression.Not(In(property, column, filter.value)),

                "isnull" =>
                    NullCheck(property, column, isNull: true),

                "isnotnull" =>
                    NullCheck(property, column, isNull: false),

                _ => throw new NotSupportedException(
                    $"Unsupported condition: {filter.condition}")
            };
        }

        // "Not Equal", "not_equal", "NotEqual" -> "notequal"; symbols are kept as-is.
        private static string Normalize(string? condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return "=";

            return new string(condition
                    .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-')
                    .ToArray())
                .ToLowerInvariant();
        }

        private static Expression Equality(MemberExpression property, string column, string? value, bool negate)
        {
            // "= null" / "!= null" behave like isNull / isNotNull.
            if (value == null)
                return NullCheck(property, column, isNull: !negate);

            var constant = Constant(property.Type, column, value);

            return negate
                ? Expression.NotEqual(property, constant)
                : Expression.Equal(property, constant);
        }

        private static Expression Compare(MemberExpression property, string column, string? value, ExpressionType comparison)
        {
            RequireValue(column, value);

            if (property.Type == typeof(string))
            {
                // string.Compare(x.Col, value) > 0  ->  SQL: x.Col > value
                var compare = Expression.Call(StringCompare, property, Expression.Constant(value, typeof(string)));
                return Expression.MakeBinary(comparison, compare, Expression.Constant(0));
            }

            if (UnderlyingType(property.Type) == typeof(bool) || UnderlyingType(property.Type) == typeof(Guid))
                throw new ArgumentException($"Condition '{comparison}' is not supported for column {column}.");

            return Expression.MakeBinary(comparison, property, Constant(property.Type, column, value));
        }

        private static Expression Between(MemberExpression property, string column, string? value)
        {
            var parts = SplitList(value);

            if (parts.Length != 2)
                throw new ArgumentException(
                    $"Condition 'between' on column {column} needs two values separated by a comma, e.g. \"10,20\".");

            return Expression.AndAlso(
                Compare(property, column, parts[0], ExpressionType.GreaterThanOrEqual),
                Compare(property, column, parts[1], ExpressionType.LessThanOrEqual));
        }

        private static Expression StringMethod(MemberExpression property, string column, string? value, MethodInfo method, string op)
        {
            if (property.Type != typeof(string))
                throw new ArgumentException($"Condition '{op}' can only be used on text columns; {column} is {FriendlyName(property.Type)}.");

            RequireValue(column, value);

            // x.Col != null && x.Col.Contains(value)
            return Expression.AndAlso(
                Expression.NotEqual(property, Expression.Constant(null, typeof(string))),
                Expression.Call(property, method, Expression.Constant(value, typeof(string))));
        }

        private static Expression In(MemberExpression property, string column, string? value)
        {
            var parts = SplitList(value);

            if (parts.Length == 0)
                throw new ArgumentException($"Condition 'in' on column {column} needs at least one value, e.g. \"1,2,3\".");

            // new List<TProp> { ... }.Contains(x.Col)  ->  SQL: x.Col IN (...)
            var listType = typeof(List<>).MakeGenericType(property.Type);
            var list = (IList)Activator.CreateInstance(listType)!;

            foreach (var part in parts)
                list.Add(ConvertValue(property.Type, column, part));

            var contains = listType.GetMethod(nameof(List<object>.Contains), new[] { property.Type })!;

            return Expression.Call(Expression.Constant(list, listType), contains, property);
        }

        private static Expression NullCheck(MemberExpression property, string column, bool isNull)
        {
            if (property.Type.IsValueType && Nullable.GetUnderlyingType(property.Type) == null)
                throw new ArgumentException($"Column {column} cannot be null, so 'isNull' / 'isNotNull' do not apply.");

            var nullConstant = Expression.Constant(null, property.Type);

            return isNull
                ? Expression.Equal(property, nullConstant)
                : Expression.NotEqual(property, nullConstant);
        }

        private static ConstantExpression Constant(Type propertyType, string column, string value)
        {
            // Typed as the property type (incl. Nullable<T>) so comparisons are lifted correctly.
            return Expression.Constant(ConvertValue(propertyType, column, value), propertyType);
        }

        private static object? ConvertValue(Type propertyType, string column, string raw)
        {
            var target = UnderlyingType(propertyType);
            var value = raw.Trim();

            try
            {
                if (target == typeof(string)) return raw;
                if (target == typeof(Guid)) return Guid.Parse(value);
                if (target.IsEnum) return Enum.Parse(target, value, ignoreCase: true);
                if (target == typeof(DateOnly)) return DateOnly.Parse(value, CultureInfo.InvariantCulture);
                if (target == typeof(TimeOnly)) return TimeOnly.Parse(value, CultureInfo.InvariantCulture);
                if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
                if (target == typeof(bool)) return value switch
                {
                    "1" => true,
                    "0" => false,
                    _ => bool.Parse(value)
                };

                return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                throw new ArgumentException(
                    $"Value '{raw}' is not a valid {FriendlyName(target)} for column {column}.");
            }
        }

        private static string[] SplitList(string? value) =>
            (value ?? string.Empty)
                .Split(ListSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        private static void RequireValue(string column, string? value)
        {
            if (value == null)
                throw new ArgumentException($"A value is required to filter column {column}.");
        }

        private static Type UnderlyingType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

        private static string FriendlyName(Type type)
        {
            var t = UnderlyingType(type);

            if (t == typeof(string)) return "text";
            if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "whole number";
            if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return "number";
            if (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly)) return "date";
            if (t == typeof(bool)) return "true/false value";
            if (t == typeof(Guid)) return "GUID";

            return t.Name;
        }
    }
}
