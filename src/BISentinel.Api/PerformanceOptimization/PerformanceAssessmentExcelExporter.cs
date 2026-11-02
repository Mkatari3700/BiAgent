using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace BISentinel.Api.PerformanceOptimization;

public sealed class PerformanceAssessmentExcelExporter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const int MaximumCellLength = 32_000;

    public byte[] Export(PerformanceAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var sheets = CreateSheets(assessment);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", writer => WriteContentTypes(writer, sheets.Count));
            WriteEntry(archive, "_rels/.rels", WriteRootRelationships);
            WriteEntry(archive, "xl/workbook.xml", writer => WriteWorkbook(writer, sheets));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", writer => WriteWorkbookRelationships(writer, sheets.Count));
            for (var index = 0; index < sheets.Count; index++)
            {
                var sheet = sheets[index];
                WriteEntry(
                    archive,
                    $"xl/worksheets/sheet{index + 1}.xml",
                    writer => WriteWorksheet(writer, sheet));
            }
        }

        return output.ToArray();
    }

    private static IReadOnlyList<WorksheetData> CreateSheets(PerformanceAssessment assessment)
    {
        var summary = assessment.ExecutiveSummary;
        var scope = assessment.ScopeAnalyzed;
        var overviewRows = new[]
        {
            Row("Project", assessment.ProjectName),
            Row("Artifact type", assessment.ArtifactType),
            Row("Analyzed at (UTC)", assessment.AnalyzedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            Row("Static patterns", summary.ConfirmedStaticFindings),
            Row("Improvement hypotheses", summary.Hypotheses),
            Row("Measured issues", summary.MeasuredIssues),
            Row("Semantic model included", scope.SemanticModelIdentified),
            Row("Report definitions included", scope.ReportDefinitionsFound),
            Row("Reports", scope.Reports),
            Row("Tables", scope.Tables),
            Row("Columns", scope.Columns),
            Row("Measures", scope.Measures),
            Row("Relationships", scope.Relationships),
            Row("Partitions", scope.Partitions),
            Row("Pages", scope.Pages),
            Row("Visuals", scope.Visuals),
            Row("Conclusion", summary.PerformanceConclusion),
            Row("Analysis boundary", scope.AnalysisBoundary),
            Row("Data handling", "Generated on demand for download. This assessment is not saved by BI Sentinel.")
        };

        var findingRows = assessment.Findings
            .Select(finding => Row(
                finding.Priority,
                finding.Category,
                finding.Finding,
                finding.Impact,
                finding.EvidenceType,
                finding.Confidence,
                finding.Evidence,
                finding.Recommendation,
                finding.ExpectedBenefit,
                finding.RiskOfChange,
                finding.OptimizationTag,
                finding.Severity))
            .ToArray();
        var improvementRows = assessment.Findings
            .Select((finding, index) => (finding, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.finding.Recommendation))
            .OrderBy(item => PriorityRank(item.finding.Priority))
            .ThenBy(item => item.index)
            .Select((item, index) => Row(
                $"P{PriorityRank(item.finding.Priority) + 1}",
                (index + 1).ToString(CultureInfo.InvariantCulture),
                item.finding.Priority,
                item.finding.Category,
                item.finding.Finding,
                item.finding.Recommendation,
                item.finding.Impact,
                item.finding.Evidence,
                item.finding.ExpectedBenefit,
                item.finding.Confidence,
                item.finding.RiskOfChange))
            .ToArray();
        var daxRows = assessment.DaxInventory
            .SelectMany(item => item.Issues.Count == 0
                ? [Row(item.TableName, item.MeasureName, item.ObjectType, item.Expression, "", "", "", "", "", "")]
                : item.Issues.Select(issue => Row(
                    item.TableName,
                    item.MeasureName,
                    item.ObjectType,
                    item.Expression,
                    issue.Severity,
                    issue.Issue,
                    issue.TechnicalExplanation,
                    issue.RecommendedRewritePattern,
                    issue.RiskOfChange,
                    $"{issue.Confidence}; {issue.EvidenceType}")))
            .ToArray();
        var modelRows = assessment.ModelInventory.Tables
            .Select(table => Row(
                table.Name,
                table.Artifact,
                table.Columns,
                string.Join("; ", table.ColumnInventory.Select(column =>
                    $"{column.Name} ({column.DataType ?? "type unknown"}{(column.IsCalculated ? ", calculated" : "")}{(column.IsHidden ? ", hidden" : "")})")),
                table.Measures,
                table.CalculatedColumns,
                table.IsCalculatedTable,
                string.Join("; ", table.Partitions),
                string.Join("; ", table.Hierarchies)))
            .ToArray();
        var relationshipRows = assessment.ModelInventory.Relationships
            .Select(relationship => Row(
                relationship.From,
                relationship.To,
                relationship.FromCardinality,
                relationship.ToCardinality,
                relationship.IsActive,
                relationship.FilterDirection,
                relationship.Evidence))
            .ToArray();
        var reportRows = assessment.ReportInventory
            .Select(report => Row(
                report.ReportName,
                report.PageName,
                report.VisualCount,
                report.SlicerCount,
                report.CustomVisualCount,
                report.HasBookmarks,
                report.HasDrillthrough,
                report.HasTooltipPages,
                report.HasConditionalFormatting,
                report.HasCrossFilteringConfiguration,
                string.Join("; ", report.PotentialBottlenecks),
                string.Join("; ", report.Opportunities),
                report.Evidence))
            .ToArray();
        var evidenceRows = new List<string[]>();
        evidenceRows.AddRange(assessment.EvidenceGaps.Select(gap => Row(
            "Evidence gap",
            gap.Priority,
            gap.Category,
            gap.MissingEvidence,
            gap.WhyRequired,
            gap.CollectionMethod)));
        evidenceRows.AddRange(assessment.AcceptanceGates.Select(gate => Row(
            "Acceptance gate",
            gate.Passed ? "Passed" : "Blocked",
            gate.Gate,
            gate.Status,
            string.Join("; ", gate.MissingArtifacts),
            string.Join("; ", gate.NextActions))));
        evidenceRows.AddRange(assessment.RecommendedNextSteps.Select(step => Row(
            "Recommended next step", "", "", step, "", "")));
        var opportunityRows = assessment.PotentialOptimizationCandidates
            .Select(opportunity => Row(
                opportunity.Tag,
                opportunity.Classification,
                opportunity.Reason,
                opportunity.RequiredValidation,
                opportunity.AcceptanceCriteria,
                opportunity.Risk,
                opportunity.Confidence))
            .ToArray();

        return
        [
            new("Overview", ["Metric", "Assessment"], overviewRows),
            new("Key Improvements", ["Priority", "Order", "Source priority", "Category", "Finding", "Recommended action", "Why it matters", "Evidence", "Expected benefit", "Confidence", "Risk"], improvementRows),
            new("Findings", ["Priority", "Category", "Finding", "Impact", "Evidence type", "Confidence", "Evidence", "Recommendation", "Expected benefit", "Risk of change", "Optimization tag", "Severity"], findingRows),
            new("DAX Inventory", ["Table", "Measure or object", "Object type", "Expression", "Severity", "Issue", "Technical explanation", "Suggested rewrite", "Risk of change", "Confidence / evidence"], daxRows),
            new("Model Inventory", ["Table", "Artifact", "Columns", "Column inventory", "Measures", "Calculated columns", "Calculated table", "Partitions", "Hierarchies"], modelRows),
            new("Relationships", ["From", "To", "From cardinality", "To cardinality", "Active", "Filter direction", "Evidence"], relationshipRows),
            new("Report Inventory", ["Report", "Page", "Visuals", "Slicers", "Custom visuals", "Bookmarks", "Drillthrough", "Tooltip pages", "Conditional formatting", "Cross-filtering", "Potential bottlenecks", "Opportunities", "Evidence"], reportRows),
            new("Evidence and Gates", ["Record type", "Priority / status", "Category / gate", "Missing evidence / status", "Why / missing artifacts", "Collection method / next actions"], evidenceRows),
            new("Optimization Options", ["Tag", "Classification", "Reason", "Required validation", "Acceptance criteria", "Risk", "Confidence"], opportunityRows)
        ];
    }

    private static int PriorityRank(string? priority)
    {
        if (priority?.StartsWith("High", StringComparison.OrdinalIgnoreCase) == true) return 0;
        if (priority?.StartsWith("Medium", StringComparison.OrdinalIgnoreCase) == true) return 1;
        if (priority?.StartsWith("Low", StringComparison.OrdinalIgnoreCase) == true) return 2;
        return 3;
    }

    private static string[] Row(params object?[] values) =>
        values.Select(value => value switch
        {
            null => "",
            bool boolean => boolean ? "Yes" : "No",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        }).ToArray();

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

    private static void WriteContentTypes(XmlWriter writer, int sheetCount)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Types", ContentTypesNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipsNamespace);
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "rels");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Default");
        writer.WriteAttributeString("Extension", "xml");
        writer.WriteAttributeString("ContentType", "application/xml");
        writer.WriteEndElement();
        writer.WriteStartElement("Override");
        writer.WriteAttributeString("PartName", "/xl/workbook.xml");
        writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        writer.WriteEndElement();
        for (var index = 1; index <= sheetCount; index++)
        {
            writer.WriteStartElement("Override");
            writer.WriteAttributeString("PartName", $"/xl/worksheets/sheet{index}.xml");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRootRelationships(XmlWriter writer)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", PackageRelationshipsNamespace);
        writer.WriteStartElement("Relationship");
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", $"{RelationshipsNamespace}/officeDocument");
        writer.WriteAttributeString("Target", "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbook(XmlWriter writer, IReadOnlyList<WorksheetData> sheets)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipsNamespace);
        writer.WriteStartElement("sheets");
        for (var index = 0; index < sheets.Count; index++)
        {
            writer.WriteStartElement("sheet");
            writer.WriteAttributeString("name", sheets[index].Name);
            writer.WriteAttributeString("sheetId", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("r", "id", RelationshipsNamespace, $"rId{index + 1}");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorkbookRelationships(XmlWriter writer, int sheetCount)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("Relationships", PackageRelationshipsNamespace);
        for (var index = 1; index <= sheetCount; index++)
        {
            writer.WriteStartElement("Relationship");
            writer.WriteAttributeString("Id", $"rId{index}");
            writer.WriteAttributeString("Type", $"{RelationshipsNamespace}/worksheet");
            writer.WriteAttributeString("Target", $"worksheets/sheet{index}.xml");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteWorksheet(XmlWriter writer, WorksheetData sheet)
    {
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetData");
        WriteRow(writer, 1, sheet.Headers);
        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            WriteRow(writer, index + 2, sheet.Rows[index]);
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter writer, int rowIndex, IReadOnlyList<string> values)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowIndex.ToString(CultureInfo.InvariantCulture));
        for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", $"{ColumnName(columnIndex + 1)}{rowIndex}");
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is");
            writer.WriteStartElement("t");
            writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            var value = values[columnIndex] ?? "";
            writer.WriteString(value.Length <= MaximumCellLength
                ? value
                : value[..MaximumCellLength] + " [truncated]");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string ColumnName(int column)
    {
        var name = new StringBuilder();
        while (column > 0)
        {
            column--;
            name.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }
        return name.ToString();
    }

    private sealed record WorksheetData(string Name, string[] Headers, IReadOnlyList<string[]> Rows);
}
