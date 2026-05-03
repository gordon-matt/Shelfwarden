//using System.Security.Claims;
//using Microsoft.AspNetCore.Http;

//namespace Shelfwarden.Services.Auth;

//public class UserContextService(IHttpContextAccessor httpContextAccessor) : IUserContextService
//{
//    public string? GetCurrentUserId()
//        => httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

//    public string? GetCurrentUserName()
//        => httpContextAccessor.HttpContext?.User?.Identity?.Name;

//    public bool IsAuthenticated()
//        => httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

//    public bool IsAdministrator()
//        => httpContextAccessor.HttpContext?.User?.IsInRole(Constants.Roles.Administrator) ?? false;

//    public IReadOnlyList<string> GetRoleNames()
//    {
//        var user = httpContextAccessor.HttpContext?.User;
//        if (user is null)
//        {
//            return [];
//        }

//        return user.Claims
//            .Where(c => c.Type == ClaimTypes.Role)
//            .Select(c => c.Value)
//            .Distinct(StringComparer.OrdinalIgnoreCase)
//            .ToList();
//    }
//}