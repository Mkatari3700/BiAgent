using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace BISentinel.Api.GovernanceAudit;

[McpServerToolType]
public sealed class PowerBiGovernanceMcpTools
{
    [McpServerTool(Name = "powerbi_governance_get_checks")]
    [System.ComponentModel.Description("List versioned checks. OPT-001 through OPT-003 automatically inspect semantic-model metadata where Power BI exposes it and report unavailable metadata as insufficient evidence.")]
    public static IReadOnlyList<GovernanceCheckDefinition> GetChecks(
        ILogger<PowerBiGovernanceMcpTools> logger)
    {
        logger.LogInformation("MCP governance tool invoked: powerbi_governance_get_checks.");
        return GovernanceCheckCatalog.All;
    }

    [McpServerTool(Name = "powerbi_governance_get_approved_workspaces")]
    [System.ComponentModel.Description("Read the administrator-approved Power BI workspace IDs available for a scan. Does not discover or expand the approved scope.")]
    public static async Task<GovernanceApprovedWorkspaceScope> GetApprovedWorkspaces(
        IGovernanceAuditConfigurationStore configurationStore,
        ILogger<PowerBiGovernanceMcpTools> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await configurationStore.GetAsync(cancellationToken);
            logger.LogInformation(
                "MCP governance tool invoked: powerbi_governance_get_approved_workspaces. ApprovedWorkspaceCount={WorkspaceCount}; ConfigurationVersion={ConfigurationVersion}.",
                configuration.WorkspaceIds.Length,
                configuration.Version);
            return new GovernanceApprovedWorkspaceScope(
                configuration.Version,
                configuration.WorkspaceIds,
                configuration.UpdatedUtc);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Reading the approved Governance Audit MCP scope failed with SQL error {SqlErrorNumber}.", exception.Number);
            throw new McpException("The approved workspace scope could not be loaded. Check database connectivity, migrations, permissions, and server logs.");
        }
    }

    [McpServerTool(Name = "powerbi_governance_get_installed_apps")]
    [System.ComponentModel.Description("List installed Power BI apps visible to the signed-in user. This is account-level inventory, not limited to the approved workspace list.")]
    public static async Task<IReadOnlyList<GovernanceArtifact>> GetInstalledApps(
        PowerBiGovernanceAuditClient auditClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PowerBiGovernanceMcpTools> logger,
        CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true ||
            !user.IsInRole("ApplicationAdmin"))
        {
            throw new McpException("An authenticated BI Sentinel administrator is required.");
        }

        try
        {
            var apps = await auditClient.GetAppsAsync(user, cancellationToken);
            logger.LogInformation(
                "MCP installed Power BI app catalog read. Apps={AppCount}.",
                apps.Count);
            return apps;
        }
        catch (PowerBiAuditException exception)
        {
            logger.LogWarning(
                exception,
                "MCP installed app catalog read failed with HTTP {StatusCode}.",
                (int)exception.StatusCode);
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "powerbi_governance_get_gateways")]
    [System.ComponentModel.Description("List Power BI gateways where the signed-in user has gateway administrator access. This is account-level inventory, not limited to the approved workspace list.")]
    public static async Task<IReadOnlyList<GovernanceArtifact>> GetGateways(
        PowerBiGovernanceAuditClient auditClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PowerBiGovernanceMcpTools> logger,
        CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true ||
            !user.IsInRole("ApplicationAdmin"))
        {
            throw new McpException("An authenticated BI Sentinel administrator is required.");
        }

        try
        {
            var gateways = await auditClient.GetGatewaysAsync(user, cancellationToken);
            logger.LogInformation("MCP gateway catalog read. Gateways={GatewayCount}.", gateways.Count);
            return gateways;
        }
        catch (PowerBiAuditException exception)
        {
            logger.LogWarning(
                exception,
                "MCP gateway catalog read failed with HTTP {StatusCode}.",
                (int)exception.StatusCode);
            throw new McpException(exception.Message);
        }
    }

    [McpServerTool(Name = "powerbi_governance_scan")]
    [System.ComponentModel.Description("Run and persist a read-only scan of selected workspaces from the saved BI Sentinel allowlist. Requires explicit confirmation and never expands tenant scope.")]
    public static async Task<GovernanceAuditResult> Scan(
        [System.ComponentModel.Description("Must be true only after the user has reviewed and approved the exact workspace selection, account-level App/Gateway scope if selected, artifact types, and checks.")]
        bool confirmScan,
        IGovernanceAuditConfigurationStore configurationStore,
        IGovernanceAuditHistoryStore historyStore,
        IGovernanceExceptionStore exceptionStore,
        PowerBiGovernanceAuditClient auditClient,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<PowerBiGovernanceMcpTools> logger,
        CancellationToken cancellationToken,
        [System.ComponentModel.Description("Optional subset of administrator-approved workspace GUIDs. Omit to scan all approved workspaces.")]
        Guid[]? workspaceIds = null,
        [System.ComponentModel.Description("Artifact inventories to request. Supported values: Reports, SemanticModels, Dashboards, Apps, Gateways. Apps and Gateways are account-level inventories, not workspace-scoped.")]
        string[]? artifactTypes = null,
        [System.ComponentModel.Description("Versioned check IDs from the catalog. Omit to run all registered checks.")]
        string[]? checkIds = null,
        [System.ComponentModel.Description("Optional installed Power BI app IDs from the global artifact catalog. Omit to include all visible apps when Apps is selected.")]
        Guid[]? appIds = null,
        [System.ComponentModel.Description("Optional Power BI gateway IDs from the global artifact catalog. Omit to include all visible gateways when Gateways is selected.")]
        Guid[]? gatewayIds = null)
    {
        var requestId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var context = httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true ||
            !context.User.IsInRole("ApplicationAdmin"))
        {
            throw new McpException("An authenticated BI Sentinel administrator is required.");
        }

        if (!confirmScan)
        {
            throw new McpException("Set confirmScan to true only after the user has reviewed and approved the requested scan scope.");
        }

        try
        {
            var scanConfiguration = await configurationStore.GetAsync(cancellationToken);
            var maximumWorkspaces = configuration.GetValue<int>(
                "GovernanceAudit:MaximumWorkspacesPerScan",
                50);
            var selectedWorkspaceIds = GovernanceAuditSelection.ValidateWorkspaceIds(
                scanConfiguration.WorkspaceIds,
                workspaceIds,
                maximumWorkspaces);

            logger.LogInformation(
                "MCP governance scan {RequestId} started. Workspaces={WorkspaceIds}; ArtifactTypes={ArtifactTypes}; CheckIds={CheckIds}.",
                requestId,
                string.Join(",", selectedWorkspaceIds),
                artifactTypes is null ? "default" : string.Join(",", artifactTypes),
                checkIds is null ? "all" : string.Join(",", checkIds));

            var result = await auditClient.ScanAsync(
                selectedWorkspaceIds,
                context.User,
                cancellationToken,
                checkIds,
                artifactTypes,
                selectedAppIds: appIds,
                selectedGatewayIds: gatewayIds);
            var scannedBy = context.User.Identity?.Name ?? "BI Sentinel MCP administrator";
            var runId = await historyStore.SaveAsync(result, scannedBy, cancellationToken);
            result = result with { RunId = runId };
            var exceptions = await exceptionStore.GetForFindingsAsync(
                result.Findings,
                cancellationToken);
            result = GovernanceExceptionApplicator.Apply(
                result,
                exceptions,
                DateTimeOffset.UtcNow);

            logger.LogInformation(
                "MCP governance scan {RequestId} completed. RunId={RunId}; Workspaces={WorkspaceCount}; Reports={ReportCount}; Dashboards={DashboardCount}; SemanticModels={SemanticModelCount}; Findings={FindingCount}; DurationMs={DurationMs}.",
                requestId,
                runId,
                result.Workspaces.Count,
                result.Workspaces.Sum(workspace => workspace.Reports.Count),
                result.Workspaces.Sum(workspace => workspace.Dashboards.Count),
                result.Workspaces.Sum(workspace => workspace.SemanticModels.Count),
                result.Findings.Count,
                stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "MCP governance scan {RequestId} rejected an invalid selection.", requestId);
            throw new McpException(exception.Message);
        }
        catch (PowerBiAuditException exception)
        {
            logger.LogWarning(
                exception,
                "MCP governance scan {RequestId} failed while reading Power BI. StatusCode={StatusCode}.",
                requestId,
                (int)exception.StatusCode);
            throw new McpException(exception.Message);
        }
        catch (SqlException exception)
        {
            logger.LogError(
                exception,
                "MCP governance scan {RequestId} failed while reading configuration or saving results. SqlErrorNumber={SqlErrorNumber}.",
                requestId,
                exception.Number);
            throw new McpException("The scan could not be completed or saved. Check BI Sentinel database connectivity, migrations, permissions, and server logs.");
        }
    }

    [McpServerTool(Name = "powerbi_governance_get_saved_run")]
    [System.ComponentModel.Description("Load a persisted Power BI Governance Audit run and its auditable findings by run ID.")]
    public static async Task<GovernanceAuditResult> GetSavedRun(
        [System.ComponentModel.Description("The GUID of a previously persisted Governance Audit run.")]
        Guid runId,
        IGovernanceAuditHistoryStore historyStore,
        IGovernanceExceptionStore exceptionStore,
        ILogger<PowerBiGovernanceMcpTools> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("MCP governance tool invoked: powerbi_governance_get_saved_run. RunId={RunId}.", runId);
            var result = await historyStore.GetAsync(runId, cancellationToken);
            if (result is null)
            {
                throw new McpException($"Governance Audit run '{runId}' was not found.");
            }

            var exceptions = await exceptionStore.GetForFindingsAsync(
                result.Findings,
                cancellationToken);
            return GovernanceExceptionApplicator.Apply(
                result,
                exceptions,
                DateTimeOffset.UtcNow);
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "Loading saved MCP Governance Audit run {RunId} failed with SQL error {SqlErrorNumber}.", runId, exception.Number);
            throw new McpException("The saved scan could not be loaded. Check BI Sentinel database connectivity, migrations, permissions, and server logs.");
        }
    }
}

[McpServerResourceType]
public sealed class PowerBiGovernanceMcpResources
{
    [McpServerResource(
        UriTemplate = "bi-sentinel://governance/check-catalog",
        Name = "Power BI governance check catalog",
        MimeType = "application/json")]
    [System.ComponentModel.Description("Versioned governance checks supported by this BI Sentinel deployment.")]
    public static string GetCheckCatalog(ILogger<PowerBiGovernanceMcpResources> logger)
    {
        logger.LogInformation("MCP governance resource read: bi-sentinel://governance/check-catalog.");
        return JsonSerializer.Serialize(GovernanceCheckCatalog.All, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [McpServerResource(
        UriTemplate = "bi-sentinel://governance/capabilities",
        Name = "Power BI governance MCP capabilities",
        MimeType = "application/json")]
    [System.ComponentModel.Description("Implemented and currently unsupported scanning capabilities; metadata is not inferred.")]
    public static string GetCapabilities(ILogger<PowerBiGovernanceMcpResources> logger)
    {
        logger.LogInformation("MCP governance resource read: bi-sentinel://governance/capabilities.");
        return """
        {
          "implemented": {
            "transport": "MCP Streamable HTTP",
            "workspaceScope": "Administrator-approved BI Sentinel allowlist and per-scan subset; the Governance Audit UI supports name-contains and dedicated/shared-capacity filtering within that allowlist",
            "artifacts": ["WORKSPACE", "REPORT", "DASHBOARD", "SEMANTIC_MODEL", "APP", "GATEWAY"],
            "governanceChecks": ["GOV-001", "GOV-002", "GOV-003", "GOV-004", "GOV-005", "GOV-006", "GOV-007", "GOV-008", "GOV-009"],
            "findings": "Versioned, evidence-bearing findings stored in BI Sentinel SQL history",
            "reportSelection": "All reports in selected workspaces or explicitly selected report IDs",
            "refreshHistory": "GOV-009 evaluates the latest 60 refresh history entries when semantic models are selected and the signed-in user has Power BI Write permission on each model; it does not request or use write permission"
          },
          "notImplemented": [
            "Tenant-wide Admin API and Scanner API inventory",
            "Dataflow and datamart inventory",
            "Workspace filters by department, owner, or business unit",
            "Dynamic report filters for modified date, usage recency, or certification",
            "Consecutive-refresh thresholds and refresh SLA checks",
            "Microsoft Graph owner enabled/deleted verification",
            "Dedicated standards-violation checks for workspace, report, and semantic-model naming or required governance metadata",
            "Business owner, technical owner, data steward, sensitivity label, endorsement, and classification checks",
            "Exception registry and exception approvals",
            "False-positive validation datasets and discrepancy reports",
            "A reproducible stored scan-scope/check-version manifest in each run"
          ],
          "limitations": [
            "The tenant-wide request is intentionally restricted to the administrator-approved workspace allowlist.",
            "Apps and gateways are account-level catalogs, not artifacts proven to belong to a selected workspace.",
            "Power BI refresh history may return HTTP 403 unless the signed-in user already has Write permission on that semantic model; such cases are INSUFFICIENT_EVIDENCE."
          ],
          "safety": "Only metadata returned by configured Power BI APIs is evaluated; unselected or unavailable evidence must not be interpreted as absence."
        }
        """;
    }
}

[McpServerPromptType]
public sealed class PowerBiGovernanceMcpPrompts
{
    [McpServerPrompt(Name = "review_powerbi_governance_run")]
    [System.ComponentModel.Description("Review a saved governance scan without inventing evidence or treating advisory findings as proven violations.")]
    public static string ReviewRun(
        string runId,
        ILogger<PowerBiGovernanceMcpPrompts> logger)
    {
        logger.LogInformation("MCP governance prompt requested: review_powerbi_governance_run.");
        return $"""
        Review saved BI Sentinel Power BI Governance Audit run {runId}.
        First use powerbi_governance_get_saved_run with this run ID.
        Base every conclusion only on returned findings and their recorded API evidence.
        Distinguish FAIL, PASS, INSUFFICIENT_EVIDENCE, and any approved exception status.
        Do not infer missing owners, metadata, or tenant-wide coverage from data that was not returned.
        Summarize scope and API evidence, then group findings by severity and check.
        Call out limitations and candidate findings requiring a governance owner's review.
        """;
    }
}
