using Microsoft.AspNetCore.Mvc;

namespace BISentinel.Api.Security;

public sealed class WorkspaceScopeMiddleware(
    RequestDelegate next,
    IOrganizationHierarchyStore hierarchyStore,
    ApplicationAuthenticationSettings settings,
    ILogger<WorkspaceScopeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path == "/api/auth/session" ||
            context.Request.Path == "/api/workspace/scopes")
        {
            await next(context);
            return;
        }

        var scopeType = context.Request.Headers["X-BI-Sentinel-Scope-Type"].ToString();
        var scopeIdValue = context.Request.Headers["X-BI-Sentinel-Scope-Id"].ToString();
        if (string.IsNullOrEmpty(scopeType) && string.IsNullOrEmpty(scopeIdValue))
        {
            var unscopedUser = ApplicationUserContext.Get(context);
            var requiresWorkspaceScope =
                context.Request.Path.StartsWithSegments("/api/dashboard") ||
                context.Request.Path.StartsWithSegments("/api/support") ||
                context.Request.Path.StartsWithSegments("/api/reviews");
            var unscopedPowerBiScope =
                context.Request.Path.StartsWithSegments("/api/governance-audit") ||
                context.Request.Path.StartsWithSegments("/api/reviews/online") ||
                context.Request.Path.StartsWithSegments("/mcp");
            if (unscopedUser is not null && !unscopedUser.IsAdmin &&
                (requiresWorkspaceScope || unscopedPowerBiScope))
            {
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status409Conflict,
                    "Workspace scope selection is required",
                    unscopedPowerBiScope
                        ? "This Power BI data is not mapped to BI Sentinel scopes. A root administrator must use the global All organizations context."
                        : "Choose an assigned organization, project, or team before using this data.");
                return;
            }

            await next(context);
            return;
        }

        if (scopeType is not ("Organization" or "Project" or "Team") ||
            !Guid.TryParse(scopeIdValue, out var scopeId))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid workspace scope",
                "Choose a valid organization, project, or team.");
            return;
        }

        var user = ApplicationUserContext.Get(context);
        if (user is null)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Sign-in required",
                "Sign in before choosing a workspace scope.");
            return;
        }

        try
        {
            if (!await hierarchyStore.IsScopeAccessibleAsync(
                    settings.TenantId,
                    user.UserId,
                    scopeType,
                    scopeId,
                    user.IsAdmin,
                    context.RequestAborted))
            {
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Workspace scope is not available",
                    "Your account does not have access to the selected organization, project, or team.");
                return;
            }
        }
        catch (Exception exception) when (exception is Microsoft.Data.SqlClient.SqlException)
        {
            logger.LogError(exception, "Validating the active workspace scope failed.");
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Workspace scope could not be verified",
                "The application could not verify your selected organization, project, or team.");
            return;
        }

        var activeScope = new ActiveWorkspaceScope(scopeType, scopeId);
        var usesUnmappedPowerBiScope =
            context.Request.Path.StartsWithSegments("/api/governance-audit") ||
            context.Request.Path.StartsWithSegments("/api/reviews/online") ||
            context.Request.Path.StartsWithSegments("/mcp");
        if (usesUnmappedPowerBiScope)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                "Power BI data is not mapped to this workspace scope",
                "Governance and online review are not yet associated with BI Sentinel organizations, projects, or teams. Switch to All organizations to use these Power BI-wide tools.");
            return;
        }

        context.Items[ActiveWorkspaceScope.ItemKey] = activeScope;
        await next(context);
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
