using BISentinel.Api.Feedback;
using BISentinel.Api.GovernanceAudit;
using BISentinel.Api.PerformanceOptimization;
using BISentinel.Api.Portal;
using BISentinel.Api.Reviews;
using BISentinel.Api.ReviewHistory;
using BISentinel.Api.Security;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using ModelContextProtocol.AspNetCore;

const long MaximumUploadRequestBytes = 501L * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var detail = context.ProblemDetails.Detail;
        var isDatabaseFailure =
            detail?.Contains("database", StringComparison.OrdinalIgnoreCase) == true ||
            detail?.Contains("migration", StringComparison.OrdinalIgnoreCase) == true ||
            detail?.Contains("SQL permission", StringComparison.OrdinalIgnoreCase) == true;
        if (!builder.Environment.IsDevelopment() && isDatabaseFailure)
        {
            context.ProblemDetails.Detail =
                "A database operation failed. Verify database connectivity, required database migrations/scripts, and application database permissions.";
        }
    };
});
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
            var existingRemoteFailureHandler = options.Events.OnRemoteFailure;
            options.Events.OnRedirectToIdentityProvider = async context =>
            {
                if (existingRedirectHandler is not null)
                {
                    await existingRedirectHandler(context);
                }

                if (context.Properties.Items.TryGetValue("PowerBiProjectReviewConsent", out var projectReviewConsent) &&
                    string.Equals(projectReviewConsent, "true", StringComparison.Ordinal))
                {
                    var scopes = context.ProtocolMessage.Scope
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Concat(PowerBiOnlineReviewClient.RequiredScopes)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    context.ProtocolMessage.Scope = string.Join(' ', scopes);
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
            options.Events.OnRemoteFailure = async context =>
            {
                if (existingRemoteFailureHandler is not null)
                {
                    await existingRemoteFailureHandler(context);
                }

                if (context.Response.HasStarted)
                {
                    return;
                }

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("EntraOpenIdConnect");
                logger.LogWarning(
                    context.Failure,
                    "Entra remote sign-in failed on {Method} {Path} using {Scheme}://{Host} with content type {ContentType}.",
                    context.Request.Method,
                    context.Request.Path,
                    context.Request.Scheme,
                    context.Request.Host,
                    context.Request.ContentType ?? "(none)");

                context.HandleResponse();
                context.Response.Redirect("/auth/error?sign-in=true");
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
    options.AddPolicy("HierarchyManager", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("ApplicationAdmin", "OrgLead", "ProjectLead", "TeamLead"));
    options.AddPolicy("GovernanceAuditReader", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("ApplicationAdmin"));
    options.AddPolicy("GovernanceAuditAdmin", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("ApplicationAdmin"));
});

if (governanceAuditEnabled)
{
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true)
        .AddAuthorizationFilters()
        .WithTools<PowerBiGovernanceMcpTools>()
        .WithResources<PowerBiGovernanceMcpResources>()
        .WithPrompts<PowerBiGovernanceMcpPrompts>();
}

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
builder.Services.AddSingleton<IGovernanceExceptionStore>(_ =>
    new SqlGovernanceExceptionStore(connectionString));
builder.Services.AddScoped<IPowerBiDelegatedAccessTokenProvider, PowerBiDelegatedAccessTokenProvider>();
builder.Services.AddHttpClient<PowerBiOnlineReviewClient>(client =>
{
    client.BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<PowerBiGovernanceAuditClient>(client =>
{
    client.BaseAddress = new Uri("https://api.powerbi.com/v1.0/myorg/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<PbipProjectInputService>();
builder.Services.AddSingleton<PerformanceAssessmentService>();
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
builder.Services.AddSingleton<IReviewRule, UnusedTablesRule>();
builder.Services.AddSingleton<IReviewRule, UnusedColumnsRule>();
builder.Services.AddSingleton<IReviewRule, UnusedMeasuresRule>();
builder.Services.AddSingleton<IReviewFeedbackStore>(_ => new SqlReviewFeedbackStore(connectionString));
builder.Services.AddSingleton<IReviewSubmissionStore>(_ => new SqlReviewSubmissionStore(connectionString));
builder.Services.AddSingleton<IReviewHistoryStore>(_ => new SqlReviewHistoryStore(connectionString));
builder.Services.AddSingleton<IPortalDataStore>(_ => new SqlPortalDataStore(connectionString));
builder.Services.AddSingleton<IOrganizationHierarchyStore>(_ =>
    new SqlOrganizationHierarchyStore(connectionString));
builder.Services.AddSingleton<IAccessRequestStore>(_ =>
    new SqlAccessRequestStore(connectionString));
builder.Services.AddSingleton<IAccessRequestEmailSender, SmtpAccessRequestEmailSender>();
builder.Services.AddHostedService<ReviewHistoryCleanupService>();
builder.Services.AddHostedService<AccessRequestNotificationService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnhandledRequest");
        logger.LogError(exception, "Unhandled request failure. Reference ID {ReferenceId}.", context.TraceIdentifier);

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/mcp") ||
            context.Request.Headers.Accept.Any(value => value?.Contains(
                "application/json",
                StringComparison.OrdinalIgnoreCase) == true))
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong",
                Detail = "BI Sentinel could not complete that request. Try again or contact your administrator."
            };
            problem.Extensions["referenceId"] = context.TraceIdentifier;
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
            return;
        }

        var webRoot = app.Environment.WebRootPath ??
            Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var errorPage = await File.ReadAllTextAsync(
            Path.Combine(webRoot, "error.html"),
            context.RequestAborted);
        errorPage = errorPage.Replace(
            "{{REFERENCE_ID}}",
            HtmlEncoder.Default.Encode(context.TraceIdentifier),
            StringComparison.Ordinal);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(errorPage, context.RequestAborted);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
if (entraAuthenticationEnabled)
{
    app.UseMiddleware<ApplicationUserAccessMiddleware>();
    app.UseMiddleware<WorkspaceScopeMiddleware>();
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isPublicPath =
            path == "/auth/sign-in" ||
            path == "/auth/continue" ||
            path == "/auth/error" ||
            path == "/auth/sign-out" ||
            path == "/auth/signed-out" ||
            path == "/api/auth/session" ||
            path == "/auth.css" ||
            path == "/styles.css" ||
            path == "/brand-mark.svg" ||
            path == "/health/live" ||
            path == "/health/ready" ||
            path == "/signin-oidc" ||
            path == "/signout-callback-oidc";
        if (isPublicPath || context.User.Identity?.IsAuthenticated == true)
        {
            await next();
            return;
        }

        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/mcp"))
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
        context.Response.Redirect(
            $"/auth/sign-in?returnUrl={Uri.EscapeDataString(redirectUri)}");
    });
}
app.UseStaticFiles();
if (governanceAuditEnabled)
{
    app.UseMiddleware<McpRequestAuditMiddleware>();
}
app.UseAuthorization();
app.UseDefaultFiles();
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
            user?.IsManager ?? false,
            user?.IsLead ?? false,
            user?.IsBootstrapAdmin ?? false)
        {
            IsOrgLead = user?.IsOrgLead ?? false,
            IsProjectLead = user?.IsProjectLead ?? false,
            IsTeamLead = user?.IsTeamLead ?? false,
            AccessGranted = user?.IsEnabled ?? false
        });
    })
    .AllowAnonymous()
    .WithTags("Authentication")
    .WithSummary("Get the current BI Sentinel sign-in and application role")
    .Produces<ApplicationAuthSessionResponse>(StatusCodes.Status200OK);

app.MapGet(
    "/api/workspace/scopes",
    async (
        HttpContext httpContext,
        IOrganizationHierarchyStore hierarchyStore,
        ApplicationAuthenticationSettings settings,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var snapshot = await hierarchyStore.GetSnapshotAsync(
                settings.TenantId,
                user.UserId,
                settings.BootstrapAdminObjectId,
                user.IsAdmin,
                cancellationToken);
            return Results.Ok(new WorkspaceScopesResponse(
                WorkspaceScopeOptionsBuilder.Build(snapshot, user.UserId, user.IsAdmin),
                user.IsAdmin));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading available workspace scopes failed.");
            return Results.Problem(
                title: "Workspace scopes could not be loaded",
                detail: ApplicationDatabaseError.GetDetail(
                    exception,
                    app.Environment,
                    "Verify that hierarchy migration 019 and its database permissions are applied."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Workspace")
    .WithSummary("List the organizations, projects, and teams available to the signed-in user")
    .Produces<WorkspaceScopesResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/workspace/context",
    async (
        HttpContext httpContext,
        IOrganizationHierarchyStore hierarchyStore,
        ApplicationAuthenticationSettings settings,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var snapshot = await hierarchyStore.GetSnapshotAsync(
                settings.TenantId,
                user.UserId,
                settings.BootstrapAdminObjectId,
                user.IsAdmin,
                cancellationToken);
            return Results.Ok(BuildWorkspaceContext(
                snapshot,
                ActiveWorkspaceScope.Get(httpContext),
                user.IsAdmin));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading active workspace users failed.");
            return Results.Problem(
                title: "Workspace users could not be loaded",
                detail: ApplicationDatabaseError.GetDetail(
                    exception,
                    app.Environment,
                    "Verify that hierarchy migration 019 and its database permissions are applied."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Workspace")
    .WithSummary("List users associated with the selected organization, project, or team")
    .Produces<WorkspaceContextResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/access-requests/scopes",
    async (IAccessRequestStore store, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await store.GetScopesAsync(cancellationToken));
        }
        catch (SqlException exception)
        {
            app.Logger.LogError(exception, "Loading access-request scopes failed.");
            return Results.Problem(
                title: "Access scopes could not be loaded",
                detail: "Verify database migration 020 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Access Requests")
    .WithSummary("List available organization, project, and team access scopes")
    .Produces<IReadOnlyList<AccessRequestScopeOption>>(StatusCodes.Status200OK);

app.MapGet(
    "/api/access-requests/mine",
    async (
        HttpContext httpContext,
        IAccessRequestStore store,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await store.GetMyRequestsAsync(user.UserId, cancellationToken));
        }
        catch (SqlException exception)
        {
            app.Logger.LogError(exception, "Loading the user's access requests failed.");
            return Results.Problem(
                title: "Your access requests could not be loaded",
                detail: "Verify database migration 020 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Access Requests")
    .WithSummary("List access requests submitted by the signed-in user")
    .Produces<IReadOnlyList<AccessRequestRecord>>(StatusCodes.Status200OK);

app.MapPost(
    "/api/access-requests",
    async (
        CreateAccessRequestRequest request,
        HttpContext httpContext,
        IAccessRequestStore store,
        ApplicationAuthenticationSettings settings,
        CancellationToken cancellationToken) =>
    {
        if (request.ScopeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ScopeType) ||
            string.IsNullOrWhiteSpace(request.RoleName) ||
            request.Message?.Length > 2000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["scope"] = request.ScopeId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(request.ScopeType) ||
                    string.IsNullOrWhiteSpace(request.RoleName)
                        ? ["Choose an access scope and role."]
                        : [],
                ["message"] = request.Message?.Length > 2000
                    ? ["The optional message must be 2,000 characters or fewer."]
                    : []
            }.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await store.CreateAsync(
                settings.TenantId,
                user.UserId,
                request.ScopeType,
                request.ScopeId,
                request.RoleName,
                request.Message,
                cancellationToken);
            return result switch
            {
                AccessRequestMutationResult.Updated => Results.Accepted("/api/access-requests/mine", new
                {
                    status = "Pending",
                    detail = "Your request was submitted. A scoped lead will review it."
                }),
                AccessRequestMutationResult.Duplicate => Results.Conflict(new
                {
                    detail = "A pending request already exists for this scope and role."
                }),
                AccessRequestMutationResult.Invalid => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["roleName"] = ["Choose a valid role for the selected scope."]
                }),
                AccessRequestMutationResult.NotFound => Results.NotFound(new
                {
                    detail = "The selected organization, project, or team is unavailable."
                }),
                _ => Results.Forbid()
            };
        }
        catch (SqlException exception)
        {
            app.Logger.LogError(exception, "Submitting a hierarchy access request failed.");
            return Results.Problem(
                title: "Access request could not be submitted",
                detail: "Verify database migration 020 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Access Requests")
    .WithSummary("Request access to an organization, project, or team")
    .Produces(StatusCodes.Status202Accepted)
    .Produces(StatusCodes.Status409Conflict)
    .ProducesValidationProblem();

app.MapGet(
    "/api/access-requests/inbox",
    async (
        HttpContext httpContext,
        IAccessRequestStore store,
        ApplicationAuthenticationSettings settings,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var accessSettings = await store.GetSettingsAsync(cancellationToken);
            return Results.Ok(await store.GetInboxAsync(
                settings.TenantId,
                user.UserId,
                user.IsAdmin,
                accessSettings.EscalationDays,
                cancellationToken));
        }
        catch (SqlException exception)
        {
            app.Logger.LogError(exception, "Loading the access-request approval inbox failed.");
            return Results.Problem(
                title: "Access request inbox could not be loaded",
                detail: "Verify database migration 020 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("HierarchyManager")
    .WithTags("Access Requests")
    .WithSummary("List access requests available to the signed-in scoped lead")
    .Produces<IReadOnlyList<AccessRequestRecord>>(StatusCodes.Status200OK);

app.MapPost(
    "/api/access-requests/{accessRequestId:guid}/decision",
    async (
        Guid accessRequestId,
        AccessRequestDecision request,
        HttpContext httpContext,
        IAccessRequestStore store,
        ApplicationAuthenticationSettings settings,
        CancellationToken cancellationToken) =>
    {
        if (request.Message?.Length > 2000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["message"] = ["The optional message must be 2,000 characters or fewer."]
            });
        }

        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var accessSettings = await store.GetSettingsAsync(cancellationToken);
            var result = await store.DecideAsync(
                settings.TenantId,
                user.UserId,
                accessRequestId,
                user.IsAdmin,
                request.Decision,
                request.Message,
                accessSettings.EscalationDays,
                cancellationToken);
            return result switch
            {
                AccessRequestMutationResult.Updated => Results.Ok(new { accessRequestId, request.Decision }),
                AccessRequestMutationResult.NotFound => Results.NotFound(),
                AccessRequestMutationResult.AlreadyResolved => Results.Conflict(new
                {
                    detail = "This request has already been resolved."
                }),
                AccessRequestMutationResult.Invalid => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["decision"] = ["Choose Approve or Decline."]
                }),
                AccessRequestMutationResult.Forbidden => Results.Forbid(),
                _ => Results.Problem(
                    title: "Access request could not be resolved",
                    statusCode: StatusCodes.Status500InternalServerError)
            };
        }
        catch (SqlException exception)
        {
            app.Logger.LogError(exception, "Resolving access request {AccessRequestId} failed.", accessRequestId);
            return Results.Problem(
                title: "Access request could not be resolved",
                detail: "Verify database migration 020 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("HierarchyManager")
    .WithTags("Access Requests")
    .WithSummary("Approve or decline a scoped access request")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict)
    .ProducesValidationProblem();

app.MapGet(
    "/api/admin/access-requests/settings",
    async (IAccessRequestStore store, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await store.GetSettingsAsync(cancellationToken));
        }
        catch (SqlException exception)
        {
            app.Logger.LogError(exception, "Loading access-request escalation settings failed.");
            return Results.Problem(
                title: "Access request settings could not be loaded",
                detail: "Verify database migration 020 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("ApplicationAdmin")
    .WithTags("Administration")
    .WithSummary("Get the access-request escalation interval")
    .Produces<AccessRequestSettings>(StatusCodes.Status200OK);

app.MapPut(
    "/api/admin/access-requests/settings",
    async (
        UpdateAccessRequestSettingsRequest request,
        HttpContext httpContext,
        IAccessRequestStore store,
        CancellationToken cancellationToken) =>
    {
        if (request.EscalationDays is < 1 or > 30)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.EscalationDays)] = ["Choose an escalation interval from 1 to 30 days."]
            });
        }

        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            await store.SetSettingsAsync(user.UserId, request.EscalationDays, cancellationToken);
            return Results.Ok(new AccessRequestSettings(request.EscalationDays));
        }
        catch (SqlException exception)
        {
            app.Logger.LogError(exception, "Saving access-request escalation settings failed.");
            return Results.Problem(
                title: "Access request settings could not be saved",
                detail: "Verify database migration 020 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("ApplicationAdmin")
    .WithTags("Administration")
    .WithSummary("Set the access-request escalation interval")
    .Produces<AccessRequestSettings>(StatusCodes.Status200OK)
    .ProducesValidationProblem();

app.MapGet(
    "/api/dashboard/summary",
    async (
        HttpContext httpContext,
        IPortalDataStore portalStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await portalStore.GetDashboardSummaryAsync(
                user.UserId,
                user.IsAdmin,
                ActiveWorkspaceScope.Get(httpContext),
                cancellationToken));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading the portal dashboard summary failed.");
            return Results.Problem(
                title: "Dashboard summary could not be loaded",
                detail: ApplicationDatabaseError.GetDetail(
                    exception,
                    app.Environment,
                    "Verify that the review-history, application-access, governance, review-submission, and support database scripts are applied and the application has the required permissions."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Portal")
    .WithSummary("Get dashboard counts for the active organization, project, or team")
    .Produces<PortalDashboardSummary>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/support/requests",
    async (
        HttpContext httpContext,
        IPortalDataStore portalStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await portalStore.GetSupportRequestsAsync(
                user.UserId,
                user.IsAdmin,
                ActiveWorkspaceScope.Get(httpContext),
                cancellationToken));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading BI Sentinel support requests failed.");
            return Results.Problem(
                title: "Support requests could not be loaded",
                detail: ApplicationDatabaseError.GetDetail(
                    exception,
                    app.Environment,
                    "Verify that the support-request database script is applied and the application has the required permissions."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Support")
    .WithSummary("List the current user's support requests or the administrator queue")
    .Produces<IReadOnlyList<SupportRequestRecord>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/support/requests",
    async (
        CreateSupportRequest request,
        HttpContext httpContext,
        IPortalDataStore portalStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var category = request.Category?.Trim();
        var subject = request.Subject?.Trim();
        var message = request.Message?.Trim();
        if (category is not ("Technical issue" or "Access request" or "Feature request" or "Other") ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 200 ||
            string.IsNullOrWhiteSpace(message) || message.Length > 4000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["category"] = category is not ("Technical issue" or "Access request" or "Feature request" or "Other")
                    ? ["Choose a valid support category."]
                    : [],
                ["subject"] = string.IsNullOrWhiteSpace(subject) || subject.Length > 200
                    ? ["Enter a subject between 1 and 200 characters."]
                    : [],
                ["message"] = string.IsNullOrWhiteSpace(message) || message.Length > 4000
                    ? ["Enter a message between 1 and 4,000 characters."]
                    : []
            }.Where(pair => pair.Value.Length > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        try
        {
            var supportRequestId = await portalStore.CreateSupportRequestAsync(
                user.UserId,
                new CreateSupportRequest(category, subject, message),
                ActiveWorkspaceScope.Get(httpContext),
                cancellationToken);
            return Results.Ok(new { supportRequestId });
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Creating a BI Sentinel support request failed.");
            return Results.Problem(
                title: "Support request could not be sent",
                detail: ApplicationDatabaseError.GetDetail(
                    exception,
                    app.Environment,
                    "Verify that the support-request database script is applied and the application has the required permissions."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Support")
    .WithSummary("Create a support request")
    .Produces(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/support/requests/{supportRequestId:guid}/response",
    async (
        Guid supportRequestId,
        RespondToSupportRequest request,
        HttpContext httpContext,
        IPortalDataStore portalStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = request.Response?.Trim();
        var status = request.Status?.Trim();
        if (string.IsNullOrWhiteSpace(response) || response.Length > 4000 ||
            status is not ("Open" or "In progress" or "Resolved"))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["response"] = string.IsNullOrWhiteSpace(response) || response.Length > 4000
                    ? ["Enter an administrator response between 1 and 4,000 characters."]
                    : [],
                ["status"] = status is not ("Open" or "In progress" or "Resolved")
                    ? ["Choose Open, In progress, or Resolved."]
                    : []
            }.Where(pair => pair.Value.Length > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        try
        {
            var updated = await portalStore.RespondToSupportRequestAsync(
                supportRequestId,
                user.UserId,
                new RespondToSupportRequest(response, status),
                ActiveWorkspaceScope.Get(httpContext),
                cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Responding to BI Sentinel support request {SupportRequestId} failed.", supportRequestId);
            return Results.Problem(
                title: "Support request response could not be saved",
                detail: ApplicationDatabaseError.GetDetail(
                    exception,
                    app.Environment,
                    "Verify that the support-request database script is applied and the application has the required permissions."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("ApplicationAdmin")
    .WithTags("Support")
    .WithSummary("Respond to a support request and update its status")
    .Produces<SupportRequestRecord>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

if (entraAuthenticationEnabled)
{
    app.MapGet(
        "/auth/sign-in",
        (HttpContext httpContext, string? returnUrl, bool? signedOut) =>
            CreateSignInPageAsync(
                httpContext,
                app.Environment,
                returnUrl,
                signedOut == true))
        .AllowAnonymous()
        .WithTags("Authentication")
        .WithSummary("Show the BI Sentinel sign-in page");

    app.MapGet(
        "/auth/continue",
        (HttpContext httpContext, string? returnUrl) =>
        {
            var destination = NormalizeLocalReturnUrl(returnUrl);
            return httpContext.User.Identity?.IsAuthenticated == true
                ? Results.LocalRedirect(destination)
                : Results.Challenge(
                    new Microsoft.AspNetCore.Authentication.AuthenticationProperties
                    {
                        RedirectUri = destination
                    },
                    [OpenIdConnectDefaults.AuthenticationScheme]);
        })
        .AllowAnonymous()
        .WithTags("Authentication")
        .WithSummary("Continue to Microsoft Entra sign-in");

    app.MapGet(
        "/auth/error",
        (IWebHostEnvironment environment) => Results.File(
            Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "error.html"),
            contentType: "text/html; charset=utf-8"))
        .AllowAnonymous()
        .WithTags("Authentication")
        .WithSummary("Show the standard BI Sentinel error page");

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
        (Delegate)((HttpContext httpContext) => CreateSignInPageAsync(
            httpContext,
            app.Environment,
            "/",
            signedOut: true)))
        .AllowAnonymous()
        .WithTags("Authentication")
        .WithSummary("Return to the BI Sentinel sign-in page after signing out");

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
                    detail: ApplicationDatabaseError.GetDetail(
                        exception,
                        "Verify database migration 008 and application database permissions."),
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
                    detail: ApplicationDatabaseError.GetDetail(
                        exception,
                        "Verify database migration 008 and application database permissions."),
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

    app.MapPut(
        "/api/admin/application-users/{userId:guid}/review-role",
        async (
            Guid userId,
            SetApplicationUserReviewRoleRequest request,
            HttpContext httpContext,
            IApplicationUserStore userStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            if (request.RoleName is not ("ReviewManager" or "ReviewLead"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.RoleName)] = ["RoleName must be ReviewManager or ReviewLead."]
                });
            }

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
                var result = await userStore.SetReviewerRoleAsync(
                    settings.TenantId,
                    userId,
                    actor.UserId,
                    request.RoleName,
                    request.IsGranted,
                    cancellationToken);
                return result == ApplicationUserRoleChangeResult.UserNotFound
                    ? Results.NotFound()
                    : Results.Ok(new ApplicationUserReviewRoleResponse(
                        userId,
                        request.RoleName,
                        request.IsGranted));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Changing BI Sentinel reviewer role failed for user {UserId}.", userId);
                return Results.Problem(
                    title: "Reviewer role could not be changed",
                    detail: "Verify database migration 012 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("ApplicationAdmin")
        .WithTags("Administration")
        .WithSummary("Grant or remove a BI Sentinel Manager or Lead reviewer role")
        .Produces<ApplicationUserReviewRoleResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    app.MapGet(
        "/api/admin/hierarchy",
        async (
            HttpContext httpContext,
            IOrganizationHierarchyStore hierarchyStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            var actor = ApplicationUserContext.Get(httpContext);
            if (actor is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await hierarchyStore.GetSnapshotAsync(
                    settings.TenantId,
                    actor.UserId,
                    settings.BootstrapAdminObjectId,
                    actor.IsAdmin,
                    cancellationToken));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading organization hierarchy settings failed.");
                return Results.Problem(
                    title: "Organization settings could not be loaded",
                    detail: "Apply database migration 019 and verify application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("HierarchyManager")
        .WithTags("Administration")
        .WithSummary("List organizations, projects, teams, users, and scoped access")
        .Produces<OrganizationHierarchySnapshot>(StatusCodes.Status200OK);

    app.MapPost(
        "/api/admin/hierarchy/organizations",
        async (
            CreateOrganizationRequest request,
            HttpContext httpContext,
            IOrganizationHierarchyStore hierarchyStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Name)] = ["Enter an organization name between 1 and 200 characters."]
                });
            }

            var actor = ApplicationUserContext.Get(httpContext);
            if (actor is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var (result, id) = await hierarchyStore.CreateOrganizationAsync(
                    settings.TenantId, actor.UserId, name, actor.IsAdmin, cancellationToken);
                return result switch
                {
                    HierarchyMutationResult.Forbidden => Results.Forbid(),
                    HierarchyMutationResult.Conflict => Results.Conflict(new { detail = "An organization with that name already exists." }),
                    HierarchyMutationResult.Updated => Results.Ok(new { organizationId = id, name }),
                    _ => Results.NotFound()
                };
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Creating organization failed.");
                return Results.Problem(
                    title: "Organization could not be created",
                    detail: "Verify database migration 019 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("HierarchyManager")
        .WithTags("Administration")
        .WithSummary("Create an organization (root administrator only)")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .ProducesValidationProblem();

    app.MapPost(
        "/api/admin/hierarchy/projects",
        async (
            CreateProjectRequest request,
            HttpContext httpContext,
            IOrganizationHierarchyStore hierarchyStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            var name = request.Name?.Trim();
            if (request.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["organizationId"] = request.OrganizationId == Guid.Empty ? ["Choose an organization."] : [],
                    [nameof(request.Name)] = string.IsNullOrWhiteSpace(name) || name.Length > 200
                        ? ["Enter a project name between 1 and 200 characters."]
                        : []
                }.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value));
            }

            var actor = ApplicationUserContext.Get(httpContext);
            if (actor is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var (result, id) = await hierarchyStore.CreateProjectAsync(
                    settings.TenantId, actor.UserId, request.OrganizationId, name, actor.IsAdmin, cancellationToken);
                return result switch
                {
                    HierarchyMutationResult.Forbidden => Results.Forbid(),
                    HierarchyMutationResult.Conflict => Results.Conflict(new { detail = "A project with that name already exists in this organization." }),
                    HierarchyMutationResult.Updated => Results.Ok(new { projectId = id, organizationId = request.OrganizationId, name }),
                    _ => Results.NotFound()
                };
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Creating project failed.");
                return Results.Problem(
                    title: "Project could not be created",
                    detail: "Verify database migration 019 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("HierarchyManager")
        .WithTags("Administration")
        .WithSummary("Create a project under an organization")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .ProducesValidationProblem();

    app.MapPost(
        "/api/admin/hierarchy/teams",
        async (
            CreateTeamRequest request,
            HttpContext httpContext,
            IOrganizationHierarchyStore hierarchyStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            var name = request.Name?.Trim();
            if (request.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 200)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["projectId"] = request.ProjectId == Guid.Empty ? ["Choose a project."] : [],
                    [nameof(request.Name)] = string.IsNullOrWhiteSpace(name) || name.Length > 200
                        ? ["Enter a team name between 1 and 200 characters."]
                        : []
                }.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value));
            }

            var actor = ApplicationUserContext.Get(httpContext);
            if (actor is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var (result, id) = await hierarchyStore.CreateTeamAsync(
                    settings.TenantId, actor.UserId, request.ProjectId, name, actor.IsAdmin, cancellationToken);
                return result switch
                {
                    HierarchyMutationResult.Forbidden => Results.Forbid(),
                    HierarchyMutationResult.Conflict => Results.Conflict(new { detail = "A team with that name already exists in this project." }),
                    HierarchyMutationResult.Updated => Results.Ok(new { teamId = id, projectId = request.ProjectId, name }),
                    _ => Results.NotFound()
                };
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Creating team failed.");
                return Results.Problem(
                    title: "Team could not be created",
                    detail: "Verify database migration 019 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("HierarchyManager")
        .WithTags("Administration")
        .WithSummary("Create a team under a project")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .ProducesValidationProblem();

    app.MapPut(
        "/api/admin/hierarchy/assignments",
        async (
            SetUserScopeAssignmentRequest request,
            HttpContext httpContext,
            IOrganizationHierarchyStore hierarchyStore,
            ApplicationAuthenticationSettings settings,
            CancellationToken cancellationToken) =>
        {
            if (request.UserId == Guid.Empty || request.ScopeId == Guid.Empty)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["userId"] = request.UserId == Guid.Empty ? ["Choose a user."] : [],
                    ["scopeId"] = request.ScopeId == Guid.Empty ? ["Choose a scope."] : []
                }.Where(pair => pair.Value.Length > 0).ToDictionary(pair => pair.Key, pair => pair.Value));
            }

            var actor = ApplicationUserContext.Get(httpContext);
            if (actor is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await hierarchyStore.SetAssignmentAsync(
                    settings.TenantId,
                    actor.UserId,
                    request.UserId,
                    request.ScopeType,
                    request.ScopeId,
                    request.RoleName,
                    request.IsAssigned,
                    actor.IsAdmin,
                    cancellationToken);
                return result switch
                {
                    HierarchyMutationResult.Forbidden => Results.Forbid(),
                    HierarchyMutationResult.NotFound => Results.NotFound(),
                    _ => Results.Ok(new { request.UserId, request.ScopeType, request.ScopeId, request.RoleName, request.IsAssigned })
                };
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Changing hierarchy role assignment failed.");
                return Results.Problem(
                    title: "User access could not be changed",
                    detail: "Verify database migration 019 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("HierarchyManager")
        .WithTags("Administration")
        .WithSummary("Assign or remove a scoped lead or read-only role")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

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
    app.MapGet("/api/governance-audit/scan-scope", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/access-status", GovernanceAuditUnavailable);
    app.MapPut("/api/governance-audit/configuration", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/checks", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/workspaces", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/scan-workspaces", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/workspaces/{workspaceId:guid}/artifacts", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/apps", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/gateways", GovernanceAuditUnavailable);
    app.MapPost("/api/governance-audit/scan", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/runs", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/runs/{runId:guid}/export", GovernanceAuditUnavailable);
    app.MapPost("/api/governance-audit/exceptions", GovernanceAuditUnavailable);
    app.MapPost("/api/governance-audit/exceptions/{exceptionId:guid}/revoke", GovernanceAuditUnavailable);
    app.MapGet("/api/governance-audit/signin", GovernanceAuditUnavailable);
}
else
{
    app.MapGet(
        "/api/governance-audit/checks",
        () => Results.Ok(GovernanceCheckCatalog.All))
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("List versioned Governance Audit checks");

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
        "/api/governance-audit/access-status",
        async (
            IPowerBiDelegatedAccessTokenProvider tokenProvider,
            HttpContext context) =>
        {
            try
            {
                await tokenProvider.GetAccessTokenAsync(
                    context.User,
                    "https://analysis.windows.net/powerbi/api/Workspace.Read.All");
                var modelMetadataAuthorized = true;
                try
                {
                    await tokenProvider.GetAccessTokenAsync(
                        context.User,
                        "https://analysis.windows.net/powerbi/api/Tenant.Read.All");
                }
                catch (PowerBiDelegatedConsentRequiredException)
                {
                    modelMetadataAuthorized = false;
                }

                return Results.Ok(new { authorized = true, modelMetadataAuthorized });
            }
            catch (PowerBiDelegatedConsentRequiredException)
            {
                return Results.Ok(new { authorized = false, modelMetadataAuthorized = false });
            }
        })
        .RequireAuthorization("GovernanceAuditAdmin")
        .WithTags("Governance Audit")
        .WithSummary("Check delegated Power BI read authorization");

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

    app.MapGet(
        "/api/governance-audit/scan-scope",
        async (
            IGovernanceAuditConfigurationStore configurationStore,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var configuration = await configurationStore.GetAsync(cancellationToken);
                return Results.Ok(new
                {
                    workspaceIds = configuration.WorkspaceIds,
                    maximumWorkspaces = maximumGovernanceWorkspaces
                });
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading Governance Audit scan scope failed.");
                return Results.Problem(
                    title: "Governance Audit scan scope could not be loaded",
                    detail: "Verify database migration 007 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("Get the approved workspace IDs available for a Governance Audit scan");

    app.MapGet(
        "/api/governance-audit/scan-workspaces",
        async (
            IGovernanceAuditConfigurationStore configurationStore,
            PowerBiGovernanceAuditClient auditClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var approvedScope = await configurationStore.GetAsync(cancellationToken);
                var workspaces = await auditClient.GetApprovedWorkspaceDetailsAsync(
                    approvedScope.WorkspaceIds,
                    httpContext.User,
                    cancellationToken);
                return Results.Ok(workspaces);
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading approved Governance Audit workspace names failed.");
                return Results.Problem(
                    title: "Approved workspace names could not be loaded",
                    detail: "Verify Governance Audit database migrations and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (PowerBiAuditException exception)
            {
                app.Logger.LogWarning(exception, "Loading approved Power BI workspace names failed with HTTP {StatusCode}.", (int)exception.StatusCode);
                return Results.Problem(
                    title: "Power BI workspace names could not be loaded",
                    detail: exception.Message,
                    statusCode: exception.RequiresConsent
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("Get names and metadata for only the approved Governance Audit workspaces");

    app.MapGet(
        "/api/governance-audit/workspaces/{workspaceId:guid}/artifacts",
        async (
            Guid workspaceId,
            IGovernanceAuditConfigurationStore configurationStore,
            PowerBiGovernanceAuditClient auditClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var approvedScope = await configurationStore.GetAsync(cancellationToken);
                if (!approvedScope.WorkspaceIds.Contains(workspaceId))
                {
                    return Results.NotFound(new ProblemDetails
                    {
                        Title = "Workspace not in approved Governance Audit scope",
                        Detail = "Artifact discovery is limited to workspaces approved by a BI Sentinel administrator.",
                        Status = StatusCodes.Status404NotFound
                    });
                }

                return Results.Ok(await auditClient.GetWorkspaceArtifactCatalogAsync(
                    workspaceId,
                    httpContext.User,
                    cancellationToken));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading Governance Audit artifact catalog for workspace {WorkspaceId} failed.", workspaceId);
                return Results.Problem(
                    title: "Artifact list could not be loaded",
                    detail: "Verify Governance Audit database migrations and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (PowerBiAuditException exception)
            {
                app.Logger.LogWarning(exception, "Power BI artifact discovery failed for workspace {WorkspaceId} with HTTP {StatusCode}.", workspaceId, (int)exception.StatusCode);
                return Results.Problem(
                    title: "Power BI artifact discovery failed",
                    detail: exception.Message,
                    statusCode: exception.RequiresConsent
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("List reports and dashboards in one approved workspace");

    app.MapGet(
        "/api/governance-audit/apps",
        async (
            PowerBiGovernanceAuditClient auditClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await auditClient.GetAppsAsync(httpContext.User, cancellationToken));
            }
            catch (PowerBiAuditException exception)
            {
                app.Logger.LogWarning(exception, "Power BI installed app discovery failed with HTTP {StatusCode}.", (int)exception.StatusCode);
                return Results.Problem(
                    title: "Power BI installed apps could not be loaded",
                    detail: exception.Message,
                    statusCode: exception.RequiresConsent
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("List installed Power BI apps visible to the signed-in user");

    app.MapGet(
        "/api/governance-audit/gateways",
        async (
            PowerBiGovernanceAuditClient auditClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await auditClient.GetGatewaysAsync(httpContext.User, cancellationToken));
            }
            catch (PowerBiAuditException exception)
            {
                app.Logger.LogWarning(exception, "Power BI gateway discovery failed with HTTP {StatusCode}.", (int)exception.StatusCode);
                return Results.Problem(
                    title: "Power BI gateways could not be loaded",
                    detail: exception.Message,
                    statusCode: exception.RequiresConsent
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("List gateways where the signed-in user has gateway administrator access");

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
                    detail: exception.Number == 208
                        ? "The Governance Audit history schema is incomplete. Apply migrations 007, 009, 010, 011, 014, 015, and 016 to SmartAgent."
                        : "Apply Governance Audit migrations 007, 009, 010, 011, 014, 015, and 016 and verify application database permissions.",
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
            IGovernanceExceptionStore exceptionStore,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await historyStore.GetAsync(runId, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound();
                }

                var exceptions = await exceptionStore.GetForFindingsAsync(
                    result.Findings,
                    cancellationToken);
                return Results.Ok(GovernanceExceptionApplicator.Apply(
                    result,
                    exceptions,
                    DateTimeOffset.UtcNow));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Loading saved Governance Audit run {RunId} failed.", runId);
                return Results.Problem(
                    title: "Governance Audit run could not be loaded",
                    detail: exception.Number == 208
                        ? "The Governance Audit history or exception schema is incomplete. Apply migrations 007, 009, 010, 011, 014, 015, and 016 to SmartAgent."
                        : "Verify Governance Audit migrations 007, 009, 010, 011, 014, 015, and 016 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("Load a saved Governance Audit run");

    app.MapGet(
        "/api/governance-audit/runs/{runId:guid}/export",
        async Task<IResult> (
            Guid runId,
            IGovernanceAuditHistoryStore historyStore,
            IGovernanceExceptionStore exceptionStore,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await historyStore.GetAsync(runId, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound();
                }

                var exceptions = await exceptionStore.GetForFindingsAsync(
                    result.Findings,
                    cancellationToken);
                var currentResult = GovernanceExceptionApplicator.Apply(
                    result,
                    exceptions,
                    DateTimeOffset.UtcNow);
                var workbook = GovernanceExcelExporter.CreateWorkbook(currentResult);
                return Results.File(
                    workbook,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"bi-sentinel-governance-audit-{currentResult.ScannedUtc:yyyy-MM-dd}.xlsx");
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Exporting saved Governance Audit run {RunId} failed.", runId);
                return Results.Problem(
                    title: "Governance Audit workbook could not be exported",
                    detail: "Verify Governance Audit history and exception migrations and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("Export a saved Governance Audit run as an Excel workbook");

    app.MapPost(
        "/api/governance-audit/exceptions",
        async (
            CreateGovernanceExceptionRequest request,
            IGovernanceExceptionStore exceptionStore,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var validationErrors = new Dictionary<string, string[]>();
            if (request.RunId == Guid.Empty || request.WorkspaceId == Guid.Empty)
            {
                validationErrors[nameof(request.RunId)] = ["Select a saved audit run and workspace."];
            }

            if (!GovernanceCheckCatalog.TryGet(request.CheckId, out var check))
            {
                validationErrors[nameof(request.CheckId)] = ["Select a registered governance check."];
            }

            if (request.ArtifactId == Guid.Empty)
            {
                validationErrors[nameof(request.ArtifactId)] = ["Artifact ID must be a non-empty GUID or omitted when unavailable."];
            }

            var artifactName = request.ArtifactName?.Trim();
            if (string.IsNullOrWhiteSpace(artifactName) || artifactName.Length > 256)
            {
                validationErrors[nameof(request.ArtifactName)] = ["Artifact name is required and must be 256 characters or fewer."];
            }

            var owner = request.Owner?.Trim();
            if (string.IsNullOrWhiteSpace(owner) || owner.Length > 256)
            {
                validationErrors[nameof(request.Owner)] = ["Exception owner is required and must be 256 characters or fewer."];
            }

            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            {
                validationErrors[nameof(request.Reason)] = ["A reason is required and must be 2,000 characters or fewer."];
            }

            var artifactType = request.ArtifactType?.Trim();
            if (artifactType?.Length > 40)
            {
                validationErrors[nameof(request.ArtifactType)] = ["Artifact type must be 40 characters or fewer."];
            }

            if (request.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                validationErrors[nameof(request.ExpiresUtc)] = ["Expiry must be a future date and time."];
            }
            else if (request.ExpiresUtc > DateTimeOffset.UtcNow.AddYears(1))
            {
                validationErrors[nameof(request.ExpiresUtc)] = ["An exception cannot be approved for more than one year."];
            }

            if (validationErrors.Count > 0)
            {
                return Results.ValidationProblem(validationErrors);
            }

            var normalizedRequest = request with
            {
                CheckId = check!.CheckId,
                ArtifactName = artifactName!,
                ArtifactType = string.IsNullOrEmpty(artifactType) ? null : artifactType,
                Owner = owner!,
                Reason = reason!
            };
            var actor = ApplicationUserContext.Get(httpContext)?.EmailAddress
                ?? httpContext.User.Identity?.Name
                ?? "Governance Audit administrator";
            try
            {
                return Results.Ok(await exceptionStore.ApproveAsync(
                    normalizedRequest,
                    actor,
                    cancellationToken));
            }
            catch (GovernanceExceptionConflictException exception)
            {
                return Results.Conflict(new { title = "Exception could not be approved", detail = exception.Message });
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Approving a Governance Audit exception failed.");
                return Results.Problem(
                    title: "Governance exception could not be saved",
                    detail: "Verify migration 015 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditAdmin")
        .WithTags("Governance Audit")
        .WithSummary("Approve a time-limited exception for a saved failing finding")
        .Produces<GovernanceExceptionRecord>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Produces(StatusCodes.Status409Conflict);

    app.MapPost(
        "/api/governance-audit/exceptions/{exceptionId:guid}/revoke",
        async (
            Guid exceptionId,
            RevokeGovernanceExceptionRequest request,
            IGovernanceExceptionStore exceptionStore,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Reason)] = ["A revocation reason is required and must be 2,000 characters or fewer."]
                });
            }

            var actor = ApplicationUserContext.Get(httpContext)?.EmailAddress
                ?? httpContext.User.Identity?.Name
                ?? "Governance Audit administrator";
            try
            {
                var record = await exceptionStore.RevokeAsync(
                    exceptionId,
                    reason,
                    actor,
                    cancellationToken);
                return record is null
                    ? Results.NotFound()
                    : Results.Ok(record);
            }
            catch (GovernanceExceptionConflictException exception)
            {
                return Results.Conflict(new { title = "Exception could not be revoked", detail = exception.Message });
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(exception, "Revoking Governance Audit exception {ExceptionId} failed.", exceptionId);
                return Results.Problem(
                    title: "Governance exception could not be revoked",
                    detail: "Verify migration 015 and application database permissions.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditAdmin")
        .WithTags("Governance Audit")
        .WithSummary("Revoke an active Governance Audit exception and preserve its audit trail")
        .Produces<GovernanceExceptionRecord>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .ProducesValidationProblem();

    app.MapPost(
        "/api/governance-audit/scan",
        async (
            IGovernanceAuditConfigurationStore configurationStore,
            IGovernanceAuditHistoryStore historyStore,
            IGovernanceExceptionStore exceptionStore,
            PowerBiGovernanceAuditClient auditClient,
            HttpContext httpContext,
            CancellationToken cancellationToken,
            [FromQuery] string[]? checks,
            [FromQuery] Guid[]? workspaceIds,
            [FromQuery] string[]? artifactTypes,
            [FromQuery] Guid[]? reportIds,
            [FromQuery] Guid[]? dashboardIds,
            [FromQuery] bool? filterReports,
            [FromQuery] bool? filterDashboards,
            [FromQuery] Guid[]? appIds,
            [FromQuery] Guid[]? gatewayIds,
            [FromQuery] bool? filterApps,
            [FromQuery] bool? filterGateways) =>
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

            Guid[] selectedWorkspaceIds;
            try
            {
                selectedWorkspaceIds = GovernanceAuditSelection.ValidateWorkspaceIds(
                    configuration.WorkspaceIds,
                    workspaceIds,
                    maximumGovernanceWorkspaces);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Governance Audit workspace selection",
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest
                });
            }

            GovernanceAuditResult result;
            try
            {
                result = await auditClient.ScanAsync(
                    selectedWorkspaceIds,
                    httpContext.User,
                    cancellationToken,
                    checks,
                    artifactTypes,
                    filterReports == true && artifactTypes?.Contains("Reports", StringComparer.OrdinalIgnoreCase) == true
                        ? reportIds ?? []
                        : null,
                    filterDashboards == true && artifactTypes?.Contains("Dashboards", StringComparer.OrdinalIgnoreCase) == true
                        ? dashboardIds ?? []
                        : null,
                    filterApps == true && artifactTypes?.Contains("Apps", StringComparer.OrdinalIgnoreCase) == true
                        ? appIds ?? []
                        : null,
                    filterGateways == true && artifactTypes?.Contains("Gateways", StringComparer.OrdinalIgnoreCase) == true
                        ? gatewayIds ?? []
                        : null);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Governance Audit selection",
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest
                });
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
                var savedResult = result with { RunId = runId };
                var exceptions = await exceptionStore.GetForFindingsAsync(
                    savedResult.Findings,
                    cancellationToken);
                return Results.Ok(GovernanceExceptionApplicator.Apply(
                    savedResult,
                    exceptions,
                    DateTimeOffset.UtcNow));
            }
            catch (SqlException exception)
            {
                app.Logger.LogError(
                    exception,
                    "Saving Governance Audit scan results failed with SQL error {SqlErrorNumber}.",
                    exception.Number);
                return Results.Problem(
                    title: exception.Number == 208 &&
                           exception.Message.Contains("BiAgentGovernanceExceptions", StringComparison.OrdinalIgnoreCase)
                        ? "Governance exception status could not be applied"
                        : "Governance Audit results could not be saved",
                    detail: exception.Number == 207
                        ? "Power BI was read, but required Governance Audit columns are missing. Apply migrations 009 and 010 to SmartAgent, then retry."
                        : exception.Number == 547 &&
                          exception.Message.Contains(
                              "CK_BiAgentGovernanceAuditRunFindings_Severity",
                              StringComparison.OrdinalIgnoreCase)
                            ? "Power BI was read, but the database severity constraint does not permit the selected governance check severity. Apply database migration 013 to SmartAgent, then retry the scan."
                        : exception.Number == 547 &&
                          exception.Message.Contains(
                              "CK_BiAgentGovernanceAuditRunArtifacts_Type",
                              StringComparison.OrdinalIgnoreCase)
                            ? "Power BI was read, but the database does not permit Dashboard records yet. Apply database migration 010 to SmartAgent, then retry the scan."
                        : exception.Number == 208 &&
                          exception.Message.Contains(
                              "BiAgentGovernanceAuditRunGlobalArtifacts",
                              StringComparison.OrdinalIgnoreCase)
                            ? "Power BI was read, but the database does not have the global Apps/Gateways history table. Apply database migration 011 to SmartAgent, then retry the scan."
                        : exception.Number == 208 &&
                          (exception.Message.Contains("BiAgentGovernanceAuditRunReportConnections", StringComparison.OrdinalIgnoreCase) ||
                           exception.Message.Contains("BiAgentGovernanceAuditRunGlobalArtifacts", StringComparison.OrdinalIgnoreCase))
                            ? "Power BI was read, but required inventory tables are missing. Apply database migration 014 to SmartAgent, then retry the scan."
                        : exception.Number == 208 &&
                          exception.Message.Contains("BiAgentGovernanceExceptions", StringComparison.OrdinalIgnoreCase)
                            ? "The audit run was saved, but exception status could not be loaded. Apply database migration 015 to SmartAgent and verify application database permissions."
                            : $"Power BI was read, but its results could not be saved (SQL error {exception.Number}). Review the server log and confirm the application is connected to SmartAgent.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Governance Audit")
        .WithSummary("Run a read-only audit of approved Power BI workspaces")
        .WithDescription("Runs the selected checks against approved workspaces. OPT-001 through OPT-003 automatically request semantic-model table metadata; unavailable metadata is recorded as insufficient evidence.")
        .DisableAntiforgery();

    app.MapMcp("/mcp")
        .RequireAuthorization("GovernanceAuditReader")
        .WithTags("Power BI Governance MCP")
        .WithSummary("Access the BI Sentinel Power BI Governance MCP tools, resources, and prompts");
}

app.MapPost(
    "/api/reviews/upload",
    async (
        IFormFile file,
        string? projectId,
        HttpContext httpContext,
        PbipProjectInputService projectInput,
        IReviewHistoryStore historyStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var review = await projectInput.ReviewArchiveAsync(file, projectId, cancellationToken);
            var user = ApplicationUserContext.Get(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            return await SaveReviewAsync(
                review,
                historyStore,
                reviewHistoryRetentionDays,
                user.UserId,
                logger,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
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
    "/api/reviews/online/signin",
    () =>
    {
        var properties = new AuthenticationProperties { RedirectUri = "/reviews?source=online" };
        properties.Items["PowerBiProjectReviewConsent"] = "true";
        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Project Review")
    .WithSummary("Authorize delegated read access for online Project Review");

app.MapGet(
    "/api/reviews/online/workspaces",
    async (
        PowerBiOnlineReviewClient onlineReviewClient,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await onlineReviewClient.DiscoverWorkspacesAsync(
                httpContext.User,
                cancellationToken));
        }
        catch (PowerBiOnlineReviewException exception)
        {
            logger.LogWarning(exception, "Discovering the signed-in user's Power BI workspaces failed.");
            return Results.Problem(
                title: "Power BI workspaces could not be loaded",
                detail: exception.Message,
                statusCode: exception.RequiresConsent
                    ? StatusCodes.Status409Conflict
                    : (int)exception.StatusCode);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Project Review")
    .WithSummary("List workspaces accessible to the signed-in Power BI user")
    .Produces<IReadOnlyList<PowerBiReviewWorkspace>>(StatusCodes.Status200OK);

app.MapPost(
    "/api/reviews/online/scan",
    async (
        PowerBiOnlineReviewRequest request,
        PowerBiOnlineReviewClient onlineReviewClient,
        IPbipProjectReviewer reviewer,
        IReviewHistoryStore historyStore,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (request.WorkspaceId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.WorkspaceId)] = ["Choose a valid Power BI workspace."]
            });
        }

        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var scan = await onlineReviewClient.ScanWorkspaceAsync(
                request.WorkspaceId,
                httpContext.User,
                cancellationToken);
            var unavailableEvidenceRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "measure-table-name",
                "measure-naming",
                "required-measures",
                "explicit-measures",
                "dax-division",
                "dax-format",
                "unused-tables",
                "unused-columns",
                "unused-measures"
            };
            var projectId = $"online-{request.WorkspaceId:N}";
            var review = await reviewer.ReviewOnlineModelAsync(
                scan.Project,
                projectId,
                unavailableEvidenceRules,
                cancellationToken);
            review = review with
            {
                Findings = review.Findings.Concat(scan.EvidenceLimitations.Select(limitation =>
                    new PbipReviewFinding(
                        "ONLINE-EVIDENCE-001",
                        "Information",
                        limitation,
                        scan.Project.ProjectName,
                        "Use a PBIP/PBIR project upload for DAX, measures, report definitions, and full semantic-model metadata.")).ToArray())
                    .ToArray()
            };

            var entry = await historyStore.SaveAsync(
                review,
                reviewHistoryRetentionDays,
                user.UserId,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
            return Results.Ok(new SavePowerBiOnlineReviewResponse(
                entry.Summary.ReviewRunId,
                entry.Summary.ReviewedUtc,
                entry.Summary.ExpiresUtc,
                review with { ProjectName = entry.Summary.ProjectName },
                entry.Findings,
                new PowerBiOnlineReviewEvidence(
                    scan.Project.ProjectName,
                    scan.ReportCount,
                    scan.SemanticModelCount,
                    scan.SchemaAvailableModelCount,
                    scan.EvidenceLimitations.ToArray())));
        }
        catch (PowerBiOnlineReviewException exception)
        {
            logger.LogWarning(
                exception,
                "Online Power BI project review failed for workspace {WorkspaceId}.",
                request.WorkspaceId);
            return Results.Problem(
                title: "Online Project Review could not be completed",
                detail: exception.Message,
                statusCode: exception.RequiresConsent
                    ? StatusCodes.Status409Conflict
                    : (int)exception.StatusCode);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Saving an online Power BI Project Review failed.");
            return Results.Problem(
                title: "Online review completed but could not be saved",
                detail: "Verify the review-history migration and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException exception)
        {
            logger.LogWarning(exception, "A configured review regular expression exceeded its time limit.");
            return Results.Problem(
                title: "Review configuration requires adjustment",
                detail: "A configured naming pattern took too long to evaluate. Adjust it in Administration and retry.",
                statusCode: StatusCodes.Status400BadRequest);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationUser"))
    .WithTags("Project Review")
    .WithSummary("Review metadata from one accessible Power BI workspace")
    .WithDescription("Reads workspace, report, semantic-model, and available table/relationship metadata using the signed-in user's delegated Power BI access. DAX, report visuals, and unsupported model schemas remain explicit evidence gaps.")
    .Produces<SavePowerBiOnlineReviewResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/performance-optimization/assess",
    async (
        IFormFile file,
        PbipProjectInputService projectInput,
        PerformanceAssessmentService assessmentService,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var assessment = await projectInput.ProcessArtifactAsync(
                file,
                allowLooseArtifacts: true,
                (projectPath, projectName, token) => Task.FromResult(
                    assessmentService.Assess(
                        projectPath,
                        projectName,
                        Path.GetExtension(file.FileName))),
                cancellationToken);
            return Results.Ok(assessment);
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Uploaded Power BI artifact could not be assessed.");
            return Results.Problem(
                title: "Uploaded Power BI artifact is invalid or unsupported",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    })
    .WithTags("Performance Optimization")
    .WithSummary("Generate a read-only static performance assessment")
    .WithDescription("Accepts PBIT, a PBIP/PBIR project ZIP, Model.bim, a TMDL definition or TMDL ZIP, or DAX. Results separate static observations and hypotheses from unavailable runtime telemetry; no Power BI content is changed.")
    .WithMetadata(new RequestSizeLimitAttribute(MaximumUploadRequestBytes))
    .WithMetadata(new RequestFormLimitsAttribute
    {
        MultipartBodyLengthLimit = MaximumUploadRequestBytes
    })
    .DisableAntiforgery()
    .Produces<PerformanceAssessment>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

app.MapGet(
    "/api/reviews/history",
    async (
        int? limit,
        string? projectName,
        HttpContext httpContext,
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
            var user = ApplicationUserContext.Get(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await historyStore.GetRecentAsync(
                resultLimit,
                searchName,
                user.IsAdmin || user.IsManager || user.IsLead ? null : user.UserId,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext)));
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
    .WithDescription("Searches up to 500 unexpired review summaries belonging to the signed-in uploader, or all runs for an authorized Admin, Manager, or Lead reviewer.")
    .Produces<IReadOnlyList<ReviewHistorySummary>>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/history/{reviewRunId:guid}",
    async (
        Guid reviewRunId,
        HttpContext httpContext,
        IReviewHistoryStore historyStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var user = ApplicationUserContext.Get(httpContext);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var entry = await historyStore.GetAsync(
                reviewRunId,
                user.IsAdmin || user.IsManager || user.IsLead ? null : user.UserId,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
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
    .WithDescription("Returns an unexpired review summary and findings only when owned by the signed-in uploader or when requested by an authorized reviewer.")
    .Produces<ReviewHistoryEntry>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/history/{reviewRunId:guid}/submission",
    async (
        Guid reviewRunId,
        HttpContext httpContext,
        IReviewSubmissionStore submissionStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await submissionStore.GetMineAsync(
                reviewRunId,
                user.UserId,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext)));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading current user's review submission failed.");
            return Results.Problem(
                title: "Review submission could not be loaded",
                detail: "Verify database migration 012 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Review submissions")
    .WithSummary("Load the signed-in user's submitted response and reviewer feedback")
    .Produces<ReviewSubmission>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/reviews/history/{reviewRunId:guid}/submission",
    async (
        Guid reviewRunId,
        SubmitReviewRequest request,
        HttpContext httpContext,
        IReviewSubmissionStore submissionStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var errors = ReviewSubmissionValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var submission = await submissionStore.SubmitAsync(
                reviewRunId,
                user.UserId,
                request.Findings,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
            return submission is null
                ? Results.Conflict(new
                {
                    title = "Review cannot be submitted",
                    detail = "The review may have expired, the finding list changed, or this review was already submitted."
                })
                : Results.Ok(submission);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Saving review submission failed for review run {ReviewRunId}.", reviewRunId);
            return Results.Problem(
                title: "Review submission could not be saved",
                detail: "Verify database migration 012 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Review submissions")
    .WithSummary("Submit the user's per-finding justifications and lock the submission")
    .Produces<ReviewSubmission>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status409Conflict)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/my-submissions",
    async (
        HttpContext httpContext,
        IReviewSubmissionStore submissionStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await submissionStore.GetMyQueueAsync(
                user.UserId,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext)));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading the current user's submission list failed.");
            return Results.Problem(
                title: "Submitted reviews could not be loaded",
                detail: "Verify database migration 012 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithTags("Review submissions")
    .WithSummary("List submissions owned by the signed-in user")
    .Produces<IReadOnlyList<ReviewSubmissionSummary>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/submissions",
    async (
        bool? includeReviewed,
        HttpContext httpContext,
        IReviewSubmissionStore submissionStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await submissionStore.GetQueueAsync(
                includeReviewed == true,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext)));
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading reviewer submission queue failed.");
            return Results.Problem(
                title: "Review queue could not be loaded",
                detail: "Verify database migration 012 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
    .WithTags("Review submissions")
    .WithSummary("List pending or completed submissions for authorized reviewers")
    .Produces<IReadOnlyList<ReviewSubmissionSummary>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet(
    "/api/reviews/submissions/{submissionId:guid}",
    async (
        Guid submissionId,
        HttpContext httpContext,
        IReviewSubmissionStore submissionStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var submission = await submissionStore.GetAsync(
                submissionId,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
            return submission is null ? Results.NotFound() : Results.Ok(submission);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading reviewer submission {SubmissionId} failed.", submissionId);
            return Results.Problem(
                title: "Submitted review could not be loaded",
                detail: "Verify database migration 012 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
    .WithTags("Review submissions")
    .WithSummary("Load a submitted review for authorized review")
    .Produces<ReviewSubmission>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/reviews/submissions/{submissionId:guid}/complete",
    async (
        Guid submissionId,
        CompleteReviewSubmissionRequest request,
        HttpContext httpContext,
        IReviewSubmissionStore submissionStore,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var user = ApplicationUserContext.Get(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var errors = ReviewSubmissionValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var submission = await submissionStore.CompleteReviewAsync(
                submissionId,
                user.UserId,
                request.Findings,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
            return submission is null
                ? Results.Conflict(new
                {
                    title = "Review response cannot be submitted",
                    detail = "The submission may have expired, already been reviewed, or the finding list changed."
                })
                : Results.Ok(submission);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Submitting reviewer response failed for {SubmissionId}.", submissionId);
            return Results.Problem(
                title: "Reviewer response could not be saved",
                detail: "Verify database migration 012 and application database permissions.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
    .WithTags("Review submissions")
    .WithSummary("Record a per-finding reviewer decision and lock the response")
    .Produces<ReviewSubmission>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status409Conflict)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPost(
    "/api/reviews/feedback",
    async (
        SaveReviewFeedbackRequest request,
        HttpContext httpContext,
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
            var savedUtc = await feedbackStore.SaveAsync(
                request,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
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
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
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
        HttpContext httpContext,
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
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
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
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
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
        HttpContext httpContext,
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
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext));
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
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
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
        HttpContext httpContext,
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
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext)));
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
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
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
        HttpContext httpContext,
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
            return Results.Ok(await feedbackStore.GetRecentAsync(
                resultLimit,
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext)));
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
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
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
        HttpContext httpContext,
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
                cancellationToken,
                ActiveWorkspaceScope.Get(httpContext)));
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
    .RequireAuthorization(policy => policy.RequireRole("ApplicationAdmin", "ReviewManager", "ReviewLead"))
    .WithTags("Reviews")
    .WithSummary("List waiting and closed review findings")
    .WithDescription("Lists unexpired findings and this browser reviewer's submission status.")
    .Produces<IReadOnlyList<ReviewDashboardRun>>(StatusCodes.Status200OK)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapFallback(
    "/api/{**path}",
    () => Results.Problem(
        title: "API endpoint not found",
        detail: "This BI Sentinel deployment may not include the requested API. Publish and restart the API before using this page.",
        statusCode: StatusCodes.Status404NotFound));
app.MapFallbackToFile("index.html");
app.Run();

static WorkspaceContextResponse BuildWorkspaceContext(
    OrganizationHierarchySnapshot snapshot,
    ActiveWorkspaceScope? activeScope,
    bool isRootAdmin)
{
    var scopeOptions = WorkspaceScopeOptionsBuilder.Build(snapshot, Guid.Empty, isRootAdmin: true);
    var activeScopeOption = activeScope is null
        ? null
        : scopeOptions.FirstOrDefault(option =>
            option.ScopeType == activeScope.ScopeType && option.ScopeId == activeScope.ScopeId);
    if (activeScope is not null && activeScopeOption is null)
    {
        return new WorkspaceContextResponse(activeScope, null, []);
    }

    var includedScopeIds = new HashSet<Guid>();
    if (activeScopeOption is not null)
    {
        includedScopeIds.Add(activeScope!.ScopeId);
        if (activeScope.ScopeType == "Organization")
        {
            var projectIds = snapshot.Projects
                .Where(project => project.OrganizationId == activeScope.ScopeId)
                .Select(project => project.ProjectId)
                .ToHashSet();
            includedScopeIds.UnionWith(projectIds);
            includedScopeIds.UnionWith(snapshot.Teams
                .Where(team => projectIds.Contains(team.ProjectId))
                .Select(team => team.TeamId));
        }
        else if (activeScope.ScopeType == "Project")
        {
            var project = snapshot.Projects.FirstOrDefault(item => item.ProjectId == activeScope.ScopeId);
            if (project is not null)
            {
                includedScopeIds.Add(project.OrganizationId);
            }
            includedScopeIds.UnionWith(snapshot.Teams
                .Where(team => team.ProjectId == activeScope.ScopeId)
                .Select(team => team.TeamId));
        }
        else
        {
            var team = snapshot.Teams.FirstOrDefault(item => item.TeamId == activeScope.ScopeId);
            var project = team is null
                ? null
                : snapshot.Projects.FirstOrDefault(item => item.ProjectId == team.ProjectId);
            if (project is not null)
            {
                includedScopeIds.Add(project.ProjectId);
                includedScopeIds.Add(project.OrganizationId);
            }
        }
    }

    var rolesByUser = snapshot.Assignments
        .Where(assignment => includedScopeIds.Contains(assignment.ScopeId))
        .GroupBy(assignment => assignment.UserId)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<string>)group
                .Select(assignment => assignment.RoleName)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    var members = snapshot.Users
        .Where(user => activeScope is null
            ? isRootAdmin
            : rolesByUser.ContainsKey(user.UserId) || user.IsAdmin)
        .Select(user =>
        {
            var roles = rolesByUser.GetValueOrDefault(user.UserId, []);
            var visibleRoles = new HashSet<string>(roles, StringComparer.Ordinal);
            if (user.IsAdmin) visibleRoles.Add("ApplicationAdmin");
            if (user.IsManager) visibleRoles.Add("ReviewManager");
            if (user.IsLead) visibleRoles.Add("ReviewLead");
            return new WorkspaceMemberSummary(
                user.UserId,
                user.DisplayName,
                user.EmailAddress,
                visibleRoles.Order(StringComparer.Ordinal).ToArray());
        })
        .OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    return new WorkspaceContextResponse(activeScope, activeScopeOption?.Label, members);
}

static string NormalizeLocalReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/";
    }

    var candidate = returnUrl.Trim();
    var path = candidate.Split('?', '#')[0];
    if (candidate[0] != '/' ||
        candidate.StartsWith("//", StringComparison.Ordinal) ||
        candidate.StartsWith("/\\", StringComparison.Ordinal) ||
        candidate.Contains('\\') ||
        candidate.Any(char.IsControl) ||
        path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase))
    {
        return "/";
    }

    return candidate;
}

static async Task<IResult> CreateSignInPageAsync(
    HttpContext httpContext,
    IWebHostEnvironment environment,
    string? returnUrl,
    bool signedOut)
{
    var destination = NormalizeLocalReturnUrl(returnUrl);
    if (httpContext.User.Identity?.IsAuthenticated == true && !signedOut)
    {
        return Results.LocalRedirect(destination);
    }

    var webRoot = environment.WebRootPath ??
        Path.Combine(environment.ContentRootPath, "wwwroot");
    var page = await File.ReadAllTextAsync(
        Path.Combine(webRoot, "sign-in.html"),
        httpContext.RequestAborted);
    var signInUrl = $"/auth/continue?returnUrl={Uri.EscapeDataString(destination)}";
    page = page
        .Replace("{{SIGN_IN_URL}}", signInUrl, StringComparison.Ordinal)
        .Replace(
            "{{SIGN_IN_HEADING}}",
            signedOut ? "You’re signed out" : "Request BI Sentinel access",
            StringComparison.Ordinal)
        .Replace(
            "{{SIGN_IN_MESSAGE}}",
            signedOut
                ? "Your BI Sentinel session has ended. Sign in with your organization account to continue."
                : "Sign in with Microsoft to request access to an organization, project, or team. A BI Sentinel lead must approve your request before you can enter the workspace.",
            StringComparison.Ordinal);
    return Results.Content(page, "text/html; charset=utf-8");
}

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
    Guid uploadedByUserId,
    ILogger<Program> logger,
    CancellationToken cancellationToken,
    ActiveWorkspaceScope? scope)
{
    try
    {
        var entry = await historyStore.SaveAsync(
            review,
            retentionDays,
            uploadedByUserId,
            cancellationToken,
            scope);
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
