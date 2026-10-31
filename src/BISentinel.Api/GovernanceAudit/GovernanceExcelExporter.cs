using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace BISentinel.Api.GovernanceAudit;

public static class GovernanceExcelExporter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypeNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] CreateWorkbook(GovernanceAuditResult result)
    {
        var sheets = new[]
        {
            new Worksheet("Findings", CreateFindingRows(result)),
            new Worksheet("Run Summary", CreateRunSummaryRows(result)),
            new Worksheet("Workspaces", CreateWorkspaceRows(result)),
            new Worksheet("Workspace Access", CreateAccessRows(result)),
            new Worksheet("Artifacts", CreateArtifactRows(result)),
            new Worksheet("Connections", CreateConnectionRows(result)),
            new Worksheet("Check Reference", CreateCheckReferenceRows())
        };
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", writer => WriteContentTypes(writer, sheets.Length));
            WriteEntry(archive, "_rels/.rels", WriteRootRelationships);
            WriteEntry(archive, "xl/workbook.xml", writer => WriteWorkbook(writer, sheets));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", writer => WriteWorkbookRelationships(writer, sheets.Length));
            for (var index = 0; index < sheets.Length; index++)
            {
                var rows = sheets[index].Rows;
                WriteEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", writer => WriteWorksheet(writer, rows));
            }
        }

        return output.ToArray();
    }

    private static IReadOnlyList<string[]> CreateFindingRows(GovernanceAuditResult result)
    {
        var rows = new List<string[]>
        {
            new[]
            {
                "Check ID", "Check version", "Check name", "Severity", "Status", "Original status",
                "Workspace", "Workspace ID", "Artifact type", "Artifact ID", "Artifact name", "Finding",
                "Explanation", "Evidence source", "Evidence timestamp (UTC)", "Expected value",
                "Observed value", "Evaluation logic", "Exception rules", "Rule owner", "Exception status",
                "Exception owner", "Exception reason", "Exception expiry (UTC)", "Evidence (JSON)"
            }
        };
        rows.AddRange(result.Findings.Select(finding => new[]
        {
            finding.CheckId,
            finding.CheckVersion,
            finding.CheckName,
            finding.Severity,
            finding.Status,
            finding.OriginalStatus ?? string.Empty,
            finding.WorkspaceName,
            finding.WorkspaceId.ToString("D"),
            finding.ArtifactType ?? string.Empty,
            finding.ArtifactId?.ToString("D") ?? string.Empty,
            finding.ArtifactName ?? string.Empty,
            finding.Title,
            finding.Detail,
            finding.EvidenceSource,
            finding.EvidenceTimestamp.ToUniversalTime().ToString("O"),
            finding.ExpectedValue ?? string.Empty,
            finding.ActualValue ?? string.Empty,
            finding.EvaluationLogic,
            finding.ExceptionRules,
            finding.RuleOwner,
            finding.ExceptionRecord?.Status ?? string.Empty,
            finding.ExceptionRecord?.Owner ?? string.Empty,
            finding.ExceptionRecord?.Reason ?? string.Empty,
            finding.ExceptionRecord?.ExpiresUtc.ToUniversalTime().ToString("O") ?? string.Empty,
            finding.Evidence.ValueKind == JsonValueKind.Undefined ? string.Empty : finding.Evidence.GetRawText()
        }));
        return rows;
    }

    private static IReadOnlyList<string[]> CreateRunSummaryRows(GovernanceAuditResult result)
    {
        var findingsByStatus = result.Findings
            .GroupBy(finding => finding.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<string[]>
        {
            new[] { "Run ID", result.RunId.ToString("D") },
            new[] { "Scanned at (UTC)", result.ScannedUtc.ToUniversalTime().ToString("O") },
            new[] { "Scanned by", result.ScannedBy ?? "Not recorded" },
            new[] { "Workspaces scanned", result.Workspaces.Count.ToString() },
            new[] { "Reports returned", result.Workspaces.Sum(audit => audit.Reports.Count).ToString() },
            new[] { "Dashboards returned", result.Workspaces.Sum(audit => audit.Dashboards.Count).ToString() },
            new[] { "Semantic models returned", result.Workspaces.Sum(audit => audit.SemanticModels.Count).ToString() },
            new[] { "Apps returned", result.Apps.Count.ToString() },
            new[] { "Gateways returned", result.Gateways.Count.ToString() },
            new[] { "Findings", result.Findings.Count.ToString() },
            new[] { "Needs review (FAIL)", findingsByStatus.GetValueOrDefault("FAIL", 0).ToString() },
            new[] { "Evidence gaps", findingsByStatus.GetValueOrDefault("INSUFFICIENT_EVIDENCE", 0).ToString() },
            new[] { "Approved exceptions", findingsByStatus.GetValueOrDefault("EXCEPTION_APPROVED", 0).ToString() },
            new[] { "Passed checks", findingsByStatus.GetValueOrDefault("PASS", 0).ToString() },
            new[]
            {
                "Artifact types returned",
                string.Join(", ", result.Workspaces
                    .SelectMany(audit => audit.Reports
                        .Concat(audit.Dashboards)
                        .Concat(audit.SemanticModels))
                    .Select(artifact => artifact.Type)
                    .Concat(result.Apps.Select(artifact => artifact.Type))
                    .Concat(result.Gateways.Select(artifact => artifact.Type))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(type => type, StringComparer.OrdinalIgnoreCase))
            },
            Array.Empty<string>(),
            new[] { "Workspace", "Workspace ID", "Hosting", "Reports", "Dashboards", "Semantic models", "Principals" }
        };
        rows.AddRange(result.Workspaces.Select(audit => new[]
        {
            audit.Workspace.Name,
            audit.Workspace.Id.ToString("D"),
            audit.Workspace.IsOnDedicatedCapacity ? "Dedicated" : "Shared",
            audit.Reports.Count.ToString(),
            audit.Dashboards.Count.ToString(),
            audit.SemanticModels.Count.ToString(),
            audit.Users.Count.ToString()
        }));
        return rows;
    }

    private static IReadOnlyList<string[]> CreateCheckReferenceRows()
    {
        var rows = new List<string[]>
        {
            new[]
            {
                "Check ID", "Version", "Check name", "Severity", "Owner", "Purpose", "Evaluation logic",
                "Evidence source", "Exception guidance", "Expected result"
            }
        };
        rows.AddRange(GovernanceCheckCatalog.All.Select(check => new[]
        {
            check.CheckId,
            check.Version,
            check.Name,
            check.Severity,
            check.Owner,
            check.Description,
            check.EvaluationLogic,
            check.EvidenceSource,
            check.ExceptionRules,
            check.ExpectedResult
        }));
        return rows;
    }

    private static IReadOnlyList<string[]> CreateWorkspaceRows(GovernanceAuditResult result)
    {
        var rows = new List<string[]>
        {
            new[] { "Workspace", "Workspace ID", "Read only", "On dedicated capacity", "Reports", "Dashboards", "Semantic models", "Principals" }
        };
        rows.AddRange(result.Workspaces.Select(audit => new[]
        {
            audit.Workspace.Name,
            audit.Workspace.Id.ToString("D"),
            audit.Workspace.IsReadOnly.ToString(),
            audit.Workspace.IsOnDedicatedCapacity.ToString(),
            audit.Reports.Count.ToString(),
            audit.Dashboards.Count.ToString(),
            audit.SemanticModels.Count.ToString(),
            audit.Users.Count.ToString()
        }));
        return rows;
    }

    private static IReadOnlyList<string[]> CreateAccessRows(GovernanceAuditResult result)
    {
        var rows = new List<string[]>
        {
            new[] { "Workspace", "Workspace ID", "Display name", "Email", "Identifier", "Principal type", "Access role" }
        };
        rows.AddRange(result.Workspaces.SelectMany(audit => audit.Users.Select(user => new[]
        {
            audit.Workspace.Name,
            audit.Workspace.Id.ToString("D"),
            user.DisplayName ?? string.Empty,
            user.EmailAddress ?? string.Empty,
            user.Identifier,
            user.PrincipalType,
            user.AccessRight
        })));
        return rows;
    }

    private static IReadOnlyList<string[]> CreateArtifactRows(GovernanceAuditResult result)
    {
        var rows = new List<string[]>
        {
            new[] { "Workspace", "Workspace ID", "Artifact type", "Artifact name", "Artifact ID", "Semantic model ID", "Configured by", "Status", "URL" }
        };
        rows.AddRange(result.Workspaces.SelectMany(audit => audit.Reports
            .Concat(audit.SemanticModels)
            .Concat(audit.Dashboards)
            .Select(artifact => new[]
            {
                audit.Workspace.Name,
                audit.Workspace.Id.ToString("D"),
                artifact.Type,
                artifact.Name,
                artifact.Id.ToString("D"),
                artifact.DatasetId ?? string.Empty,
                artifact.ConfiguredBy ?? string.Empty,
                artifact.Status ?? string.Empty,
                artifact.WebUrl ?? string.Empty
            })));
        rows.AddRange(result.Apps.Concat(result.Gateways).Select(artifact => new[]
        {
            "Account-level",
            string.Empty,
            artifact.Type,
            artifact.Name,
            artifact.Id.ToString("D"),
            artifact.DatasetId ?? string.Empty,
            artifact.ConfiguredBy ?? string.Empty,
            artifact.Status ?? string.Empty,
            artifact.WebUrl ?? string.Empty
        }));
        return rows;
    }

    private static IReadOnlyList<string[]> CreateConnectionRows(GovernanceAuditResult result)
    {
        var rows = new List<string[]>
        {
            new[]
            {
                "Workspace", "Report", "Semantic model", "Source type", "Connection details", "Gateway ID",
                "Connection status", "Connection status detail", "Gateway status", "Gateway status detail"
            }
        };
        rows.AddRange(result.Workspaces.SelectMany(audit => audit.ReportConnections.Select(connection => new[]
        {
            audit.Workspace.Name,
            connection.ReportName,
            connection.SemanticModelName ?? string.Empty,
            connection.SourceType ?? string.Empty,
            connection.ConnectionDetails ?? string.Empty,
            connection.GatewayId?.ToString("D") ?? string.Empty,
            connection.Status,
            connection.StatusDetail,
            connection.GatewayStatus,
            connection.GatewayStatusDetail
        })));
        return rows;
    }

    private static void WriteContentTypes(XmlWriter writer, int sheetCount)
    {
        writer.WriteStartElement("Types", ContentTypeNamespace);
        writer.WriteStartElement("Default", ContentTypeNamespace);
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Default", ContentTypeNamespace);
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Override", ContentTypeNamespace);
        writer.WriteAttributeString("PartName", "/xl/workbook.xml");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        writer.WriteEndElement();
        for (var index = 1; index <= sheetCount; index++)
        {
            writer.WriteStartElement("Override", ContentTypeNamespace);
            writer.WriteAttributeString("PartName", $"/xl/worksheets/sheet{index}.xml");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteRootRelationships(XmlWriter writer)
    {
        writer.WriteStartElement("Relationships", PackageRelationshipNamespace);
        writer.WriteStartElement("Relationship", PackageRelationshipNamespace);
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", $"{RelationshipNamespace}/officeDocument");
        writer.WriteAttributeString("Target", "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorkbook(XmlWriter writer, IReadOnlyList<Worksheet> sheets)
    {
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipNamespace);
        writer.WriteStartElement("sheets", SpreadsheetNamespace);
        for (var index = 0; index < sheets.Count; index++)
        {
            writer.WriteStartElement("sheet", SpreadsheetNamespace);
            writer.WriteAttributeString("name", sheets[index].Name);
            writer.WriteAttributeString("sheetId", (index + 1).ToString());
            writer.WriteAttributeString("r", "id", RelationshipNamespace, $"rId{index + 1}");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorkbookRelationships(XmlWriter writer, int sheetCount)
    {
        writer.WriteStartElement("Relationships", PackageRelationshipNamespace);
        for (var index = 1; index <= sheetCount; index++)
        {
            writer.WriteStartElement("Relationship", PackageRelationshipNamespace);
            writer.WriteAttributeString("Id", $"rId{index}");
            writer.WriteAttributeString("Type", $"{RelationshipNamespace}/worksheet");
            writer.WriteAttributeString("Target", $"worksheets/sheet{index}.xml");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteWorksheet(XmlWriter writer, IReadOnlyList<string[]> rows)
    {
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetData", SpreadsheetNamespace);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            writer.WriteStartElement("row", SpreadsheetNamespace);
            writer.WriteAttributeString("r", (rowIndex + 1).ToString());
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                writer.WriteStartElement("c", SpreadsheetNamespace);
                writer.WriteAttributeString("r", $"{GetColumnName(columnIndex + 1)}{rowIndex + 1}");
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is", SpreadsheetNamespace);
                writer.WriteStartElement("t", SpreadsheetNamespace);
                writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                writer.WriteString(TruncateCell(rows[rowIndex][columnIndex]));
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteEntry(ZipArchive archive, string name, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false
        });
        write(writer);
    }

    private static string GetColumnName(int columnNumber)
    {
        var name = new StringBuilder();
        while (columnNumber > 0)
        {
            columnNumber--;
            name.Insert(0, (char)('A' + columnNumber % 26));
            columnNumber /= 26;
        }
        return name.ToString();
    }

    private static string TruncateCell(string value) =>
        value.Length <= 32_767 ? value : value[..32_764] + "...";

    private sealed record Worksheet(string Name, IReadOnlyList<string[]> Rows);
}
