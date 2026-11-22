using i25Core.Core.DTO;
using i25Core.Core.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace i25Core.Web.Controllers
{
    /// <summary>
    /// A receiver's stored notifications (database channel).
    /// There is no authentication yet, so callers name the receiver in the URL; once login exists,
    /// read the receiver from the signed-in user instead.
    /// </summary>
    [ApiController]
    [Route("api/[controller]/{notifiableType}/{notifiableId:int}")]
    [Route("api/v{version:apiVersion}/[controller]/{notifiableType}/{notifiableId:int}")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationInbox _inbox;

        public NotificationController(INotificationInbox inbox)
        {
            _inbox = inbox;
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
