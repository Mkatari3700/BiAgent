using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BISentinel.Api.GovernanceAudit;
using BISentinel.Api.ReviewHistory;

namespace BISentinel.Api.Reviews;

public sealed record PowerBiReviewWorkspace(
    Guid Id,
    string Name,
    bool IsReadOnly,
    bool IsOnDedicatedCapacity);

public sealed record PowerBiOnlineWorkspaceScan(
    PbipProjectModel Project,
    int ReportCount,
    int SemanticModelCount,
    int SchemaAvailableModelCount,
    IReadOnlyList<string> EvidenceLimitations);

public sealed record PowerBiOnlineReviewRequest(Guid WorkspaceId);

public sealed record PowerBiOnlineReviewEvidence(
    string WorkspaceName,
    int ReportCount,
    int SemanticModelCount,
    int SchemaAvailableModelCount,
    string[] EvidenceLimitations);

public sealed record SavePowerBiOnlineReviewResponse(
    Guid ReviewRunId,
    DateTime ReviewedUtc,
    DateTime ExpiresUtc,
    PbipReviewResult Review,
    IReadOnlyList<ReviewHistoryFinding> Findings,
    PowerBiOnlineReviewEvidence OnlineEvidence);

public sealed class PowerBiOnlineReviewException(
    string message,
    HttpStatusCode statusCode,
    bool requiresConsent = false,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public bool RequiresConsent { get; } = requiresConsent;
}

public sealed class PowerBiOnlineReviewClient(
    HttpClient httpClient,
    IPowerBiDelegatedAccessTokenProvider tokenProvider)
{
    public static IReadOnlyList<string> RequiredScopes { get; } = Array.AsReadOnly(
    [
        "https://analysis.windows.net/powerbi/api/Workspace.Read.All",
        "https://analysis.windows.net/powerbi/api/Report.Read.All",
        "https://analysis.windows.net/powerbi/api/Dataset.Read.All"
    ]);

    private const int PageSize = 500;
    private const int MaximumWorkspaces = 5_000;
    private const int MaximumDatasetsPerWorkspace = 25;

    public async Task<IReadOnlyList<PowerBiReviewWorkspace>> DiscoverWorkspacesAsync(
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var workspaces = await GetCollectionAsync(
            "groups",
            "https://analysis.windows.net/powerbi/api/Workspace.Read.All",
            user,
            cancellationToken);
        return workspaces.Select(workspace => new PowerBiReviewWorkspace(
            GetGuid(workspace, "id"),
            GetString(workspace, "name"),
            GetBoolean(workspace, "isReadOnly"),
            GetBoolean(workspace, "isOnDedicatedCapacity"))).ToArray();
    }

    public async Task<PowerBiOnlineWorkspaceScan> ScanWorkspaceAsync(
        Guid workspaceId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var workspaces = await DiscoverWorkspacesAsync(user, cancellationToken);
        var workspace = workspaces.FirstOrDefault(item => item.Id == workspaceId);
        if (workspace is null)
        {
            throw new PowerBiOnlineReviewException(
                "The selected workspace is not available to the signed-in Power BI user.",
                HttpStatusCode.Forbidden);
        }

        var workspacePath = $"groups/{workspaceId:D}";
        var datasets = await GetCollectionAsync(
            $"{workspacePath}/datasets",
            "https://analysis.windows.net/powerbi/api/Dataset.Read.All",
            user,
            cancellationToken);
        var reports = await GetCollectionAsync(
            $"{workspacePath}/reports",
            "https://analysis.windows.net/powerbi/api/Report.Read.All",
            user,
            cancellationToken);

        var limitations = new List<string>();
        var models = new List<ModelTable>();
        var relationships = new List<ModelRelationship>();
        var schemaAvailableModels = 0;
        if (datasets.Count > MaximumDatasetsPerWorkspace)
        {
            limitations.Add(
                $"Only the first {MaximumDatasetsPerWorkspace} semantic models were checked; the workspace contains {datasets.Count}.");
        }

        foreach (var dataset in datasets.Take(MaximumDatasetsPerWorkspace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(GetString(dataset, "id"), out var datasetId))
            {
                limitations.Add("A semantic model with an invalid identifier was skipped.");
                continue;
            }

            var datasetName = GetString(dataset, "name");
            try
            {
                using var tableDocument = await GetDocumentAsync(
                    $"{workspacePath}/datasets/{datasetId:D}/tables",
                    "https://analysis.windows.net/powerbi/api/Dataset.Read.All",
                    user,
                    cancellationToken);
                var tableItems = GetArray(tableDocument.RootElement, "tables");
                if (tableItems.Count > 0)
                {
                    schemaAvailableModels++;
                }

                foreach (var table in tableItems)
                {
                    var tableName = GetString(table, "name");
                    var columns = GetArray(table, "columns")
                        .Select(column => new ModelColumn(
                            GetString(column, "name"),
                            GetOptionalString(column, "dataType")))
                        .ToArray();
                    models.Add(new ModelTable(
                        tableName,
                        $"{datasetName}/{tableName}",
                        columns,
                        []));
                }

                using var relationshipDocument = await GetDocumentAsync(
                    $"{workspacePath}/datasets/{datasetId:D}/relationships",
                    "https://analysis.windows.net/powerbi/api/Dataset.Read.All",
                    user,
                    cancellationToken);
                foreach (var relationship in GetArray(
                             relationshipDocument.RootElement,
                             "value",
                             "relationships"))
                {
                    relationships.Add(new ModelRelationship(
                        GetString(relationship, "fromTable"),
                        GetString(relationship, "fromColumn"),
                        GetString(relationship, "toTable"),
                        GetString(relationship, "toColumn"),
                        string.Equals(
                            GetOptionalString(relationship, "crossFilteringBehavior"),
                            "BothDirections",
                            StringComparison.OrdinalIgnoreCase),
                        GetOptionalString(relationship, "fromCardinality"),
                        GetOptionalString(relationship, "toCardinality"),
                        GetOptionalBoolean(relationship, "isActive"),
                        GetOptionalString(relationship, "crossFilteringBehavior")));
                }
            }
            catch (PowerBiOnlineReviewException exception) when (
                exception.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.Forbidden
                    or HttpStatusCode.NotFound)
            {
                limitations.Add(
                    $"Semantic model '{datasetName}' did not expose table metadata through the Power BI REST API.");
            }
        }

        if (schemaAvailableModels < datasets.Count)
        {
            limitations.Add(
                "Table and relationship checks only run for semantic models that expose schema through the Power BI REST API. Import models may require XMLA or tenant metadata-scanning access.");
        }

        limitations.Add(
            "Online scans do not retrieve DAX measures, report visual definitions, refresh history, or model partitions. Upload a PBIP/PBIR project for those checks.");

        var reportIds = reports
            .Select(report => GetOptionalString(report, "datasetId"))
            .Where(datasetId => !string.IsNullOrWhiteSpace(datasetId))
            .Select(datasetId => $"dataset:{datasetId}")
            .ToArray();
        return new PowerBiOnlineWorkspaceScan(
            new PbipProjectModel(
                workspace.Name,
                [],
                models,
                relationships,
                HasSemanticModel: datasets.Count > 0,
                ReportReferences: reportIds),
            reports.Count,
            datasets.Count,
            schemaAvailableModels,
            limitations.Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<IReadOnlyList<JsonElement>> GetCollectionAsync(
        string relativeUri,
        string scope,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var items = new List<JsonElement>();
        for (var skip = 0; ; skip += PageSize)
        {
            using var document = await GetDocumentAsync(
                $"{relativeUri}?$top={PageSize}&$skip={skip}",
                scope,
                user,
                cancellationToken);
            var page = GetArray(document.RootElement, "value");
            items.AddRange(page);
            if (page.Count < PageSize)
            {
                return items;
            }

            if (items.Count >= MaximumWorkspaces)
            {
                throw new PowerBiOnlineReviewException(
                    $"Power BI workspace discovery reached the {MaximumWorkspaces} item safety limit.",
                    HttpStatusCode.BadGateway);
            }
        }
    }

    private async Task<JsonDocument> GetDocumentAsync(
        string relativeUri,
        string scope,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        string accessToken;
        try
        {
            accessToken = await tokenProvider.GetAccessTokenAsync(user, scope);
        }
        catch (PowerBiDelegatedConsentRequiredException exception)
        {
            throw new PowerBiOnlineReviewException(
                "Power BI read consent is required. Authorize the requested delegated read scopes and try again.",
                HttpStatusCode.Unauthorized,
                requiresConsent: true,
                exception);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    "The Power BI access token is no longer valid. Reauthorize Power BI access and retry.",
                HttpStatusCode.Forbidden =>
                    "The signed-in Power BI user does not have permission to read this workspace or semantic model.",
                (HttpStatusCode)429 =>
                    "Power BI throttled the request. Wait a few minutes and retry.",
                _ => $"Power BI could not return the requested workspace data (HTTP {(int)response.StatusCode})."
            };
            throw new PowerBiOnlineReviewException(detail, response.StatusCode);
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new PowerBiOnlineReviewException(
                "Power BI returned an invalid response for the online project review.",
                HttpStatusCode.BadGateway,
                innerException: exception);
        }
    }

    private static IReadOnlyList<JsonElement> GetArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().Select(item => item.Clone()).ToArray();
            }
        }

        return [];
    }

    private static Guid GetGuid(JsonElement element, string name) =>
        Guid.TryParse(GetString(element, name), out var value)
            ? value
            : throw new PowerBiOnlineReviewException(
                $"Power BI returned a workspace without a valid {name}.",
                HttpStatusCode.BadGateway);

    private static string GetString(JsonElement element, string name) =>
        GetOptionalString(element, name) ?? string.Empty;

    private static string? GetOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string name) =>
        GetOptionalBoolean(element, name) ?? false;

    private static bool? GetOptionalBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
