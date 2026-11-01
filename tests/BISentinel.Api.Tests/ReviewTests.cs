using System.IO.Compression;
using BISentinel.Api.Feedback;
using BISentinel.Api.Reviews;
using BISentinel.Api.ReviewHistory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BISentinel.Api.Tests;

public sealed class ReviewTests
{
    [Fact]
    public void FeedbackValidator_AcceptsRatingAndOptionalComment()
    {
        var request = new SaveReviewFeedbackRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "helpful",
            null);

        Assert.Empty(ReviewFeedbackValidator.Validate(request));
    }

    [Fact]
    public void FeedbackValidator_AcceptsCommentWithoutRating()
    {
        var request = new SaveReviewFeedbackRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "",
            "Please review this standard.");

        Assert.Empty(ReviewFeedbackValidator.Validate(request));
    }

    [Fact]
    public void FeedbackValidator_RejectsInvalidRatingAndOversizedComment()
    {
        var request = new SaveReviewFeedbackRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "excellent",
            new string('x', 501));

        var errors = ReviewFeedbackValidator.Validate(request);

        Assert.Contains(nameof(request.Rating), errors.Keys);
        Assert.Contains(nameof(request.Comment), errors.Keys);
    }

    [Fact]
    public void FeedbackValidator_RequiresReviewFindingId()
    {
        var request = new SaveReviewFeedbackRequest(
            Guid.NewGuid(),
            Guid.Empty,
            "helpful",
            null);

        var errors = ReviewFeedbackValidator.Validate(request);

        Assert.Contains(nameof(request.ReviewFindingId), errors.Keys);
    }

    [Theory]
    [InlineData("SalesModel (20261025-123456-789)", "SalesModel")]
    [InlineData("SalesModel (20261025-123456-789-2)", "SalesModel")]
    [InlineData("SalesModel", "SalesModel")]
    public void ReviewProjectName_GetBaseName_RemovesOnlyLegacyVersionSuffix(
        string projectName,
        string expectedBaseName)
    {
        Assert.Equal(expectedBaseName, ReviewProjectName.GetBaseName(projectName));
        Assert.True(ReviewProjectName.IsSameProject(projectName, expectedBaseName));
    }

    [Fact]
    public void FindingFingerprint_IsStableForTheSameProjectRuleAndLocation()
    {
        var first = ReviewFindingIdentity.CreateFingerprint("sample-demo-project", "NAM-005", "Measures.tmdl");
        var sameIssueOnAnotherRun = ReviewFindingIdentity.CreateFingerprint(
            "sample-demo-project",
            "NAM-005",
            "Measures.tmdl");
        var sameLocationInAnotherProject = ReviewFindingIdentity.CreateFingerprint(
            "sales-project",
            "NAM-005",
            "Measures.tmdl");

        Assert.Equal(first, sameIssueOnAnotherRun);
        Assert.NotEqual(first, sameLocationInAnotherProject);
    }

    [Fact]
    public void ReviewConfigurationValidator_AcceptsConfiguredPoliciesAndPatterns()
    {
        var configuration = new ReviewConfiguration
        {
            TableNamePattern = "^Dim[A-Z][A-Za-z0-9]*$",
            Rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["table-naming"] = new() { Enabled = true, Severity = "Error" }
            }
        };

        var errors = ReviewConfigurationValidator.Validate(configuration, ["table-naming"]);

        Assert.Empty(errors);
    }

    [Fact]
    public void ModuleAvailability_DefaultsToReviewsAndGovernanceAuditOnly()
    {
        var modules = new ReviewConfiguration().Modules;

        Assert.True(modules.Reviews);
        Assert.True(modules.GovernanceAudit);
        Assert.False(modules.PerformanceOptimization);
        Assert.False(modules.ReleaseReadiness);
        Assert.False(modules.DocumentationGenerator);
        Assert.False(modules.ArchitectureReview);
        Assert.False(modules.BiDeliveryManager);
    }

    [Fact]
    public async Task ReviewModuleAvailabilityMiddleware_HidesReviewApiWhenDisabled()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/reviews/upload";
        var nextCalled = false;
        var middleware = new ReviewModuleAvailabilityMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<ReviewModuleAvailabilityMiddleware>.Instance);

        await middleware.InvokeAsync(
            context,
            new StubReviewConfigurationStore(new ReviewConfiguration
            {
                Modules = new ModuleAvailability { Reviews = false }
            }));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task ReviewModuleAvailabilityMiddleware_AllowsReviewApiWhenEnabled()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/reviews/history";
        var nextCalled = false;
        var middleware = new ReviewModuleAvailabilityMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<ReviewModuleAvailabilityMiddleware>.Instance);

        await middleware.InvokeAsync(
            context,
            new StubReviewConfigurationStore(new ReviewConfiguration()));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task ReviewModuleAvailabilityMiddleware_HidesPerformanceOptimizationApiWhenDisabled()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/performance-optimization/assess";
        var nextCalled = false;
        var middleware = new ReviewModuleAvailabilityMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<ReviewModuleAvailabilityMiddleware>.Instance);

        await middleware.InvokeAsync(
            context,
            new StubReviewConfigurationStore(new ReviewConfiguration
            {
                Modules = new ModuleAvailability { PerformanceOptimization = false }
            }));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task ReviewModuleAvailabilityMiddleware_AllowsPerformanceOptimizationApiWhenEnabled()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/performance-optimization/assess";
        var nextCalled = false;
        var middleware = new ReviewModuleAvailabilityMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<ReviewModuleAvailabilityMiddleware>.Instance);

        await middleware.InvokeAsync(
            context,
            new StubReviewConfigurationStore(new ReviewConfiguration
            {
                Modules = new ModuleAvailability { PerformanceOptimization = true }
            }));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public void ReviewConfigurationValidator_RejectsInvalidRegexSeverityAndUnknownRules()
    {
        var configuration = new ReviewConfiguration
        {
            TableNamePattern = "(",
            Rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["table-naming"] = new() { Enabled = true, Severity = "Critical" },
                ["unregistered-rule"] = new() { Enabled = true, Severity = "Warning" }
            }
        };

        var errors = ReviewConfigurationValidator.Validate(configuration, ["table-naming"]);

        Assert.Contains(nameof(configuration.TableNamePattern), errors.Keys);
        Assert.Contains("Rules.table-naming.Severity", errors.Keys);
        Assert.Contains("Rules.unregistered-rule", errors.Keys);
    }

    [Fact]
    public async Task ReviewOnlineModelAsync_SkipsChecksWithoutOnlineEvidence()
    {
        var project = new PbipProjectModel(
            "Online workspace",
            [],
            [new ModelTable(
                "fact_sales",
                "Sales model/fact_sales",
                [new ModelColumn("Column1")],
                [])],
            [],
            HasSemanticModel: true);
        var reviewer = CreateReviewer(project, "unused", DefaultRules());

        var result = await reviewer.ReviewOnlineModelAsync(
            project,
            "online-workspace",
            new HashSet<string>(
                ["measure-table-name", "measure-naming", "required-measures", "explicit-measures",
                 "dax-division", "dax-format", "unused-tables", "unused-columns", "unused-measures"],
                StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Contains("table-naming", result.ExecutedRuleKeys);
        Assert.Contains("column-naming", result.ExecutedRuleKeys);
        Assert.DoesNotContain("dax-division", result.ExecutedRuleKeys);
        Assert.DoesNotContain("unused-measures", result.ExecutedRuleKeys);
        Assert.Contains(result.Findings, finding => finding.RuleId == "NAM-001");
        Assert.Contains(result.Findings, finding => finding.RuleId == "NAM-003");
    }

    [Fact]
    public async Task Reader_ParsesProjectAndSkipsGeneratedTablesFromCounts()
    {
        using var fixture = new PbipFixture();
        var reader = new PbipProjectReader();

        var project = reader.Read(fixture.Root);
        var projectFromDescriptor = reader.Read(Path.Combine(fixture.Root, "Demo.pbip"));
        var result = await CreateReviewer(project, fixture.Root, DefaultRules())
            .ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);

        Assert.Equal("Demo", project.ProjectName);
        Assert.Equal(project.ProjectName, projectFromDescriptor.ProjectName);
        Assert.Equal(4, project.Tables.Count);
        Assert.Equal(3, result.TablesChecked);
        Assert.Equal(1, result.MeasuresChecked);
        Assert.Single(project.Relationships);
        Assert.Contains(result.Findings, finding => finding.RuleId == "NAM-003");
    }

    [Fact]
    public void ModelUsageRules_FindPotentiallyUnusedTablesColumnsAndMeasures()
    {
        var project = new PbipProjectModel(
            "Usage",
            [],
            [
                new ModelTable(
                    "FactSales",
                    "FactSales.tmdl",
                    [new("CustomerKey"), new("Amount"), new("UnusedFlag")],
                    [new("Total Sales", "SUM(FactSales[Amount])")]),
                new ModelTable("DimCustomer", "DimCustomer.tmdl", [new("CustomerKey")], []),
                new ModelTable("FactArchive", "FactArchive.tmdl", [new("OldValue")], []),
                new ModelTable(
                    "Measures",
                    "Measures.tmdl",
                    [],
                    [new("Unused KPI", "COUNTROWS(FactSales)")])
            ],
            [new("FactSales", "CustomerKey", "DimCustomer", "CustomerKey", false)],
            ReportReferences: ["{\"entity\":\"FactSales\",\"column\":\"Amount\",\"measure\":\"Total Sales\"}"]);

        var unusedTables = new UnusedTablesRule().Evaluate(project, new ReviewOptions());
        var unusedColumns = new UnusedColumnsRule().Evaluate(project, new ReviewOptions());
        var unusedMeasures = new UnusedMeasuresRule().Evaluate(project, new ReviewOptions());

        Assert.Contains(unusedTables.Findings, finding =>
            finding.RuleId == "OPT-001" &&
            finding.Message == "Table is not used: FactArchive" &&
            finding.Location == "FactArchive" &&
            finding.Recommendation.Contains("- Relationship metadata alone does not establish that the table is consumed.", StringComparison.Ordinal));
        Assert.Contains(unusedTables.Findings, finding =>
            finding.RuleId == "OPT-001" &&
            finding.Message == "Table is not used: DimCustomer" &&
            finding.Location == "DimCustomer");
        Assert.DoesNotContain(unusedTables.Findings, finding =>
            finding.Message.Contains("FactSales", StringComparison.Ordinal));
        Assert.Contains(unusedColumns.Findings, finding =>
            finding.RuleId == "OPT-002" &&
            finding.Message == "Column is not used: FactSales[UnusedFlag]" &&
            finding.Location == "FactSales[UnusedFlag]");
        Assert.DoesNotContain(unusedColumns.Findings, finding =>
            finding.Message.Contains("CustomerKey", StringComparison.Ordinal));
        Assert.DoesNotContain(unusedColumns.Findings, finding =>
            finding.Message.Contains("Amount", StringComparison.Ordinal));
        Assert.Contains(unusedMeasures.Findings, finding =>
            finding.RuleId == "OPT-003" &&
            finding.Message == "Measure is not used: Unused KPI (table Measures)" &&
            finding.Location == "Measures[Unused KPI]");
        Assert.DoesNotContain(unusedMeasures.Findings, finding =>
            finding.Message.Contains("Total Sales", StringComparison.Ordinal));

        var modelOnlyMeasures = new UnusedMeasuresRule()
            .Evaluate(project with { ReportReferences = null }, new ReviewOptions());
        Assert.Contains(modelOnlyMeasures.Findings, finding =>
            finding.Recommendation.Contains("report visual usage could not be checked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnusedTablesRule_AlwaysExcludesPowerBiGeneratedDateTables()
    {
        var project = new PbipProjectModel(
            "GeneratedDates",
            [],
            [
                new ModelTable("DateTableTemplate_abc123", "generated.tmdl", [], []),
                new ModelTable("LocalDateTable_xyz789", "local.tmdl", [], [])
            ],
            []);
        var options = new ReviewOptions { GeneratedTablePrefixes = [] };

        var findings = new UnusedTablesRule().Evaluate(project, options);

        Assert.Empty(findings.Findings);
    }

    [Fact]
    public void Reader_RejectsProjectReferencesOutsideConfiguredRoot()
    {
        using var fixture = new PbipFixture();
        File.WriteAllText(
            Path.Combine(fixture.Root, "Demo.pbip"),
            """{"artifacts":[{"report":{"path":"..\\outside"}}]}""");

        var reader = new PbipProjectReader();

        Assert.Throws<InvalidDataException>(() => reader.Read(fixture.Root));
    }

    [Fact]
    public async Task UploadReview_ReviewsCompletePbipProjectZip()
    {
        using var fixture = new PbipFixture();
        using var uploadStream = CreatePbipZipUpload(fixture.Root);
        var upload = new FormFile(uploadStream, 0, uploadStream.Length, "file", "Demo.zip");
        var options = new ReviewOptions();
        var optionsMonitor = new StubOptionsMonitor<ReviewOptions>(options);
        var projectInput = new PbipProjectInputService(
            new PbipProjectReviewer(
                new PbipProjectReader(),
                DefaultRules(),
                optionsMonitor,
                new StubReviewConfigurationStore(ReviewConfiguration.FromReviewOptions(options))),
            new PbiToolsExtractor(optionsMonitor));

        var result = await projectInput.ReviewArchiveAsync(upload, "uploaded-demo", CancellationToken.None);

        Assert.Equal("Demo", result.ProjectName);
        Assert.Equal("uploaded-demo", result.ProjectId);
        Assert.Equal(3, result.TablesChecked);
        Assert.Contains(result.Findings, finding => finding.RuleId == "NAM-003");
    }

    [Fact]
    public async Task UploadReview_RejectsZipPathTraversal()
    {
        using var uploadStream = new MemoryStream();
        using (var archive = new ZipArchive(uploadStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open());
            writer.Write("outside");
        }
        uploadStream.Position = 0;
        var upload = new FormFile(uploadStream, 0, uploadStream.Length, "file", "unsafe.zip");
        var optionsMonitor = new StubOptionsMonitor<ReviewOptions>(new ReviewOptions());
        var projectInput = new PbipProjectInputService(
            new PbipProjectReviewer(
                new PbipProjectReader(),
                DefaultRules(),
                optionsMonitor,
                new StubReviewConfigurationStore(ReviewConfiguration.FromReviewOptions(optionsMonitor.CurrentValue))),
            new PbiToolsExtractor(optionsMonitor));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => projectInput.ReviewArchiveAsync(upload, null, CancellationToken.None));
    }

    [Fact]
    public async Task UploadReview_RejectsPbixFiles()
    {
        using var stream = new MemoryStream([1]);
        var upload = new FormFile(stream, 0, stream.Length, "file", "model.pbix");
        var options = new ReviewOptions();
        var optionsMonitor = new StubOptionsMonitor<ReviewOptions>(options);
        var projectInput = new PbipProjectInputService(
            new PbipProjectReviewer(
                new PbipProjectReader(),
                DefaultRules(),
                optionsMonitor,
                new StubReviewConfigurationStore(ReviewConfiguration.FromReviewOptions(options))),
            new PbiToolsExtractor(optionsMonitor));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => projectInput.ReviewArchiveAsync(upload, null, CancellationToken.None));

        Assert.Contains("PBIX files are not supported", exception.Message);
    }

    [Fact]
    public async Task UploadReview_RequiresPbiToolsForPbitFiles()
    {
        using var stream = new MemoryStream([1]);
        var upload = new FormFile(stream, 0, stream.Length, "file", "template.pbit");
        var options = new ReviewOptions
        {
            PbiToolsPath = Path.Combine(Path.GetTempPath(), $"missing-pbi-tools-{Guid.NewGuid():N}.exe")
        };
        var optionsMonitor = new StubOptionsMonitor<ReviewOptions>(options);
        var projectInput = new PbipProjectInputService(
            new PbipProjectReviewer(
                new PbipProjectReader(),
                DefaultRules(),
                optionsMonitor,
                new StubReviewConfigurationStore(ReviewConfiguration.FromReviewOptions(options))),
            new PbiToolsExtractor(optionsMonitor));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => projectInput.ReviewArchiveAsync(upload, null, CancellationToken.None));

        Assert.Contains("PBIT review requires pbi-tools", exception.Message);
        Assert.Contains("executable was not found", exception.Message);
    }

    [Fact]
    public async Task UploadReview_RejectsFilesLargerThan500Mb()
    {
        const long maximumUploadBytes = 500L * 1024 * 1024;
        using var stream = new MemoryStream();
        var upload = new FormFile(stream, 0, maximumUploadBytes + 1, "file", "oversized.zip");
        var options = new ReviewOptions();
        var optionsMonitor = new StubOptionsMonitor<ReviewOptions>(options);
        var projectInput = new PbipProjectInputService(
            new PbipProjectReviewer(
                new PbipProjectReader(),
                DefaultRules(),
                optionsMonitor,
                new StubReviewConfigurationStore(ReviewConfiguration.FromReviewOptions(options))),
            new PbiToolsExtractor(optionsMonitor));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => projectInput.ReviewArchiveAsync(upload, null, CancellationToken.None));

        Assert.Contains("500 MB", exception.Message);
    }

    [Fact]
    public async Task Reader_ParsesStandalonePbirWithoutSemanticModel()
    {
        using var fixture = new PbirFixture();

        var project = new PbipProjectReader().Read(fixture.Root);
        var review = await CreateReviewer(project, fixture.Root, DefaultRules())
            .ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);

        Assert.False(project.HasSemanticModel);
        Assert.Empty(project.Tables);
        Assert.Contains(review.Findings, finding => finding.RuleId == "FORMAT-001");
        Assert.DoesNotContain(review.Findings, finding => finding.RuleId.StartsWith("NAM-", StringComparison.Ordinal));
        Assert.DoesNotContain(review.Findings, finding => finding.RuleId == "MEAS-001");
    }

    [Fact]
    public async Task Reader_ParsesTmdlFromPbiToolsExtractedProject()
    {
        using var fixture = new PbiToolsFixture();

        var project = new PbipProjectReader().Read(fixture.Root);
        var review = await CreateReviewer(project, fixture.Root, DefaultRules())
            .ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);

        Assert.Equal(Path.GetFileName(fixture.Root), project.ProjectName);
        Assert.Single(project.Tables);
        Assert.Equal("FactSales", project.Tables[0].Name);
        Assert.Single(project.Tables[0].Columns);
        Assert.Single(project.Tables[0].Measures);
        Assert.Contains(project.Tables[0].Measures, measure => measure.Name == "Total Sales");
        Assert.Single(project.Relationships);
        Assert.Equal(1, review.TablesChecked);
        Assert.Equal(1, review.RelationshipsChecked);
    }

    [Fact]
    public async Task Reviewer_AppliesRuleEnabledAndSeverityConfiguration()
    {
        using var fixture = new PbipFixture();
        var project = new PbipProjectReader().Read(fixture.Root);
        var options = new ReviewOptions
        {
            MeasureTableNameCandidates = ["measures (2)"],
            Rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["placeholder-column"] = new() { Enabled = false },
                ["measure-table-name"] = new() { Enabled = true, Severity = "Information" }
            }
        };

        var result = await CreateReviewer(project, fixture.Root, DefaultRules(), options)
            .ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);

        Assert.DoesNotContain(result.Findings, finding => finding.RuleId == "NAM-003");
        Assert.Contains(result.Findings, finding =>
            finding.RuleId == "NAM-004" && finding.Severity == "Information");
    }

    [Fact]
    public async Task MeasureTableNameRule_OnlyChecksExplicitCandidates()
    {
        using var fixture = new PbipFixture();
        var project = new PbipProjectReader().Read(fixture.Root);
        var dimensionWithMeasure = project.Tables
            .Select(table => table.Name == "DimCustomer"
                ? table with { Measures = [new ModelMeasure("Customer Count", "COUNTROWS(DimCustomer)")] }
                : table)
            .ToArray();
        var projectWithDimensionMeasure = project with { Tables = dimensionWithMeasure };
        var candidates = new ReviewOptions
        {
            MeasureTableNameCandidates = ["measures (2)"],
            Rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["measure-table-name"] = new() { Enabled = true }
            }
        };
        var noCandidates = new ReviewOptions
        {
            Rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["measure-table-name"] = new() { Enabled = true }
            }
        };

        var candidateResult = await CreateReviewer(
            projectWithDimensionMeasure,
            fixture.Root,
            [new MeasureTableNameRule()],
            candidates).ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);
        var unconfiguredResult = await CreateReviewer(
            projectWithDimensionMeasure,
            fixture.Root,
            [new MeasureTableNameRule()],
            noCandidates).ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);

        var finding = Assert.Single(candidateResult.Findings, item => item.RuleId == "NAM-004");
        Assert.Equal("Measures.tmdl", finding.Location);
        Assert.DoesNotContain(unconfiguredResult.Findings, item => item.RuleId == "NAM-004");
    }

    [Fact]
    public async Task Reviewer_FlagsMissingRequiredMeasuresAsConfiguredSeverity()
    {
        using var fixture = new PbipFixture();
        var project = new PbipProjectReader().Read(fixture.Root);
        var options = new ReviewOptions
        {
            RequiredMeasures = ["Total Sales", "Total Margin"],
            Rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["required-measures"] = new() { Enabled = true, Severity = "Error" }
            }
        };

        var result = await CreateReviewer(project, fixture.Root, DefaultRules(), options)
            .ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);

        var finding = Assert.Single(result.Findings, item => item.RuleId == "MEAS-001");
        Assert.Equal("Error", finding.Severity);
        Assert.Contains("Total Margin", finding.Message);
    }

    [Fact]
    public async Task Reviewer_AppliesCustomMeasureNamingPattern()
    {
        using var fixture = new PbipFixture();
        var project = new PbipProjectReader().Read(fixture.Root);
        var options = new ReviewOptions
        {
            MeasureNamePattern = "^[A-Z][a-z]+$",
            Rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase)
            {
                ["measure-naming"] = new() { Enabled = true, Severity = "Warning" }
            }
        };

        var result = await CreateReviewer(project, fixture.Root, DefaultRules(), options)
            .ReviewAsync(fixture.Root, project.ProjectName, null, CancellationToken.None);

        var finding = Assert.Single(result.Findings, item => item.RuleId == "NAM-005");
        Assert.Contains("Total Sales", finding.Message);
    }

    private static MemoryStream CreatePbipZipUpload(string projectRoot)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories))
            {
                var entryName = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
                var entry = archive.CreateEntry(entryName);
                using var entryStream = entry.Open();
                using var input = File.OpenRead(file);
                input.CopyTo(entryStream);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static IPbipProjectReviewer CreateReviewer(
        PbipProjectModel project,
        string projectPath,
        IEnumerable<IReviewRule> rules,
        ReviewOptions? options = null)
    {
        options ??= new ReviewOptions();
        return new PbipProjectReviewer(
            new StubProjectReader(project),
            rules,
            new StubOptionsMonitor<ReviewOptions>(options),
            new StubReviewConfigurationStore(ReviewConfiguration.FromReviewOptions(options)));
    }

    private static IReadOnlyList<IReviewRule> DefaultRules() =>
    [
        new ProjectStructureRule(),
        new TableNamingRule(),
        new ColumnNamingRule(),
        new PlaceholderColumnRule(),
        new MeasureTableNameRule(),
        new MeasureNamingRule(),
        new RequiredMeasuresRule(),
        new RelationshipColumnsRule(),
        new StarSchemaRelationshipRule(),
        new SingleDirectionRelationshipRule(),
        new ExplicitMeasuresRule(),
        new DaxDivisionRule(),
        new DaxFormatRule(),
        new UnusedTablesRule(),
        new UnusedColumnsRule(),
        new UnusedMeasuresRule()
    ];

    private sealed class StubProjectReader(PbipProjectModel project) : IPbipProjectReader
    {
        public PbipProjectModel Read(string projectPath) => project;
    }

    private sealed class StubOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StubReviewConfigurationStore(ReviewConfiguration configuration)
        : IReviewConfigurationStore
    {
        public Task<ReviewConfigurationSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ReviewConfigurationSnapshot(
                configuration,
                1,
                DateTimeOffset.UtcNow,
                "test"));

        public Task<ReviewConfigurationSnapshot?> UpdateAsync(
            ReviewConfiguration updatedConfiguration,
            int expectedVersion,
            string changedBy,
            CancellationToken cancellationToken) =>
            Task.FromResult<ReviewConfigurationSnapshot?>(null);
    }

    private sealed class PbipFixture : IDisposable
    {
        public PbipFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "BISentinel.Tests", Guid.NewGuid().ToString("N"));
            var report = Path.Combine(Root, "Demo.Report");
            var semanticModel = Path.Combine(Root, "Demo.SemanticModel");
            var reportDefinition = Path.Combine(report, "definition");
            var tableDirectory = Path.Combine(semanticModel, "definition", "tables");

            Directory.CreateDirectory(Path.Combine(reportDefinition, "pages"));
            Directory.CreateDirectory(tableDirectory);

            File.WriteAllText(
                Path.Combine(Root, "Demo.pbip"),
                """{"artifacts":[{"report":{"path":"Demo.Report"}}]}""");
            File.WriteAllText(
                Path.Combine(report, "definition.pbir"),
                """{"datasetReference":{"byPath":{"path":"../Demo.SemanticModel"}}}""");
            File.WriteAllText(Path.Combine(reportDefinition, "report.json"), "{}");
            File.WriteAllText(Path.Combine(reportDefinition, "pages", "pages.json"), "{}");
            File.WriteAllText(Path.Combine(semanticModel, "definition.pbism"), "{}");
            File.WriteAllText(Path.Combine(semanticModel, "definition", "model.tmdl"), "model Demo");
            File.WriteAllText(
                Path.Combine(semanticModel, "definition", "relationships.tmdl"),
                """
                relationship rel1
                    fromColumn: FactSales.CustomerKey
                    toColumn: DimCustomer.CustomerKey
                """);
            File.WriteAllText(
                Path.Combine(tableDirectory, "FactSales.tmdl"),
                """
                table FactSales
                    column CustomerKey
                    column Amount
                """);
            File.WriteAllText(
                Path.Combine(tableDirectory, "DimCustomer.tmdl"),
                """
                table DimCustomer
                    column CustomerKey
                """);
            File.WriteAllText(
                Path.Combine(tableDirectory, "Measures.tmdl"),
                """
                table 'measures (2)'
                    measure 'Total Sales' =
                        SUM(FactSales[Amount])
                        lineageTag: 12345678
                """);
            File.WriteAllText(
                Path.Combine(tableDirectory, "LocalDateTable_generated.tmdl"),
                """
                table LocalDateTable_generated
                    column Date
                """);
            File.AppendAllText(
                Path.Combine(tableDirectory, "Measures.tmdl"),
                """

                    column Column1
                """);
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

    private sealed class PbirFixture : IDisposable
    {
        public PbirFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "BISentinel.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "definition", "pages"));
            File.WriteAllText(Path.Combine(Root, "definition.pbir"), "{}");
            File.WriteAllText(Path.Combine(Root, "definition", "report.json"), "{}");
            File.WriteAllText(Path.Combine(Root, "definition", "pages", "pages.json"), "{}");
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

    private sealed class PbiToolsFixture : IDisposable
    {
        public PbiToolsFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "BISentinel.Tests", Guid.NewGuid().ToString("N"));
            var model = Path.Combine(Root, "Model");
            Directory.CreateDirectory(Path.Combine(model, "tables"));
            var duplicateModel = Path.Combine(Root, "SemanticModel", "definition");
            Directory.CreateDirectory(Path.Combine(duplicateModel, "tables"));
            File.WriteAllText(Path.Combine(Root, ".pbixproj.json"), "{}");
            File.WriteAllText(
                Path.Combine(model, "tables", "FactSales.tmdl"),
                """
                table FactSales
                    column Amount
                    measure 'Total Sales' =
                        SUM(FactSales[Amount])
                """);
            File.WriteAllText(
                Path.Combine(model, "relationships.tmdl"),
                """
                relationship rel1
                    fromColumn: FactSales.CustomerKey
                    toColumn: DimCustomer.CustomerKey
                """);
            File.WriteAllText(
                    Path.Combine(duplicateModel, "tables", "FactSales.tmdl"),
                    """
                    table FactSales
                        column Amount
                        measure 'Total Sales' =
                            SUM(FactSales[Amount])
                    """);
            File.WriteAllText(
                    Path.Combine(duplicateModel, "relationships.tmdl"),
                    """
                    relationship rel1-copy
                        fromColumn: DimCustomer.CustomerKey
                        toColumn: FactSales.CustomerKey
                    """);
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
