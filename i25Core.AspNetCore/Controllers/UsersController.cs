using FluentValidation;
using i25Core.Core.Auth;
using i25Core.Core.DTO;
using i25Core.Core.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace i25Core.AspNetCore.Controllers
{
    /// <summary>Managing other people's accounts. Administrators only.</summary>
    [ApiController]
    [Authorize(Roles = Roles.Admin)]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserAdminService _users;
        private readonly ICurrentUser _me;
        private readonly ITranslator _t;

        public UsersController(IUserAdminService users, ICurrentUser me, ITranslator translator)
        {
            _users = users;
            _me = me;
            _t = translator;
        }

        private async Task Validate<T>(T model)
        {
            var validator = HttpContext.RequestServices.GetService<IValidator<T>>();

            if (validator != null)
                await validator.ValidateAndThrowAsync(model);
        }

        /// <summary>GET /api/Users?search=ana&amp;role=Admin&amp;page=1&amp;pageSize=20</summary>
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? search, [FromQuery] string? role, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var (items, total) = await _users.ListUsersAsync(search, role, page, pageSize, cancellationToken);

            return Ok(ApiResponse<List<UserProfile>>.SuccessResult(
                items,
                _t.Get("messages.retrieved_many"),
                PaginationMeta.Create(Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), total)));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
            Ok(ApiResponse<UserProfile>.SuccessResult(await _users.GetUserAsync(id, cancellationToken), _t.Get("messages.retrieved")));

        /// <summary>POST /api/Users : create an account (default role User). The password must meet the password rules.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            var profile = await _users.CreateUserAsync(request, cancellationToken);
            return Ok(ApiResponse<UserProfile>.SuccessResult(profile, _t.Get("messages.created")));
        }

        /// <summary>PUT /api/Users/5 : change the name or switch the account on/off. A switched-off account is signed out everywhere.</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            var profile = await _users.UpdateUserAsync(id, request, _me.Id!.Value, cancellationToken);
            return Ok(ApiResponse<UserProfile>.SuccessResult(profile, _t.Get("messages.updated")));
        }

        /// <summary>PUT /api/Users/5/roles : replace the user's roles. Takes effect when their access token is next refreshed.</summary>
        [HttpPut("{id:int}/roles")]
        public async Task<IActionResult> SetRoles(int id, [FromBody] SetRolesRequest request, CancellationToken cancellationToken)
        {
            await Validate(request);
            var profile = await _users.SetRolesAsync(id, request, _me.Id!.Value, cancellationToken);
            return Ok(ApiResponse<UserProfile>.SuccessResult(profile, _t.Get("messages.updated")));
        }

        /// <summary>POST /api/Users/5/unlock : clears a lockout caused by too many wrong passwords.</summary>
        [HttpPost("{id:int}/unlock")]
        public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken)
        {
            await _users.UnlockUserAsync(id, cancellationToken);
            return Ok(ApiResponse<object?>.SuccessResult(null, _t.Get("auth.unlocked")));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            await _users.DeleteUserAsync(id, _me.Id!.Value, cancellationToken);
            return Ok(ApiResponse<object?>.SuccessResult(null, _t.Get("messages.deleted")));
        }
    }

    /// <summary>The roles that can be given to users. Administrators only.</summary>
    [ApiController]
    [Authorize(Roles = Roles.Admin)]
    [Route("api/[controller]")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class RolesController : ControllerBase
    {
        private readonly IUserAdminService _users;
        private readonly ITranslator _t;

        public RolesController(IUserAdminService users, ITranslator translator)
        {
            _users = users;
            _t = translator;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken) =>
            Ok(ApiResponse<List<RoleDto>>.SuccessResult(await _users.ListRolesAsync(cancellationToken), _t.Get("messages.retrieved_many")));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
        {
            var validator = HttpContext.RequestServices.GetService<IValidator<CreateRoleRequest>>();

            if (validator != null)
                await validator.ValidateAndThrowAsync(request);

            return Ok(ApiResponse<RoleDto>.SuccessResult(await _users.CreateRoleAsync(request, cancellationToken), _t.Get("messages.created")));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            await _users.DeleteRoleAsync(id, cancellationToken);
            return Ok(ApiResponse<object?>.SuccessResult(null, _t.Get("messages.deleted")));
        }
    }
}
