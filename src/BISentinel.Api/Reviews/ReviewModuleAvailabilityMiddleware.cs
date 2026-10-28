using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Reviews;

public sealed class ReviewModuleAvailabilityMiddleware(
    RequestDelegate next,
    ILogger<ReviewModuleAvailabilityMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(
        HttpContext context,
        IReviewConfigurationStore configurationStore)
    {
        if (!context.Request.Path.StartsWithSegments("/api/reviews", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Request.Path.StartsWithSegments(
                    "/api/governance-audit",
                    StringComparison.OrdinalIgnoreCase))
            {
                await InvokeGovernanceAuditAsync(context, configurationStore);
                return;
            }

            await next(context);
            return;
        }

        ReviewConfigurationSnapshot snapshot;
        try
        {
            snapshot = await configurationStore.GetAsync(context.RequestAborted);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Checking review module availability failed.");
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Module availability could not be checked",
                "The review module configuration is unavailable.");
            return;
        }

        if (snapshot.Configuration.Modules is null)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Module availability is invalid",
                "Reload and save the module configuration in Administration.");
            return;
        }

        if (!snapshot.Configuration.Modules.Reviews)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status404NotFound,
                "Review module is disabled",
                "Enable the Reviews module in Administration to use this endpoint.");
            return;
        }

        await next(context);
    }

    private async Task InvokeGovernanceAuditAsync(
        HttpContext context,
        IReviewConfigurationStore configurationStore)
    {
        ReviewConfigurationSnapshot snapshot;
        try
        {
            snapshot = await configurationStore.GetAsync(context.RequestAborted);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Checking Governance Audit module availability failed.");
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Module availability could not be checked",
                "The Governance Audit module configuration is unavailable.");
            return;
        }

        if (snapshot.Configuration.Modules is null)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Module availability is invalid",
                "Reload and save module availability in Administration.");
            return;
        }

        if (!snapshot.Configuration.Modules.GovernanceAudit)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status404NotFound,
                "Governance Audit module is disabled",
                "Enable Governance Audit in Administration to use this endpoint.");
            return;
        }

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
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                type = "about:blank",
                title,
                status = statusCode,
                detail,
                instance = context.Request.Path.Value
            },
            JsonOptions,
            context.RequestAborted);
    }
}
