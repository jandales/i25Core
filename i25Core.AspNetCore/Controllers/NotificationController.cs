using i25Core.Core.Auth;
using i25Core.Core.DTO;
using i25Core.Core.Exceptions;
using i25Core.Core.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace i25Core.AspNetCore.Controllers
{
    /// <summary>
    /// A receiver's stored notifications (database channel).
    /// A signed-in user can read and manage only their own inbox (type "User" and their own id).
    /// Administrators can read any inbox.
    /// </summary>
    [ApiController]
    [Route("api/[controller]/{notifiableType}/{notifiableId:int}")]
    [Route("api/v{version:apiVersion}/[controller]/{notifiableType}/{notifiableId:int}")]
    public class NotificationController : ControllerBase, IActionFilter
    {
        private readonly INotificationInbox _inbox;
        private readonly ICurrentUser _me;

        public NotificationController(INotificationInbox inbox, ICurrentUser me)
        {
            _inbox = inbox;
            _me = me;
        }

        // Runs before every action below: whose inbox is this?
        [NonAction]
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (_me.IsInRole(Roles.Admin))
                return;

            var type = context.RouteData.Values["notifiableType"]?.ToString();
            var id = context.RouteData.Values["notifiableId"]?.ToString();

            var isOwn = string.Equals(type, "User", StringComparison.OrdinalIgnoreCase)
                        && _me.Id is { } me
                        && string.Equals(id, me.ToString(), StringComparison.Ordinal);

            if (!isOwn)
                throw new ForbiddenException();     // answered as 403 in the standard envelope
        }

        [NonAction]
        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        /// <summary>GET /api/Notification/Vendor/17?unreadOnly=true&amp;take=20 : newest first.</summary>
        [HttpGet]
        public async Task<IActionResult> List(
            string notifiableType, int notifiableId, [FromQuery] bool unreadOnly = false, [FromQuery] int take = 50,
            CancellationToken cancellationToken = default)
        {
            var items = await _inbox.GetAsync(notifiableType, notifiableId, unreadOnly, take, cancellationToken);
            return Ok(ApiResponse<List<NotificationDto>>.SuccessResult(items, "Notifications retrieved successfully."));
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount(string notifiableType, int notifiableId, CancellationToken cancellationToken = default)
        {
            var count = await _inbox.UnreadCountAsync(notifiableType, notifiableId, cancellationToken);
            return Ok(ApiResponse<int>.SuccessResult(count));
        }

        [HttpPost("{id:int}/read")]
        public async Task<IActionResult> MarkAsRead(string notifiableType, int notifiableId, int id, CancellationToken cancellationToken = default)
        {
            var found = await _inbox.MarkAsReadAsync(notifiableType, notifiableId, id, cancellationToken);

            return found
                ? Ok(ApiResponse<object?>.SuccessResult(null, "Notification marked as read."))
                : NotFound(ApiResponse<object?>.Failure("Notification not found."));
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllAsRead(string notifiableType, int notifiableId, CancellationToken cancellationToken = default)
        {
            var changed = await _inbox.MarkAllAsReadAsync(notifiableType, notifiableId, cancellationToken);
            return Ok(ApiResponse<int>.SuccessResult(changed, $"{changed} notification(s) marked as read."));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(string notifiableType, int notifiableId, int id, CancellationToken cancellationToken = default)
        {
            var deleted = await _inbox.DeleteAsync(notifiableType, notifiableId, id, cancellationToken);

            return deleted
                ? Ok(ApiResponse<object?>.SuccessResult(null, "Notification deleted."))
                : NotFound(ApiResponse<object?>.Failure("Notification not found."));
        }
    }
}
