using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PlaymoxAPI.Extensions.Auth
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class BaseAuth : AuthorizeAttribute, IAuthorizationFilter
    {
        private readonly List<string>? _requiredPermissions;

        public BaseAuth(string[] requiredPermissions)
        {
            this._requiredPermissions = requiredPermissions.ToList();
        }
        public BaseAuth()
        {
            this._requiredPermissions = null;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // Skip permission check when no specific permissions are required
            if (_requiredPermissions == null)
            {
                return;
            }

            // Skip authorization when [IgnoreAuth] attribute is present
            if ((context.ActionDescriptor as ControllerActionDescriptor)?.MethodInfo.GetCustomAttributes(typeof(IgnoreAuthAttribute), false).Any() == true)
            {
                return;
            }

            var userSid = context.HttpContext.User.Claims.SingleOrDefault(p => p.Type == System.Security.Claims.ClaimTypes.Sid)?.Value;
            var longId = long.TryParse(userSid, out var id) ? id : -1;

            if (longId == -1)
            {
                throw new Exception($"Cannot convert user id to long ({userSid})");
            }

            if (!OnApiAuthorization(longId, _requiredPermissions))
            {
                context.Result = new ObjectResult(context.ModelState)
                {
                    Value = "No permissions to perform this action",
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }

        private bool OnApiAuthorization(long userId, List<string>? requiredPermissions)
        {
            if (requiredPermissions == null || requiredPermissions.Count == 0)
            {
                return true;
            }

            var userRoles = Config.GetUserRoleWithCache(userId);

            if (userRoles.Any(x => x.Role.Name == "Admin"))
            {
                return true;
            }

            requiredPermissions = requiredPermissions.Select(x => x.ToUpper()).ToList();

            // Check if any of the required permissions are present in the user's role permissions
            if (userRoles.Any(x => x.Role.Permissions.Any(y => requiredPermissions.Contains(y.PermissionId.ToUpper()))))
            {
                return true;
            }

            // Denied access
            return false;
        }
    }
}
