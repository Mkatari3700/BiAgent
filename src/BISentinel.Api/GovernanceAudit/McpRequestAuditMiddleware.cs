using System.Diagnostics;

namespace BISentinel.Api.GovernanceAudit;

public sealed class McpRequestAuditMiddleware(
    RequestDelegate next,
    ILogger<McpRequestAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await next(context);
            return;
        }

        var requestId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var actorObjectId =
            context.User.FindFirst("oid")?.Value ??
            context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        logger.LogInformation(
            "MCP HTTP request {RequestId} started. Method={Method}; ActorObjectId={ActorObjectId}.",
            requestId,
            context.Request.Method,
            actorObjectId ?? "unauthenticated");

        try
        {
            await next(context);
        }
        finally
        {
            logger.LogInformation(
                "MCP HTTP request {RequestId} completed. Method={Method}; StatusCode={StatusCode}; DurationMs={DurationMs}; ActorObjectId={ActorObjectId}.",
                requestId,
                context.Request.Method,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                actorObjectId ?? "unauthenticated");
        }
    }
}
