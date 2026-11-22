
using i25Core.BusinessLayer.Parameters;
using System.Linq.Expressions;
using System.Reflection;
using System.Globalization;

namespace i25Core.BusinessLayer.Extensions
{
    public static class QueryableExtensions
    {
        public static IQueryable<T> Filter<T>(
            this IQueryable<T> query,
            IEnumerable<GetFilter>? filters)
        {
            if (filters == null)
                return query;

            var filterList = filters
                .Where(f => !string.IsNullOrWhiteSpace(f.column))
                .ToList();

            if (!filterList.Any())
                return query;

            var parameter = Expression.Parameter(typeof(T), "x");
            Expression? combinedExpression = null;
            string? previousConjunction = null;

            foreach (var filter in filterList)
            {
                var property = typeof(T).GetProperty(
                    filter.column!,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                if (property == null)
                    throw new ArgumentException(
                        $"Invalid filter column: {filter.column}");

                var propertyExpression =
                    Expression.Property(parameter, property);

                // Operators (=, >, contains, in, between, isNull, ...) live in FilterExpressionBuilder.
                Expression condition = FilterExpressionBuilder.Build(
                    propertyExpression,
                    property,
                    filter);

                if (combinedExpression == null)
                {
                    combinedExpression = condition;
                }
                else
                {
                    combinedExpression =
                        string.Equals(
                            previousConjunction,
                            "OR",
                            StringComparison.OrdinalIgnoreCase)
                        ? Expression.OrElse(
                            combinedExpression, condition)
                        : Expression.AndAlso(
                            combinedExpression, condition);
                }
      
                previousConjunction = filter.conjunction;
            }

            if (combinedExpression == null)
                return query;

            var lambda = Expression.Lambda<Func<T, bool>>(
                combinedExpression,
                parameter);

            return query.Where(lambda);
        }

          public static IQueryable<T> Sort<T>(
            this IQueryable<T> query,
            IEnumerable<GetSort>? sorts)
        {
            if (sorts == null || !sorts.Any())
                return query;

            var parameter = Expression.Parameter(typeof(T), "x");
            var firstSort = true;

            foreach (var sort in sorts)
            {
                if (string.IsNullOrWhiteSpace(sort.Column))
                    continue;

                var property = typeof(T).GetProperty(
                    sort.Column,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                if (property == null)
                    throw new ArgumentException(
                        $"Invalid sort column: {sort.Column}");

                var propertyExpression =
                    Expression.Property(parameter, property);

                var selector = Expression.Lambda(
                    propertyExpression, parameter);

                var direction = sort.Direction?.ToUpperInvariant();

                var methodName = firstSort
                    ? direction == "DESC" ? "OrderByDescending" : "OrderBy"
                    : direction == "DESC" ? "ThenByDescending" : "ThenBy";

                var method = typeof(Queryable)
                    .GetMethods()
                    .First(m => m.Name == methodName
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 2
                        && m.GetParameters().Length == 2);

                var genericMethod = method.MakeGenericMethod(
                    typeof(T), property.PropertyType);

                query = (IQueryable<T>)genericMethod.Invoke(
                    null,
                    new object[] { query, selector })!;

                firstSort = false;
            }

            return query;
        }        

    }
}
