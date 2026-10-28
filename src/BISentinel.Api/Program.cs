using BISentinel.Api.Feedback;
using BISentinel.Api.GovernanceAudit;
using BISentinel.Api.Reviews;
using BISentinel.Api.ReviewHistory;
using BISentinel.Api.Security;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

const long MaximumUploadRequestBytes = 501L * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = MaximumUploadRequestBytes);

var connectionString = builder.Configuration.GetConnectionString("BiAgent");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'BiAgent' is required. Configure it using user-secrets or deployment configuration.");
}

var governanceAuditEnabled = builder.Configuration.GetValue<bool>("GovernanceAudit:Enabled");
var entraAuthenticationEnabled = builder.Configuration.GetValue<bool>("Authentication:Entra:Enabled");
if (!entraAuthenticationEnabled)
{
    throw new InvalidOperationException(
        "Authentication:Entra:Enabled must be true because BI Sentinel requires sign-in for all application access.");
}

var maximumGovernanceWorkspaces = builder.Configuration.GetValue<int>(
    "GovernanceAudit:MaximumWorkspacesPerScan",
    50);
if (maximumGovernanceWorkspaces is < 1 or > 100)
{
    throw new InvalidOperationException(
        "GovernanceAudit:MaximumWorkspacesPerScan must be between 1 and 100.");
}

builder.Services.AddAuthentication();
if (entraAuthenticationEnabled)
{
    var tenantId = builder.Configuration["Authentication:Entra:TenantId"];
    var clientId = builder.Configuration["Authentication:Entra:ClientId"];
    var bootstrapAdminObjectId = builder.Configuration["Authentication:BootstrapAdminObjectId"];
    if (!Guid.TryParse(tenantId, out var tenantGuid) ||
        string.IsNullOrWhiteSpace(clientId))
    {
        throw new InvalidOperationException(
            "Authentication:Entra:TenantId must be a GUID and Authentication:Entra:ClientId is required when app sign-in is enabled.");
    }

    if (!Guid.TryParse(bootstrapAdminObjectId, out var bootstrapAdminGuid))
    {
        throw new InvalidOperationException(
            "Authentication:BootstrapAdminObjectId must be the Entra object ID of the initial BI Sentinel administrator.");
    }

    builder.Services.AddSingleton(
        new ApplicationAuthenticationSettings(tenantGuid, bootstrapAdminGuid));
    builder.Services.AddSingleton<IApplicationUserStore>(_ =>
        new SqlApplicationUserStore(connectionString));
    builder.Services
        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("Authentication:Entra"))
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();
    builder.Services.PostConfigure<OpenIdConnectOptions>(
        OpenIdConnectDefaults.AuthenticationScheme,
        options =>
        {
            options.ResponseType = "code";
            options.UsePkce = true;
            options.Events ??= new OpenIdConnectEvents();
            var existingRedirectHandler = options.Events.OnRedirectToIdentityProvider;
            options.Events.OnRedirectToIdentityProvider = async context =>
            {
                if (existingRedirectHandler is not null)
                {
                    await existingRedirectHandler(context);
                }

                if (context.Properties.Items.TryGetValue("PowerBiConsent", out var consentRequested) &&
                    string.Equals(consentRequested, "true", StringComparison.Ordinal))
                {
                    var scopes = context.ProtocolMessage.Scope
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Concat(PowerBiDelegatedAccessTokenProvider.RequiredScopes)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    context.ProtocolMessage.Scope = string.Join(' ', scopes);
                }
            };
        });
    builder.Services.Configure<CookieAuthenticationOptions>(
        CookieAuthenticationDefaults.AuthenticationScheme,
        options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });
}

builder.Services.AddAuthorization(options =>
{
    if (entraAuthenticationEnabled)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }

    options.AddPolicy("ApplicationAdmin", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("ApplicationAdmin"));
    options.AddPolicy("GovernanceAuditReader", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("ApplicationAdmin"));
    options.AddPolicy("GovernanceAuditAdmin", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("ApplicationAdmin"));
});

var feedbackSqlEnabled = builder.Configuration.GetValue<bool>("Feedback:SqlEnabled");
var reviewHistoryRetentionDays = builder.Configuration.GetValue<int>("ReviewHistory:RetentionDays", 30);
if (reviewHistoryRetentionDays is < 1 or > 3650)
{
    throw new InvalidOperationException("ReviewHistory:RetentionDays must be between 1 and 3650.");
}

builder.Services.AddOptions<ReviewOptions>()
    .Bind(builder.Configuration.GetSection("Review"))
    .Validate(options => IsValidProjectId(options.ProjectId), "Review:ProjectId must be a stable identifier (1-100 letters, numbers, dots, underscores, or hyphens).")
    .Validate(options => IsValidRegex(options.TableNamePattern), "Review:TableNamePattern must be a valid regular expression.")
    .Validate(options => IsValidRegex(options.ColumnNamePattern), "Review:ColumnNamePattern must be a valid regular expression.")
    .Validate(options => IsValidRegex(options.MeasureNamePattern), "Review:MeasureNamePattern must be a valid regular expression.")
    .Validate(options => IsValidRegex(options.PlaceholderColumnPattern), "Review:PlaceholderColumnPattern must be a valid regular expression.")
    .Validate(options => options.Rules.Values.All(policy =>
        policy.Severity is "Error" or "Warning" or "Information"), "Rule severity must be Error, Warning, or Information.")
    .ValidateOnStart();
builder.Services.AddSingleton<IPbipProjectReader, PbipProjectReader>();
builder.Services.AddSingleton<IPbipProjectReviewer, PbipProjectReviewer>();
builder.Services.AddSingleton<IReviewConfigurationStore>(_ =>
    new SqlReviewConfigurationStore(connectionString));
builder.Services.AddSingleton<IGovernanceAuditConfigurationStore>(_ =>
    new SqlGovernanceAuditConfigurationStore(connectionString));
builder.Services.AddSingleton<IGovernanceAuditHistoryStore>(_ =>
    new SqlGovernanceAuditHistoryStore(connectionString));
builder.Services.AddScoped<IPowerBiDelegatedAccessTokenProvider, PowerBiDelegatedAccessTokenProvider>();
builder.Services.AddHttpClient<PowerBiGovernanceAuditClient>(client =>
{
    client.BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<PbipProjectInputService>();
builder.Services.AddSingleton<PbiToolsExtractor>();
builder.Services.AddSingleton<IReviewRule, ProjectStructureRule>();
builder.Services.AddSingleton<IReviewRule, TableNamingRule>();
builder.Services.AddSingleton<IReviewRule, ColumnNamingRule>();
builder.Services.AddSingleton<IReviewRule, PlaceholderColumnRule>();
builder.Services.AddSingleton<IReviewRule, MeasureTableNameRule>();
builder.Services.AddSingleton<IReviewRule, MeasureNamingRule>();
builder.Services.AddSingleton<IReviewRule, RequiredMeasuresRule>();
builder.Services.AddSingleton<IReviewRule, RelationshipColumnsRule>();
builder.Services.AddSingleton<IReviewRule, StarSchemaRelationshipRule>();
builder.Services.AddSingleton<IReviewRule, SingleDirectionRelationshipRule>();
builder.Services.AddSingleton<IReviewRule, ExplicitMeasuresRule>();
builder.Services.AddSingleton<IReviewRule, DaxDivisionRule>();
builder.Services.AddSingleton<IReviewRule, DaxFormatRule>();
builder.Services.AddSingleton<IReviewFeedbackStore>(_ => new SqlReviewFeedbackStore(connectionString));
builder.Services.AddSingleton<IReviewHistoryStore>(_ => new SqlReviewHistoryStore(connectionString));
builder.Services.AddHostedService<ReviewHistoryCleanupService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseAuthentication();
if (entraAuthenticationEnabled)
{
    app.UseMiddleware<ApplicationUserAccessMiddleware>();
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isPublicPath =
            path == "/auth/sign-in" ||
            path == "/auth/sign-out" ||
            path == "/auth/signed-out" ||
            path == "/api/auth/session" ||
            path == "/health/live" ||
            path == "/health/ready" ||
            path == "/signin-oidc" ||
            path == "/signout-callback-oidc";
        if (isPublicPath || context.User.Identity?.IsAuthenticated == true)
        {
            await next();
            return;
        }

        if (path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Sign-in required",
                Detail = "Sign in with an account from the configured Microsoft Entra tenant.",
                Instance = path
            }, context.RequestAborted);
            return;
        }

        var requestPath = context.Request.Path.Value;
        var isLocalPath = requestPath is { Length: > 0 } &&
            requestPath[0] == '/' &&
            (requestPath.Length == 1 || (requestPath[1] != '/' && requestPath[1] != '\\')) &&
            !requestPath.Contains('\\');
        var redirectUri = isLocalPath
            ? string.Concat(
                context.Request.PathBase,
                context.Request.Path,
                context.Request.QueryString)
            : "/";
        await context.ChallengeAsync(
            OpenIdConnectDefaults.AuthenticationScheme,
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = redirectUri
            });
    });
}
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<ReviewModuleAvailabilityMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "BI Sentinel API v1");
    });
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
    .AllowAnonymous()
    .WithTags("Health")
    .WithSummary("Check API liveness")
    .WithDescription("Returns healthy when the API process is running.")
    .Produces(StatusCodes.Status200OK);

app.MapGet(
    "/health/ready",
    async (ILogger<Program> logger, CancellationToken cancellationToken) =>
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return Results.Ok(new { status = "Healthy" });
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Database readiness check failed.");
            return Results.Problem(
                title: "Database unavailable",
                detail: "The application cannot reach its configured database.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .AllowAnonymous()
    .WithTags("Health")
    .WithSummary("Check database readiness")
    .WithDescription("Performs a read-only SQL connectivity check.")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/auth/session",
    (HttpContext httpContext) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        return Results.Ok(new ApplicationAuthSessionResponse(
            entraAuthenticationEnabled,
            httpContext.User.Identity?.IsAuthenticated == true,
            user?.DisplayName,
            user?.EmailAddress,
            user?.IsAdmin ?? false,
            user?.IsBootstrapAdmin ?? false));
    })
    .AllowAnonymous()
    .WithTags("Authentication")
    .WithSummary("Get the current BI Sentinel sign-in and application role")
    .Produces<ApplicationAuthSessionResponse>(StatusCodes.Status200OK);

if (entraAuthenticationEnabled)
{
    app.MapGet(
        "/auth/sign-in",
        (HttpContext httpContext) => httpContext.User.Identity?.IsAuthenticated == true
            ? Results.LocalRedirect("/")
            : Results.Challenge(
                new Microsoft.AspNetCore.Authentication.AuthenticationProperties
                {
                    RedirectUri = "/"
                },
                [OpenIdConnectDefaults.AuthenticationScheme]))
        .AllowAnonymous()
        .WithTags("Authentication")
        .WithSummary("Sign in with Microsoft Entra ID");

    app.MapPost(
        "/auth/sign-out",
        () => Results.SignOut(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = "/auth/signed-out"
            },
            [
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme
            ]))
        .AllowAnonymous()
        .WithTags("Authentication")
        .WithSummary("Sign out of BI Sentinel");

    app.MapGet(
        "/auth/signed-out",
        () => Results.Content(
            """
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Signed out | BI Sentinel</title></head>
            <body style="font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:12vh auto;padding:24px;color:#17243a">
              <h1>You are signed out</h1>
              <p>Your BI Sentinel session has ended.</p>
              <a href="/auth/sign-in">Sign in again</a>
            </body>
            </html>
            """,
            "text/html"))
        .AllowAnonymous()
        .WithTags("Authentication")
        .WithSummary("Show the signed-out confirmation");

    app.MapGet(
        "/api/admin/application-users",
        async (
            IApplicationUserStore userStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await userStore.GetUsersAsync(
                    settings.TenantId,
                    settings.BootstrapAdminObjectId,
                    cancellationToken));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Listing BI Sentinel users failed.");
                return Results.Problem(
                    title: "Application users could not be loaded",
                    detail: "Verify database migration 008 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("ApplicationAdmin")
        .WithTags("Administration")
        .WithSummary("List users who have signed in to BI Sentinel")
        .Produces<IReadOnlyList<ApplicationUserSummary>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    app.MapPut(
        "/api/admin/application-users/{userId:guid}/admin",
        async (
            Guid userId,
            SetApplicationUserAdminRequest request,
            HttpContext httpContext,
            IApplicationUserStore userStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            var actor = ApplicationUserContext.Get(httpContext);
            if (actor is null)
            {
                return Results.Problem(
                    title: "Application identity is unavailable",
                    detail: "Sign in again before changing application roles.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var result = await userStore.SetAdminAsync(
                    settings.TenantId,
                    userId,
                    actor.UserId,
                    request.IsAdmin,
                    settings.BootstrapAdminObjectId,
                    cancellationToken);
                return result switch
                {
                    ApplicationUserRoleChangeResult.UserNotFound => Results.NotFound(),
                    ApplicationUserRoleChangeResult.BootstrapAdminCannotBeDemoted =>
                        Results.Conflict(new { title = "Bootstrap administrator is protected", detail = "The bootstrap administrator cannot be demoted." }),
                    ApplicationUserRoleChangeResult.LastAdminCannotBeDemoted =>
                        Results.Conflict(new { title = "At least one administrator is required", detail = "Assign another administrator before removing this role." }),
                    _ => Results.Ok(new ApplicationUserAdminResponse(userId, request.IsAdmin))
                };
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Changing BI Sentinel application administrator role failed for user {UserId}.", userId);
                return Results.Problem(
                    title: "Application role could not be changed",
                    detail: "Verify database migration 008 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("ApplicationAdmin")
        .WithTags("Administration")
        .WithSummary("Grant or remove the BI Sentinel administrator role")
        .Produces<ApplicationUserAdminResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

}
else
{
    app.MapGet("/auth/sign-in", () => Results.Problem(
        title: "Sign-in is not configured",
        detail: "Configure Microsoft Entra authentication before enabling application sign-in.",
        statusCode: StatusCodes.Status503ServiceUnavailable))
        .AllowAnonymous();
    app.MapPost("/auth/sign-out", () => Results.NoContent()).AllowAnonymous();
}

app.MapGet(
    "/api/admin/review-configuration",
    async (
        IReviewConfigurationStore configurationStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var snapshot = await configurationStore.GetAsync(cancellationToken);
            return Results.Ok(new ReviewConfigurationResponse(
                snapshot.Configuration,
                snapshot.Version,
                snapshot.UpdatedUtc,
                snapshot.UpdatedBy));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading the global review configuration failed.");
            return Results.Problem(
                title: "Review configuration could not be loaded",
                detail: "Apply database migrations 005 and 006 and verify the application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Administration")
    .WithSummary("Get the global review configuration")
    .WithDescription("Returns the active database-backed review rules and standards.")
    .Produces<ReviewConfigurationResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut(
    "/api/admin/review-configuration",
    async (
        UpdateReviewConfigurationRequest request,
        IReviewConfigurationStore configurationStore,
        IEnumerable<IReviewRule> registeredRules,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (request.ExpectedVersion < 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ExpectedVersion)] = ["Expected version must be greater than zero."]
            });
        }

        var validationErrors = ReviewConfigurationValidator.Validate(
            request.Configuration,
            registeredRules.Select(rule => rule.Key));
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        try
        {
            var snapshot = await configurationStore.UpdateAsync(
                request.Configuration,
                request.ExpectedVersion,
                ApplicationUserContext.Get(httpContext)?.EmailAddress
                    ?? httpContext.User.Identity?.Name
                    ?? "BI Sentinel administrator",
                cancellationToken);
            return snapshot is null
                ? Results.Conflict(new { title = "Configuration changed", detail = "Reload the latest configuration before saving your edits." })
                : Results.Ok(new ReviewConfigurationResponse(
                    snapshot.Configuration,
                    snapshot.Version,
                    snapshot.UpdatedUtc,
                    snapshot.UpdatedBy));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Updating the global review configuration failed.");
            return Results.Problem(
                title: "Review configuration could not be saved",
                detail: "Verify database migration 005 and the application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("ApplicationAdmin")
    .WithTags("Administration")
    .WithSummary("Update the global review configuration")
    .WithDescription("Validates and atomically saves review rules and standards; updates are recorded in configuration audit history.")
    .Produces<ReviewConfigurationResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

if (!governanceAuditEnabled || !entraAuthenticationEnabled)
{
    static IResult GovernanceAuditUnavailable() => Results.Problem(
        title: "Governance Audit is not configured",
        detail: "Enable GovernanceAudit:Enabled and configure Microsoft Entra sign-in before accessing Power BI inventory.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    app.MapGet("/api/governance-audit/configuration", GovernanceAuditUnavailable);
    app.MapPut("/api/governance-audit/configuration", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/workspaces", GovernanceAuditUnavailable);
    app.MapPost("/api/governance-audit/scan", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/runs", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/signin", GovernanceAuditUnavailable);
}
else
{
    app.MapGet(
        "/api/governance-audit/signin",
        () =>
        {
            var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = "/modules/governance-audit"
            };
            properties.Items["PowerBiConsent"] = "true";
            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .RequireAuthorization("GovernanceAuditAdmin")
        .WithTags("Governance Audit")
        .WithSummary("Request delegated read access to Power BI");

    app.MapGet(
        "/api/governance-audit/configuration",
        async (
            IGovernanceAuditConfigurationStore configurationStore,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await configurationStore.GetAsync(cancellationToken));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading Governance Audit configuration failed.");
                return Results.Problem(
                    title: "Governance Audit configuration could not be loaded",
                    detail: "Apply database migration 007 and verify application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditAdmin")
        .WithTags("Governance Audit")
        .WithSummary("Get the approved Governance Audit workspace scope");

    app.MapPut(
        "/api/governance-audit/configuration",
        async (
            UpdateGovernanceAuditConfigurationRequest request,
            IGovernanceAuditConfigurationStore configurationStore,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request.ExpectedVersion < 1)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.ExpectedVersion)] = ["Expected version must be greater than zero."]
                });
            }

            if (request.WorkspaceIds is null ||
                request.WorkspaceIds.Length > maximumGovernanceWorkspaces ||
                request.WorkspaceIds.Any(id => id == Guid.Empty) ||
                request.WorkspaceIds.Distinct().Count() != request.WorkspaceIds.Length)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.WorkspaceIds)] =
                    [$"Provide no more than {maximumGovernanceWorkspaces} distinct, non-empty workspace IDs."]
                });
            }

            try
            {
                var updated = await configurationStore.UpdateAsync(
                    request.WorkspaceIds,
                    request.ExpectedVersion,
                    httpContext.User.Identity?.Name ?? "Entra Governance Audit administrator",
                    cancellationToken);
                return updated is null
                    ? Results.Conflict(new { title = "Configuration changed", detail = "Reload the approved workspace scope before saving." })
                    : Results.Ok(updated);
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Saving Governance Audit configuration failed.");
                return Results.Problem(
                    title: "Governance Audit configuration could not be saved",
                    detail: "Verify database migration 007 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditAdmin")
        .WithTags("Governance Audit")
        .WithSummary("Save the approved Governance Audit workspace scope");

    app.MapGet(
        "/api/governance-audit/workspaces",
        async (
            PowerBiGovernanceAuditClient auditClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await auditClient.DiscoverWorkspacesAsync(
                    httpContext.User,
                    cancellationToken));
            }
            catch (PowerBiAuditException exception)
            {
                app.Logger.LogWarning(exception, "Power BI workspace discovery failed with HTTP {StatusCode}.", (int)exception.StatusCode);
                return Results.Problem(
                    title: "Power BI workspace discovery failed",
                    detail: exception.Message,
                    statusCode: exception.RequiresConsent
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization("GovernanceAuditAdmin")
        .WithTags("Governance Audit")
        .WithSummary("Discover workspaces visible to the configured Power BI identity");

    app.MapGet(
        "/api/governance-audit/runs",
        async (
            int? limit,
            IGovernanceAuditHistoryStore historyStore,
            CancellationToken cancellationToken) =>
        {
            var requestedLimit = limit ?? 20;
            if (requestedLimit is < 1 or > 100)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(limit)] = ["Limit must be between 1 and 100."]
                });
            }

            try
            {
                return Results.Ok(await historyStore.GetRecentAsync(requestedLimit, cancellationToken));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading recent Governance Audit runs failed.");
                return Results.Problem(
                    title: "Governance Audit history could not be loaded",
                    detail: "Apply database migration 007 and verify application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("List recent saved Governance Audit runs");

    app.MapGet(
        "/api/governance-audit/runs/{runId:guid}",
        async (
            Guid runId,
            IGovernanceAuditHistoryStore historyStore,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await historyStore.GetAsync(runId, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading saved Governance Audit run {RunId} failed.", runId);
                return Results.Problem(
                    title: "Governance Audit run could not be loaded",
                    detail: "Verify database migration 007 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("Load a saved Governance Audit run");

    app.MapPost(
        "/api/governance-audit/scan",
        async (
            IGovernanceAuditConfigurationStore configurationStore,
            IGovernanceAuditHistoryStore historyStore,
            PowerBiGovernanceAuditClient auditClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            GovernanceAuditConfiguration configuration;
            try
            {
                configuration = await configurationStore.GetAsync(cancellationToken);
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(
                    exception,
                    "Loading the Governance Audit scan scope failed with SQL error {SqlErrorNumber}.",
                    exception.Number);
                return Results.Problem(
                    title: "Governance Audit scope could not be loaded",
                    detail: $"The approved workspace scope could not be read (SQL error {exception.Number}). Check that the application is connected to SmartAgent and review the server log.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (configuration.WorkspaceIds.Length == 0)
            {
                return Results.Conflict(new
                {
                    title = "No workspaces selected",
                    detail = "Discover workspaces and save an approved scope in Governance Audit Administration before scanning."
                });
            }

            GovernanceAuditResult result;
            try
            {
                result = await auditClient.ScanAsync(
                    configuration.WorkspaceIds,
                    httpContext.User,
                    cancellationToken);
            }
            catch (PowerBiAuditException exception)
            {
                app.Logger.LogWarning(exception, "Power BI governance scan failed with HTTP {StatusCode}.", (int)exception.StatusCode);
                return Results.Problem(
                    title: "Power BI governance scan failed",
                    detail: exception.Message,
                    statusCode: exception.RequiresConsent
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status502BadGateway);
            }

            var scannedBy = httpContext.User.Identity?.Name
                ?? "Entra Governance Audit reviewer";
            try
            {
                var runId = await historyStore.SaveAsync(result, scannedBy, cancellationToken);
                return Results.Ok(result with { RunId = runId });
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(
                    exception,
                    "Saving Governance Audit scan results failed with SQL error {SqlErrorNumber}.",
                    exception.Number);
                return Results.Problem(
                    title: "Governance Audit results could not be saved",
                    detail: $"Power BI was read, but its results could not be saved (SQL error {exception.Number}). This does not necessarily mean migration 007 is missing; review the server log and confirm the application is connected to SmartAgent.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("Run a read-only audit of approved Power BI workspaces");
}

app.MapPost(
    "/api/reviews/upload",
    async (
        IFormFile file,
        string? projectId,
        PbipProjectInputService projectInput,
        IReviewHistoryStore historyStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var review = await projectInput.ReviewArchiveAsync(file, projectId, cancellationToken);
            return await SaveReviewAsync(
                review,
                historyStore,
                reviewHistoryRetentionDays,
                logger,
                cancellationToken);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading review configuration or saving the review failed.");
            return Results.Problem(
                title: "Review could not be saved",
                detail: "Verify database migrations 002, 005, and 006 and the application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException exception)
        {
            logger.LogWarning(exception, "A configured review regular expression exceeded its time limit.");
            return Results.Problem(
                title: "Review configuration requires adjustment",
                detail: "A configured naming pattern took too long to evaluate. Adjust the pattern in Administration and retry.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Uploaded Power BI project could not be reviewed.");
            return Results.Problem(
                title: "Uploaded Power BI project is invalid or unsupported",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    })
    .WithTags("Reviews")
    .WithSummary("Upload and review a Power BI project")
    .WithDescription("Accepts PBIT files or a ZIP containing one PBIP/PBIR project, up to 500 MB. PBIX files are not supported. Extraction is temporary and extracted files are removed after review.")
    .WithMetadata(new RequestSizeLimitAttribute(MaximumUploadRequestBytes))
    .WithMetadata(new RequestFormLimitsAttribute
    {
        MultipartBodyLengthLimit = MaximumUploadRequestBytes
    })
    .DisableAntiforgery()
    .Produces<SaveReviewHistoryResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/history",
    async (
        int? limit,
        string? projectName,
        IReviewHistoryStore historyStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var resultLimit = limit ?? 100;
        if (resultLimit is < 1 or > 500)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["Limit must be between 1 and 500."]
            });
        }

        var searchName = string.IsNullOrWhiteSpace(projectName) ? null : projectName.Trim();
        if (searchName?.Length > 400)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(projectName)] = ["Project name search must be 400 characters or fewer."]
            });
        }

        try
        {
            return Results.Ok(await historyStore.GetRecentAsync(
                resultLimit,
                searchName,
                cancellationToken));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading review history from SQL Server failed.");
            return Results.Problem(
                title: "Review history could not be loaded",
                detail: "Verify the review-history migration and database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("Search saved review history")
    .WithDescription("Searches unexpired review summaries by project name, up to 500 entries.")
    .Produces<IReadOnlyList<ReviewHistorySummary>>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/history/{reviewRunId:guid}",
    async (
        Guid reviewRunId,
        IReviewHistoryStore historyStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var entry = await historyStore.GetAsync(reviewRunId, cancellationToken);
            return entry is null ? Results.NotFound() : Results.Ok(entry);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading review history entry from SQL Server failed.");
            return Results.Problem(
                title: "Review history could not be loaded",
                detail: "Verify the review-history migration and database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("Load a saved review")
    .WithDescription("Returns an unexpired review summary and its findings.")
    .Produces<ReviewHistoryEntry>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/reviews/feedback",
    async (
        SaveReviewFeedbackRequest request,
        IReviewFeedbackStore feedbackStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var validationErrors = ReviewFeedbackValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (!feedbackSqlEnabled)
        {
            return Results.Problem(
                title: "SQL feedback storage is disabled",
                detail: "Enable Feedback:SqlEnabled only after applying the feedback database migration.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var savedUtc = await feedbackStore.SaveAsync(request, cancellationToken);
            if (savedUtc is null)
            {
                return Results.NotFound(new
                {
                    title = "Review finding not found",
                    detail = "The finding may have expired or does not belong to a saved review."
                });
            }

            return Results.Ok(new SaveReviewFeedbackResponse(true, savedUtc.Value));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Saving review feedback to SQL Server failed.");
            return Results.Problem(
                title: "Feedback could not be saved",
                detail: "Verify the feedback table migration and database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("Save pilot feedback")
    .WithDescription("Creates or updates feedback for one finding in SQL Server.")
    .Produces<SaveReviewFeedbackResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/reviews/history/{reviewRunId:guid}/reopen",
    async (
        Guid reviewRunId,
        ReopenReviewFeedbackRequest request,
        IReviewFeedbackStore feedbackStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (request.ReviewerId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ReviewerId)] = ["Reviewer ID must be a non-empty browser-generated identifier."]
            });
        }

        if (!feedbackSqlEnabled)
        {
            return Results.Problem(
                title: "SQL feedback storage is disabled",
                detail: "Enable Feedback:SqlEnabled only after applying the feedback database migration.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var updatedUtc = await feedbackStore.ReopenRunAsync(
                request.ReviewerId,
                reviewRunId,
                cancellationToken);
            return updatedUtc is null
                ? Results.NotFound(new
                {
                    title = "Saved review not found",
                    detail = "The review may have expired or could not be reopened."
                })
                : Results.Ok(new SaveReviewFeedbackResponse(true, updatedUtc.Value));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Reopening a saved review in SQL Server failed.");
            return Results.Problem(
                title: "Review could not be reopened",
                detail: "Verify feedback migration 004 and the database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("Reopen a submitted review")
    .WithDescription("Reopens all submitted feedback in a saved review for the current browser reviewer.")
    .Produces<SaveReviewFeedbackResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/reviews/feedback/{reviewFindingId:guid}/reopen",
    async (
        Guid reviewFindingId,
        ReopenReviewFeedbackRequest request,
        IReviewFeedbackStore feedbackStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (request.ReviewerId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ReviewerId)] = ["Reviewer ID must be a non-empty browser-generated identifier."]
            });
        }

        if (!feedbackSqlEnabled)
        {
            return Results.Problem(
                title: "SQL feedback storage is disabled",
                detail: "Enable Feedback:SqlEnabled only after applying the feedback database migration.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var updatedUtc = await feedbackStore.ReopenAsync(
                request.ReviewerId,
                reviewFindingId,
                cancellationToken);
            return updatedUtc is null
                ? Results.NotFound(new
                {
                    title = "Submitted feedback not found",
                    detail = "The feedback may not exist or its review finding may have expired."
                })
                : Results.Ok(new SaveReviewFeedbackResponse(true, updatedUtc.Value));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Reopening review feedback in SQL Server failed.");
            return Results.Problem(
                title: "Feedback could not be reopened",
                detail: "Verify feedback migration 004 and the database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("Reopen submitted feedback")
    .WithDescription("Returns this review finding to the waiting state for the current browser reviewer.")
    .Produces<SaveReviewFeedbackResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/history/{reviewRunId:guid}/feedback",
    async (
        Guid reviewRunId,
        Guid reviewerId,
        IReviewFeedbackStore feedbackStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (reviewerId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(reviewerId)] = ["Reviewer ID must be a non-empty browser-generated identifier."]
            });
        }

        if (!feedbackSqlEnabled)
        {
            return Results.Problem(
                title: "SQL feedback storage is disabled",
                detail: "Enable Feedback:SqlEnabled only after applying the feedback database migration.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            return Results.Ok(await feedbackStore.GetForRunAsync(
                reviewerId,
                reviewRunId,
                cancellationToken));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading feedback for a saved review from SQL Server failed.");
            return Results.Problem(
                title: "Saved review feedback could not be loaded",
                detail: "Verify the feedback and review-history migrations and SQL permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("Load feedback for one saved review")
    .WithDescription("Returns this browser reviewer's submitted responses for exact findings in one saved run.")
    .Produces<IReadOnlyList<ReviewFeedbackEntry>>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/feedback",
    async (
        int? limit,
        IReviewFeedbackStore feedbackStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (!feedbackSqlEnabled)
        {
            return Results.Problem(
                title: "SQL feedback storage is disabled",
                detail: "Enable Feedback:SqlEnabled only after applying the feedback database migration.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var resultLimit = limit ?? 200;
        if (resultLimit is < 1 or > 1000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["Limit must be between 1 and 1000."]
            });
        }

        try
        {
            return Results.Ok(await feedbackStore.GetRecentAsync(resultLimit, cancellationToken));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Reading review feedback from SQL Server failed.");
            return Results.Problem(
                title: "Feedback could not be loaded",
                detail: "Verify the feedback table migration and database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("List recent pilot feedback")
    .WithDescription("Returns a bounded list of recent team feedback.")
    .Produces<IReadOnlyList<ReviewFeedbackEntry>>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/dashboard",
    async (
        Guid reviewerId,
        int? limit,
        IReviewFeedbackStore feedbackStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (reviewerId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(reviewerId)] = ["Reviewer ID must be a non-empty browser-generated identifier."]
            });
        }

        var resultLimit = limit ?? 500;
        if (resultLimit is < 1 or > 1000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = ["Limit must be between 1 and 1000."]
            });
        }

        if (!feedbackSqlEnabled)
        {
            return Results.Problem(
                title: "SQL feedback storage is disabled",
                detail: "Enable Feedback:SqlEnabled only after applying the feedback database migration.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            return Results.Ok(await feedbackStore.GetDashboardAsync(
                reviewerId,
                resultLimit,
                cancellationToken));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading the review dashboard from SQL Server failed.");
            return Results.Problem(
                title: "Review dashboard could not be loaded",
                detail: "Verify review-history migrations and the app's SQL permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Reviews")
    .WithSummary("List waiting and closed review findings")
    .WithDescription("Lists unexpired findings and this browser reviewer's submission status.")
    .Produces<IReadOnlyList<ReviewDashboardRun>>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapFallbackToFile("index.html");
app.Run();

static bool IsValidRegex(string pattern)
{
    try
    {
        _ = new System.Text.RegularExpressions.Regex(pattern);
        return true;
    }

    catch (ArgumentException)
    {
        return false;
    }
}

static bool IsValidProjectId(string? projectId) =>
    !string.IsNullOrWhiteSpace(projectId) &&
    System.Text.RegularExpressions.Regex.IsMatch(
        projectId,
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$");

static async Task<IResult> SaveReviewAsync(
    PbipReviewResult review,
    IReviewHistoryStore historyStore,
    int retentionDays,
    ILogger<Program> logger,
    CancellationToken cancellationToken)
{
    try
    {
        var entry = await historyStore.SaveAsync(review, retentionDays, cancellationToken);
        return Results.Ok(new SaveReviewHistoryResponse(
            entry.Summary.ReviewRunId,
            entry.Summary.ReviewedUtc,
            entry.Summary.ExpiresUtc,
            review with { ProjectName = entry.Summary.ProjectName },
            entry.Findings));
    }
    catch (SqlException exception)
    {
        logger.LogError(exception, "Saving review history to SQL Server failed.");
        return Results.Problem(
            title: "Review completed but history could not be saved",
            detail: "Verify the review-history migration and database permissions, then retry.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
