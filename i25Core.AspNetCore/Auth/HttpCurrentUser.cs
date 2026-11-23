using System.Security.Claims;
using i25Core.Core.Auth;

namespace i25Core.AspNetCore.Auth
{
    /// <summary>The signed-in user of the current request, read from the validated access token.</summary>
    public sealed class HttpCurrentUser : ICurrentUser
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpCurrentUser(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

        public int? Id =>
            int.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

        public string? Email => Principal?.FindFirst("email")?.Value;

        public string? Name => Principal?.FindFirst("name")?.Value;

        public IReadOnlyCollection<string> Roles =>
            Principal?.FindAll("role").Select(c => c.Value).ToArray() ?? Array.Empty<string>();

        public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
    }
}
