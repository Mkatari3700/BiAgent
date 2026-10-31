using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public sealed class ApplicationUserAccessMiddleware(
    RequestDelegate next,
    IApplicationUserStore userStore,
    ApplicationAuthenticationSettings settings,
    ILogger<ApplicationUserAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requiresApplicationRole =
            context.Request.Path.StartsWithSegments("/api/admin") ||
            context.Request.Path.StartsWithSegments("/api/governance-audit") ||
            context.Request.Path.StartsWithSegments("/api/reviews") ||
            context.Request.Path.StartsWithSegments("/mcp");
        var isSessionRequest = context.Request.Path == "/api/auth/session";
        if ((!requiresApplicationRole && !isSessionRequest) ||
            context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (!TryGetGuidClaim(context.User, "tid", "http://schemas.microsoft.com/identity/claims/tenantid", out var tenantId) ||
            !TryGetGuidClaim(context.User, "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier", out var objectId) ||
            tenantId != settings.TenantId)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                "This account cannot access BI Sentinel.",
                "The signed-in account does not contain a valid identity for the configured Entra tenant.");
            return;
        }

        var displayName = context.User.FindFirst("name")?.Value
            ?? context.User.FindFirst(ClaimTypes.Name)?.Value
            ?? context.User.Identity.Name;
        var emailAddress = context.User.FindFirst("preferred_username")?.Value
            ?? context.User.FindFirst("email")?.Value
            ?? context.User.FindFirst(ClaimTypes.Email)?.Value;

        ApplicationUserSession applicationUser;
        try
        {
            applicationUser = await userStore.EnsureUserAsync(
                tenantId,
                objectId,
                displayName,
                emailAddress,
                settings.BootstrapAdminObjectId,
                context.RequestAborted);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Provisioning the signed-in BI Sentinel user failed.");
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Application access is unavailable.",
                ApplicationDatabaseError.GetDetail(
                    exception,
                    "Verify database migration 008 and the application database permissions."));
            return;
        }

        if (!applicationUser.IsEnabled)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                "This account is disabled.",
                "Contact a BI Sentinel administrator.");
            return;
        }

        context.Items[ApplicationUserContext.ItemKey] = applicationUser;
        var roleClaims = new List<Claim>
        {
            new(ClaimTypes.Role, "ApplicationUser")
        };
        if (applicationUser.IsAdmin)
        {
            roleClaims.Add(new Claim(ClaimTypes.Role, "ApplicationAdmin"));
        }
        if (applicationUser.IsManager)
        {
            roleClaims.Add(new Claim(ClaimTypes.Role, "ReviewManager"));
        }
        if (applicationUser.IsLead)
        {
            roleClaims.Add(new Claim(ClaimTypes.Role, "ReviewLead"));
        }

        context.User.AddIdentity(new ClaimsIdentity(roleClaims, "BI Sentinel application roles"));
        await next(context);
    }

    private static bool TryGetGuidClaim(
        ClaimsPrincipal principal,
        string shortClaimType,
        string mappedClaimType,
        out Guid value)
    {
        var claimValue = principal.FindFirst(shortClaimType)?.Value
            ?? principal.FindFirst(mappedClaimType)?.Value;
        return Guid.TryParse(claimValue, out value);
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        }, context.RequestAborted);
    }
}

public sealed record ApplicationAuthenticationSettings(
    Guid TenantId,
    Guid BootstrapAdminObjectId);

public static class ApplicationUserContext
{
    public const string ItemKey = "BI Sentinel Application User";

    public static ApplicationUserSession? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value)
            ? value as ApplicationUserSession
            : null;
}
