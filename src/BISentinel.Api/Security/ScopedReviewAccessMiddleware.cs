using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public sealed class ScopedReviewAccessMiddleware(
    RequestDelegate next,
    IOrganizationHierarchyStore hierarchyStore,
    ApplicationAuthenticationSettings settings,
    ILogger<ScopedReviewAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requiresScopedReviewRole = RequiresScopedReviewRole(context.Request);
        var requiresSubmitPermission = context.Request.Method == HttpMethods.Post &&
            context.Request.Path.StartsWithSegments("/api/reviews/history") &&
            context.Request.Path.Value?.EndsWith("/submission", StringComparison.OrdinalIgnoreCase) == true;
        if (!requiresScopedReviewRole && !requiresSubmitPermission)
        {
            await next(context);
            return;
        }

        var user = ApplicationUserContext.Get(context);
        if (user is null)
        {
            await next(context);
            return;
        }

        var scope = ActiveWorkspaceScope.Get(context);
        if (requiresSubmitPermission)
        {
            if (user.IsAdmin || user.IsManager || user.IsLead || scope is not null)
            {
                await next(context);
                return;
            }

            await WriteForbiddenAsync(context);
            return;
        }

        if (user.IsAdmin || user.IsManager || user.IsLead)
        {
            await next(context);
            return;
        }

        if (scope is null)
        {
            await WriteForbiddenAsync(context);
            return;
        }

        try
        {
            if (!await hierarchyStore.CanReviewScopeAsync(
                    settings.TenantId,
                    user.UserId,
                    scope.ScopeType,
                    scope.ScopeId,
                    context.RequestAborted))
            {
                await WriteForbiddenAsync(context);
                return;
            }
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Authorizing a scoped review action failed.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Review access could not be verified",
                Detail = "The application could not verify lead access for the selected workspace scope.",
                Instance = context.Request.Path
            }, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool RequiresScopedReviewRole(HttpRequest request)
    {
        var path = request.Path;
        return path == "/api/reviews/submissions" ||
            path.StartsWithSegments("/api/reviews/submissions/") ||
            path == "/api/reviews/feedback" ||
            path.StartsWithSegments("/api/reviews/feedback/") ||
            (path.StartsWithSegments("/api/reviews/history/") &&
             (path.Value?.EndsWith("/feedback", StringComparison.OrdinalIgnoreCase) == true ||
              path.Value?.EndsWith("/reopen", StringComparison.OrdinalIgnoreCase) == true));
    }

    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Review lead access is required",
            Detail = "Only a lead assigned to this organization, project, or team can access reviewer queues and take reviewer actions in this scope.",
            Instance = context.Request.Path
        }, context.RequestAborted);
    }
}
