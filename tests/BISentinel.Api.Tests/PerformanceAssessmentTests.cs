using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using BISentinel.Api.PerformanceOptimization;
using BISentinel.Api.Reviews;

namespace BISentinel.Api.Tests;

public sealed class PerformanceAssessmentTests
{
    [Fact]
    public void Reader_ParsesBimModelInventoryProperties()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "Model.bim");
            File.WriteAllText(path,
                """
                {
                  "model": {
                    "name": "SalesModel",
                    "tables": [
                      {
                        "name": "FactSales",
                        "columns": [
                          { "name": "Amount", "dataType": "double" },
                          { "name": "Margin", "type": "calculated", "dataType": "double", "expression": "[Amount] * 0.1" }
                        ],
                        "measures": [
                          { "name": "Total Sales", "expression": "SUM(FactSales[Amount])" }
                        ],
                        "partitions": [
                          { "name": "FactSales", "mode": "import", "source": { "type": "m" } }
                        ],
                        "hierarchies": [
                          { "name": "Geography", "levels": [{ "name": "Country" }, { "name": "City" }] }
                        ]
                      },
                      { "name": "DimCustomer", "columns": [{ "name": "CustomerKey" }] }
                    ],
                    "relationships": [
                      {
                        "fromTable": "FactSales",
                        "fromColumn": "CustomerKey",
                        "toTable": "DimCustomer",
                        "toColumn": "CustomerKey",
                        "fromCardinality": "many",
                        "toCardinality": "one",
                        "isActive": true,
                        "crossFilteringBehavior": "bothDirections"
                      }
                    ]
                  }
                }
                """);

            var model = new PbipProjectReader().Read(path);

            Assert.Equal("SalesModel", model.ProjectName);
            Assert.Equal(2, model.Tables.Count);
            var factSales = Assert.Single(model.Tables, table => table.Name == "FactSales");
            Assert.True(factSales.Columns.Single(column => column.Name == "Margin").IsCalculated);
            Assert.Equal("double", factSales.Columns.Single(column => column.Name == "Amount").DataType);
            Assert.Equal("import", Assert.Single(factSales.Partitions).Mode);
            Assert.Equal(2, Assert.Single(factSales.Hierarchies).Levels.Count);
            var relationship = Assert.Single(model.Relationships);
            Assert.Equal("many", relationship.FromCardinality);
            Assert.Equal("one", relationship.ToCardinality);
            Assert.True(relationship.IsActive);
            Assert.True(relationship.IsBidirectional);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Assessment_SeparatesStaticFindingsFromTelemetryGapsAndTracksGates()
    {
        using var fixture = new AssessmentProjectFixture();
        var assessment = new PerformanceAssessmentService(new PbipProjectReader())
            .Assess(fixture.Root);

        Assert.Equal("Demo", assessment.ProjectName);
        Assert.Equal(1, assessment.ScopeAnalyzed.Measures);
        Assert.Equal(1, assessment.ScopeAnalyzed.Pages);
        Assert.Equal(2, assessment.ScopeAnalyzed.Visuals);
        Assert.Contains(assessment.DaxInventory.Single().Issues, issue =>
            issue.Issue == "SUMX iterates a direct column reference" &&
            issue.Confidence == "High");
        Assert.Contains(assessment.PotentialOptimizationCandidates, opportunity =>
            opportunity.Tag == "OPT-001" &&
            opportunity.Classification.Contains("low risk, high confidence", StringComparison.Ordinal));
        Assert.Contains(assessment.PotentialOptimizationCandidates, opportunity =>
            opportunity.Tag == "OPT-003" &&
            opportunity.Classification.Contains("telemetry or benchmarking", StringComparison.Ordinal));
        Assert.Equal(0, assessment.ExecutiveSummary.MeasuredIssues);
        Assert.Contains("no performance gain is claimed", assessment.ExecutiveSummary.PerformanceConclusion, StringComparison.OrdinalIgnoreCase);
        Assert.False(assessment.AcceptanceGates.Single(gate => gate.Gate.StartsWith("PO-2", StringComparison.Ordinal)).Passed);
        Assert.True(assessment.AcceptanceGates.Single(gate => gate.Gate.StartsWith("PO-1", StringComparison.Ordinal)).Passed);
        Assert.False(assessment.AcceptanceGates.Single(gate => gate.Gate.StartsWith("PO-3", StringComparison.Ordinal)).Passed);
        Assert.Contains(assessment.EvidenceGaps, gap => gap.MissingEvidence == "Model memory footprint");
        Assert.Contains(assessment.ReportInventory, page => page.PageName == "Sales" && page.SlicerCount == 1);
    }

    [Fact]
    public void Reader_ParsesStandaloneDaxFileAsAnUnassociatedExpression()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "TotalSales.dax");
            File.WriteAllText(path, "SUMX(FactSales, FactSales[Amount])");

            var model = new PbipProjectReader().Read(path);

            Assert.False(model.HasSemanticModel);
            Assert.Equal("TotalSales", Assert.Single(model.Tables.Single().Measures).Name);
            Assert.Contains("SUMX", model.Tables.Single().Measures.Single().Expression, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InputService_AssessesDirectBimAndRemovesTemporaryUpload()
    {
        var sourceRoot = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(sourceRoot, "Model.bim");
            File.WriteAllText(sourcePath,
                """{"model":{"name":"Sales","tables":[{"name":"FactSales","columns":[],"measures":[]}]}}""");
            await using var uploadStream = new MemoryStream(await File.ReadAllBytesAsync(sourcePath));
            var upload = new FormFile(uploadStream, 0, uploadStream.Length, "file", "Model.bim");
            string? processedPath = null;
            var input = CreateInputService();

            var assessment = await input.ProcessArtifactAsync(
                upload,
                allowLooseArtifacts: true,
                (path, name, _) =>
                {
                    processedPath = path;
                    return Task.FromResult(new PerformanceAssessmentService(new PbipProjectReader())
                        .Assess(path, name, ".bim"));
                },
                CancellationToken.None);

            Assert.Equal("Sales", assessment.ProjectName);
            Assert.Equal(1, assessment.ScopeAnalyzed.Tables);
            Assert.NotNull(processedPath);
            Assert.False(File.Exists(processedPath));
        }
        finally
        {
            Directory.Delete(sourceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InputService_AssessesZippedTmdlAndReportDefinitionsTogether()
    {
        await using var uploadStream = new MemoryStream();
        using (var archive = new ZipArchive(uploadStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "Model/tables/FactSales.tmdl", "table FactSales\n    column Amount");
            WriteEntry(archive, "Report/definition.pbir", "{}");
            WriteEntry(archive, "Report/definition/report.json", "{}");
            WriteEntry(archive, "Report/definition/pages/pages.json", """{"pageOrder":["page1"]}""");
            WriteEntry(archive, "Report/definition/pages/page1/page.json", """{"displayName":"Sales"}""");
            WriteEntry(archive, "Report/definition/pages/page1/visuals/visual1/visual.json",
                """{"visual":{"visualType":"barChart"}}""");
        }
        uploadStream.Position = 0;
        var upload = new FormFile(uploadStream, 0, uploadStream.Length, "file", "Demo.zip");
        string? processedPath = null;
        var input = CreateInputService();

        var assessment = await input.ProcessArtifactAsync(
            upload,
            allowLooseArtifacts: true,
            (path, name, _) =>
            {
                processedPath = path;
                return Task.FromResult(new PerformanceAssessmentService(new PbipProjectReader())
                    .Assess(path, name, ".zip"));
            },
            CancellationToken.None);

        Assert.Equal(1, assessment.ScopeAnalyzed.Tables);
        Assert.True(assessment.ScopeAnalyzed.ReportDefinitionsFound);
        Assert.Equal("Sales", Assert.Single(assessment.ReportInventory).PageName);
        Assert.True(assessment.AcceptanceGates.Single(gate =>
            gate.Gate.StartsWith("PO-0", StringComparison.Ordinal)).Passed);
        Assert.NotNull(processedPath);
        Assert.False(Directory.Exists(processedPath));
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "BISentinel.PerformanceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static PbipProjectInputService CreateInputService() =>
        new(
            new UnusedReviewer(),
            new PbiToolsExtractor(new TestOptionsMonitor<ReviewOptions>(new ReviewOptions())));

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private sealed class UnusedReviewer : IPbipProjectReviewer
    {
        public Task<PbipReviewResult> ReviewAsync(
            string projectPath,
            string projectId,
            string? projectName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PbipReviewResult> ReviewOnlineModelAsync(
            PbipProjectModel project,
            string projectId,
            IReadOnlySet<string> unavailableEvidenceRuleKeys,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class AssessmentProjectFixture : IDisposable
    {
        public AssessmentProjectFixture()
        {
            Root = CreateTemporaryDirectory();
            var report = Path.Combine(Root, "Demo.Report");
            var reportDefinition = Path.Combine(report, "definition");
            var page = Path.Combine(reportDefinition, "pages", "page1");
            var visualRoot = Path.Combine(page, "visuals");
            var semanticModel = Path.Combine(Root, "Demo.SemanticModel");
            var tables = Path.Combine(semanticModel, "definition", "tables");
            Directory.CreateDirectory(Path.Combine(visualRoot, "visual1"));
            Directory.CreateDirectory(Path.Combine(visualRoot, "visual2"));
            Directory.CreateDirectory(tables);

            File.WriteAllText(Path.Combine(Root, "Demo.pbip"),
                """{"artifacts":[{"report":{"path":"Demo.Report"}}]}""");
            File.WriteAllText(Path.Combine(report, "definition.pbir"),
                """{"datasetReference":{"byPath":{"path":"../Demo.SemanticModel"}}}""");
            File.WriteAllText(Path.Combine(reportDefinition, "report.json"), """{"name":"Demo"}""");
            File.WriteAllText(Path.Combine(reportDefinition, "pages", "pages.json"), """{"pageOrder":["page1"]}""");
            File.WriteAllText(Path.Combine(page, "page.json"), """{"displayName":"Sales"}""");
            File.WriteAllText(Path.Combine(semanticModel, "definition.pbism"), "{}");
            File.WriteAllText(Path.Combine(semanticModel, "definition", "model.tmdl"), "model Demo");
            File.WriteAllText(Path.Combine(semanticModel, "definition", "relationships.tmdl"),
                """
                relationship rel1
                    fromColumn: FactSales.CustomerKey
                    toColumn: DimCustomer.CustomerKey
                    fromCardinality: many
                    toCardinality: one
                    isActive: true
                    crossFilteringBehavior: oneDirection
                """);
            File.WriteAllText(Path.Combine(tables, "FactSales.tmdl"),
                """
                table FactSales
                    column CustomerKey
                        dataType: int64
                    column Amount
                        dataType: decimal
                    measure 'Total Sales' =
                        SUMX(FactSales, FactSales[Amount])
                        formatString: #,##0.00
                    partition FactSales = m
                        mode: import
                """);
            File.WriteAllText(Path.Combine(tables, "DimCustomer.tmdl"),
                """
                table DimCustomer
                    column CustomerKey
                        dataType: int64
                    hierarchy Geography
                        level Country
                        level City
                """);
            File.WriteAllText(Path.Combine(visualRoot, "visual1", "visual.json"),
                """{"visual":{"visualType":"slicer"}}""");
            File.WriteAllText(Path.Combine(visualRoot, "visual2", "visual.json"),
                """{"visual":{"visualType":"barChart","objects":{"conditionalFormatting":[]}}}""");
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
