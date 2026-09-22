using System.Security.Claims;
using HanakaServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Helpers;

public static class CoordinatorPortalAuthentication
{
    public const string Scheme = "CoordinatorPortal";

    public static AuthenticationBuilder AddCoordinatorPortal(this AuthenticationBuilder builder) => builder.AddCookie(Scheme, options =>
    {
        options.Cookie.Name = "Hanaka.Coordinator";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/CoordinatorPortal/Login";
        options.AccessDeniedPath = options.LoginPath;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context => Redirect(context, 401),
            OnRedirectToAccessDenied = context => Redirect(context, 403),
            OnValidatePrincipal = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<PickleballDbContext>();
                if (!long.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                    || !await db.Users.AsNoTracking().AnyAsync(u => u.UserId == id && u.IsActive
                        && u.UserRoles.Any(r => r.Role.RoleCode == RoleCodes.Coordinator), context.HttpContext.RequestAborted))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(Scheme);
                }
            }
        };
    });

    private static Task Redirect(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        if (context.Request.Path.StartsWithSegments("/api/coordinator-portal"))
        {
            context.Response.StatusCode = status;
            return context.Response.WriteAsJsonAsync(new { message = "Phiên điều phối đã hết hạn hoặc quyền đã thay đổi. Vui lòng đăng nhập lại." });
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
