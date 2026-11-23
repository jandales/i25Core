using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using i25Core.Core.Transactions;

namespace i25Core.Web.Transactions
{
    /// <summary>
    /// Runs the whole action in one database transaction. It commits when the action returns a success result
    /// and rolls back when the action throws or returns a 4xx/5xx result.
    /// Put it on an action or on a controller: <c>[Transactional]</c>, or <c>[Transactional(IsolationLevel.Serializable)]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TransactionalAttribute : TypeFilterAttribute
    {
        public TransactionalAttribute() : base(typeof(TransactionFilter))
            => Arguments = new object[] { IsolationLevel.Unspecified };   // Unspecified = the database default

        public TransactionalAttribute(IsolationLevel isolationLevel) : base(typeof(TransactionFilter))
            => Arguments = new object[] { isolationLevel };
    }

    internal sealed class TransactionFilter : IAsyncActionFilter
    {
        private readonly IUnitOfWork _uow;
        private readonly IsolationLevel _level;

        public TransactionFilter(IUnitOfWork uow, IsolationLevel level)
        {
            _uow = uow;
            _level = level;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            await using var scope = await _uow.BeginAsync(_level == IsolationLevel.Unspecified ? null : _level, context.HttpContext.RequestAborted);

            var executed = await next();

            // An exception that nobody handled, or an error response: leave without committing = rollback.
            if (executed.Exception != null && !executed.ExceptionHandled)
                return;

            if (IsFailure(executed.Result))
                return;

            await scope.CommitAsync(context.HttpContext.RequestAborted);
        }

        private static bool IsFailure(IActionResult? result) => result switch
        {
            IStatusCodeActionResult { StatusCode: >= 400 } => true,
            _ => false
        };
    }
}
