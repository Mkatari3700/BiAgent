using System.Text.Json;
using System.Text.RegularExpressions;
using BISentinel.Api.Reviews;

namespace BISentinel.Api.PerformanceOptimization;

public sealed class PerformanceAssessmentService(IPbipProjectReader projectReader)
{
    private static readonly Regex CommentsAndStrings = new(
        @"//.*?$|/\*.*?\*/|""(?:[^""]|"""")*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);

    private static readonly Regex FunctionCall = new(
        @"\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DirectSumX = new(
        @"\bSUMX\s*\(\s*(?<table>'[^']+'|[A-Za-z_][A-Za-z0-9_]*)\s*,\s*(?<reference>'[^']+'|[A-Za-z_][A-Za-z0-9_]*)\s*\[[^\]]+\]\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex RepeatedSimpleCall = new(
        @"\b(?<function>SUM|COUNTROWS|DISTINCTCOUNT|DATESYTD|SAMEPERIODLASTYEAR)\s*\((?<argument>[^()]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> IteratorFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUMX", "AVERAGEX", "MINX", "MAXX", "COUNTX", "COUNTAX", "PRODUCTX",
        "RANKX", "CONCATENATEX", "FILTER", "ADDCOLUMNS", "GENERATE", "GENERATEALL"
    };

    public PerformanceAssessment Assess(
        string projectPath,
        string? projectName = null,
        string? uploadedExtension = null)
    {
        var project = projectReader.Read(projectPath);
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            project = project with { ProjectName = projectName };
        }

        var tables = project.Tables;
        var measures = tables.SelectMany(table => table.Measures.Select(measure =>
            (TableName: table.Name, Name: measure.Name, measure.Expression, ObjectType: "Measure"))).ToList();
        measures.AddRange(tables
            .SelectMany(table => table.Columns
                .Where(column => column.IsCalculated && !string.IsNullOrWhiteSpace(column.Expression))
                .Select(column => (TableName: table.Name, Name: column.Name, Expression: column.Expression!, ObjectType: "Calculated column"))));

        var daxInventory = measures
            .Select(item => new PerformanceDaxInventory(
                item.TableName,
                item.Name,
                item.Expression,
                item.ObjectType,
                AnalyzeDax(item.Expression)))
            .ToArray();
        var reportInventory = BuildReportInventory(project.ReportFiles, project.ProjectName);
        var modelInventory = BuildModelInventory(project);
        var reusableArtifacts = BuildReusableArtifacts(project);
        var candidates = BuildOpportunities(project, daxInventory);
        var findings = BuildFindings(project, modelInventory, reportInventory, daxInventory, candidates);
        var evidenceGaps = BuildEvidenceGaps(project, reportInventory);
        var gates = BuildGates(project, reportInventory, candidates, evidenceGaps);
        var risks = BuildRisks(project);
        var recommendedNextSteps = BuildNextSteps(project, reportInventory);
        var fabricReadiness = BuildFabricReadiness(project, evidenceGaps);
        var reportDescriptorCount = project.ReportFiles.Count(file =>
            file.RelativePath.EndsWith("definition.pbir", StringComparison.OrdinalIgnoreCase));
        var reportCount = reportDescriptorCount > 0
            ? reportDescriptorCount
            : project.ReportFiles.Any(file =>
                file.RelativePath.EndsWith("report.json", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
        var staticCount = findings.Count(finding =>
            finding.EvidenceType == "Static analysis observation");
        var hypothesisCount = findings.Count(finding =>
            finding.EvidenceType == "Optimization hypothesis");

        return new PerformanceAssessment(
            project.ProjectName,
            GetArtifactType(projectPath, uploadedExtension),
            DateTimeOffset.UtcNow,
            new PerformanceExecutiveSummary(
                staticCount,
                hypothesisCount,
                0,
                "No measured performance conclusion is available from the uploaded artifact. Findings are static observations or hypotheses; no performance gain is claimed."),
            new PerformanceScope(
                project.HasSemanticModel,
                project.ReportFiles.Count > 0,
                reportCount,
                tables.Count,
                tables.Sum(table => table.Columns.Count),
                tables.Sum(table => table.Measures.Count),
                project.Relationships.Count,
                tables.Sum(table => table.Partitions.Count),
                reportInventory.Count,
                reportInventory.Sum(report => report.VisualCount),
                "Read-only static analysis of uploaded definitions. Query, refresh, rendering, resource, and capacity telemetry are not collected."),
            modelInventory,
            reportInventory,
            daxInventory,
            reusableArtifacts,
            findings,
            new PerformanceEvidenceSeparation(
                [],
                findings.Where(finding =>
                    finding.EvidenceType is "Static analysis observation" or "Optimization hypothesis").ToArray(),
                evidenceGaps),
            candidates,
            evidenceGaps,
            risks,
            recommendedNextSteps,
            fabricReadiness,
            gates);
    }

    private static IReadOnlyList<PerformanceDaxIssue> AnalyzeDax(string expression)
    {
        var dax = CommentsAndStrings.Replace(expression, string.Empty);
        var issues = new List<PerformanceDaxIssue>();
        var functionNames = FunctionCall.Matches(dax)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
        var iterators = functionNames.Where(IteratorFunctions.Contains).ToArray();

        foreach (Match match in DirectSumX.Matches(dax))
        {
            var iteratorTable = NormalizeIdentifier(match.Groups["table"].Value);
            var referenceTable = NormalizeIdentifier(match.Groups["reference"].Value);
            if (!string.Equals(iteratorTable, referenceTable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            issues.Add(new PerformanceDaxIssue(
                "Low",
                "SUMX iterates a direct column reference",
                "The iterator expression is a direct reference to a column on the iterated table. This is a syntactic candidate only; model semantics and measured query cost are unavailable.",
                "Consider SUM(Table[Column]) only if row-level expression semantics, data type, blank handling, and filter behavior are equivalent.",
                "Moderate: confirm semantic equivalence and benchmark representative queries before changing the measure.",
                "High",
                "Static analysis observation"));
        }

        if (iterators.Length > 0)
        {
            issues.Add(new PerformanceDaxIssue(
                "Low",
                $"Iterator functions observed: {string.Join(", ", iterators.Distinct(StringComparer.OrdinalIgnoreCase))}",
                "Iterator functions evaluate an expression across a table. Their cost depends on model cardinality, storage mode, query context, and engine plan, none of which is available here.",
                "Use DAX Studio or Performance Analyzer traces to determine whether the iterator is on a measured hot path; do not replace it based on syntax alone.",
                "Low for investigation; changing semantics can be high risk.",
                "High",
                "Static analysis observation"));
        }

        var nestedIterators = IteratorFunctions
            .Where(outer => ContainsNestedFunction(
                dax,
                outer,
                new HashSet<string>(IteratorFunctions, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        if (nestedIterators.Length > 0)
        {
            issues.Add(new PerformanceDaxIssue(
                "Medium",
                $"Nested iterator pattern observed under: {string.Join(", ", nestedIterators)}",
                "Nested row-wise/table expressions can increase work, but the model cardinality and execution plan are not available to establish cost.",
                "Use DAX Studio server timings and query plans to validate the hot path before considering a set-based rewrite or aggregation.",
                "High: nested row context and filter semantics may change.",
                "Medium",
                "Optimization hypothesis"));
        }

        var contextTransitionIterators = IteratorFunctions
            .Where(iterator => ContainsNestedFunction(
                dax,
                iterator,
                new HashSet<string>(["CALCULATE"], StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        if (contextTransitionIterators.Length > 0)
        {
            issues.Add(new PerformanceDaxIssue(
                "Medium",
                $"CALCULATE occurs inside iterator(s): {string.Join(", ", contextTransitionIterators)}",
                "This structure can introduce context transition for iterator rows. Whether that is costly depends on the query plan and model cardinality.",
                "Inspect the query plan and validate filter/context semantics before considering a rewrite or pre-aggregation.",
                "High: context-transition and filter behavior can change.",
                "Medium",
                "Optimization hypothesis"));
        }

        if (ContainsNestedFunction(
                dax,
                "CALCULATE",
                new HashSet<string>(["FILTER"], StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add(new PerformanceDaxIssue(
                "Low",
                "FILTER is nested in CALCULATE",
                "The expression contains a table filter argument. This is not inherently misuse; filter shape, cardinality, and the query plan are required to determine whether a Boolean filter or another pattern is appropriate.",
                "Inspect the filter expression and query plan; use a Boolean filter argument only when it is semantically equivalent.",
                "High: filter replacement may alter context and results.",
                "Low",
                "Optimization hypothesis"));
        }

        var repeatedCalls = RepeatedSimpleCall.Matches(dax)
            .Select(match => $"{match.Groups["function"].Value.ToUpperInvariant()}({NormalizeExpression(match.Groups["argument"].Value)})")
            .GroupBy(call => call, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (repeatedCalls.Length > 0 && !Regex.IsMatch(dax, @"\bVAR\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var conditionalBranch = Regex.IsMatch(
                dax,
                @"\b(?:IF|SWITCH)\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            issues.Add(new PerformanceDaxIssue(
                "Low",
                conditionalBranch
                    ? "Repeated simple calculations in a branching measure"
                    : "Repeated simple calculations without a variable",
                $"Repeated expression pattern(s) were found: {string.Join("; ", repeatedCalls)}.",
                "Consider defining a VAR for repeated expressions when it preserves evaluation and filter semantics; compare query plans before and after.",
                "Moderate: variable evaluation and context can differ by expression.",
                "Medium",
                "Optimization hypothesis"));
        }

        var timeIntelligenceFunctions = new HashSet<string>(
            ["DATEADD", "DATESYTD", "DATESMTD", "DATESQTD", "TOTALYTD", "TOTALMTD", "TOTALQTD",
                "SAMEPERIODLASTYEAR", "PARALLELPERIOD", "PREVIOUSMONTH", "PREVIOUSQUARTER", "PREVIOUSYEAR"],
            StringComparer.OrdinalIgnoreCase);
        var timeIntelligenceCalls = functionNames
            .Where(timeIntelligenceFunctions.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (timeIntelligenceCalls.Length > 0)
        {
            issues.Add(new PerformanceDaxIssue(
                "Low",
                $"Time-intelligence function(s) observed: {string.Join(", ", timeIntelligenceCalls)}",
                "Static syntax does not establish an inefficiency. Calendar-table configuration, filter propagation, and query plan are not available.",
                "Validate date-table relationships and compare DAX Studio or Performance Analyzer traces for representative time-intelligence queries.",
                "Low for investigation; rewriting time logic can change results.",
                "Low",
                "Static analysis observation"));
        }
        if (Regex.IsMatch(dax, @"\bEARLIER\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            issues.Add(new PerformanceDaxIssue(
                "Low",
                "EARLIER function observed",
                "EARLIER depends on nested row context. The expression is statically identifiable, but actual row cost and correctness are not established.",
                "Review whether a variable or alternate table expression is clearer and benchmark only if this calculation is material to a measured workload.",
                "Moderate: row-context and filter semantics can change.",
                "Medium",
                "Static analysis observation"));
        }

        return issues;
    }

    private static PerformanceModelInventory BuildModelInventory(PbipProjectModel project)
    {
        var tables = project.Tables.Select(table => new PerformanceTableInventory(
            table.Name,
            table.FileName,
            table.Columns.Count,
            table.Columns.Select(column => new PerformanceColumnInventory(
                column.Name,
                column.DataType,
                column.IsCalculated,
                column.IsHidden,
                column.Expression)).ToArray(),
            table.Measures.Count,
            table.Columns.Count(column => column.IsCalculated),
            table.IsCalculated,
            table.Partitions.Select(partition =>
                $"{partition.Name} (mode: {partition.Mode ?? "unknown"}, source: {partition.SourceType ?? "unknown"})").ToArray(),
            table.Hierarchies.Select(hierarchy =>
                hierarchy.Levels.Count == 0 ? hierarchy.Name : $"{hierarchy.Name}: {string.Join(" > ", hierarchy.Levels)}").ToArray()))
            .ToArray();
        var relationships = project.Relationships.Select(relationship => new PerformanceRelationshipInventory(
            $"{relationship.FromTable}[{relationship.FromColumn}]",
            $"{relationship.ToTable}[{relationship.ToColumn}]",
            relationship.FromCardinality,
            relationship.ToCardinality,
            relationship.IsActive,
            relationship.CrossFilteringBehavior,
            relationship.FromCardinality is null || relationship.ToCardinality is null
                ? "Relationship endpoints are statically parsed; cardinality metadata is unavailable."
                : "Relationship properties are read from the uploaded model definition.")).ToArray();
        var observations = new List<string>();
        var factTables = project.Tables.Where(table => table.Name.StartsWith("Fact", StringComparison.OrdinalIgnoreCase)).ToArray();
        var dimensionTables = project.Tables.Where(table => table.Name.StartsWith("Dim", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (factTables.Length > 0 || dimensionTables.Length > 0)
        {
            observations.Add(
                $"Name-based convention only: {factTables.Length} table(s) start with 'Fact' and {dimensionTables.Length} start with 'Dim'. This does not verify star-schema semantics.");
        }

        var likelyKeys = project.Tables.SelectMany(table => table.Columns
            .Where(column => column.Name.EndsWith("Key", StringComparison.OrdinalIgnoreCase) ||
                column.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            .Select(column => $"{table.Name}[{column.Name}]"))
            .ToArray();
        observations.Add(likelyKeys.Length == 0
            ? "Surrogate-key design cannot be verified from naming because no key-like column names were detected."
            : $"Name-based key candidates: {string.Join(", ", likelyKeys)}. Key uniqueness, role, and data type are not established.");
        observations.Add(project.Relationships.Count == 0
            ? "No relationships were parsed; relationship design cannot be assessed from this artifact."
            : $"{project.Relationships.Count(relationship => relationship.IsBidirectional)} bidirectional relationship(s) are explicitly represented.");

        var hasPartitionPolicy = project.Tables.SelectMany(table => table.Partitions)
            .Any(partition => partition.Mode is not null);
        var dimToDimRelationships = project.Relationships.Count(relationship =>
            relationship.FromTable.StartsWith("Dim", StringComparison.OrdinalIgnoreCase) &&
            relationship.ToTable.StartsWith("Dim", StringComparison.OrdinalIgnoreCase));
        observations.Add(
            $"Star-schema compliance is not confirmed by naming alone; {factTables.Length} Fact*-named and {dimensionTables.Length} Dim*-named tables were detected, with {project.Relationships.Count} relationship(s).");
        observations.Add(dimToDimRelationships == 0
            ? "No relationship between two Dim*-named tables was detected; snowflake design is not otherwise inferred."
            : $"{dimToDimRelationships} relationship(s) connect Dim*-named tables; this is a name-based snowflake-pattern hypothesis that requires model-owner validation.");
        observations.Add("Fact grain, additivity, dimension completeness, uniqueness, null distribution, and aggregation suitability require source-data profiling.");
        observations.Add("Dataset size and storage mode are not represented in the parsed evidence; DirectQuery amplification and Direct Lake suitability remain unknown.");
        observations.Add($"Relationship direction metadata is known for {project.Relationships.Count(relationship => relationship.CrossFilteringBehavior is not null)} of {project.Relationships.Count} relationship(s).");
        return new PerformanceModelInventory(
            project.HasSemanticModel ? [project.ProjectName] : [],
            tables,
            relationships,
            project.Tables.Sum(table => table.Columns.Count(column => column.IsCalculated)),
            project.Tables.Count(table => table.IsCalculated),
            project.Tables.Sum(table => table.Hierarchies.Count),
            observations,
            hasPartitionPolicy
                ? "Partition mode metadata exists; incremental-refresh policy and historical partition behavior are not parsed."
                : "Not established: partition mode, refresh policy, and refresh history are unavailable.");
    }

    private static IReadOnlyList<PerformanceReusableArtifact> BuildReusableArtifacts(PbipProjectModel project)
    {
        var artifacts = new List<PerformanceReusableArtifact>();
        foreach (var table in project.Tables)
        {
            if (table.IsCalculated)
            {
                artifacts.Add(new PerformanceReusableArtifact(
                    "Calculated table",
                    table.Name,
                    table.FileName,
                    "Calculated-table metadata was parsed; expression and refresh semantics require model-owner validation."));
            }
            artifacts.AddRange(table.Measures.Select(measure => new PerformanceReusableArtifact(
                "Measure",
                measure.Name,
                $"{table.Name}[{measure.Name}]",
                "Expression is available in the uploaded semantic-model definition.")));
            artifacts.AddRange(table.Columns
                .Where(column => column.IsCalculated)
                .Select(column => new PerformanceReusableArtifact(
                    "Calculated column",
                    column.Name,
                    $"{table.Name}[{column.Name}]",
                    string.IsNullOrWhiteSpace(column.Expression)
                        ? "Calculated-column definition is identified; expression was not parsed."
                        : "Calculated-column expression is available in the uploaded definition.")));
            artifacts.AddRange(table.Hierarchies.Select(hierarchy => new PerformanceReusableArtifact(
                "Hierarchy",
                hierarchy.Name,
                table.Name,
                hierarchy.Levels.Count == 0
                    ? "Hierarchy definition is present; levels were not parsed."
                    : $"Static levels: {string.Join(", ", hierarchy.Levels)}.")));
            artifacts.AddRange(table.Partitions.Select(partition => new PerformanceReusableArtifact(
                "Partition",
                partition.Name,
                table.Name,
                $"Mode: {partition.Mode ?? "unknown"}; source type: {partition.SourceType ?? "unknown"}.")));
        }
        return artifacts;
    }

    private static IReadOnlyList<PerformanceReportInventory> BuildReportInventory(
        IReadOnlyList<ModelReportFile> reportFiles,
        string projectName)
    {
        var visualFiles = reportFiles
            .Where(file => file.RelativePath.EndsWith("visual.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pageFiles = reportFiles
            .Where(file => file.RelativePath.Replace('\\', '/').Contains("/pages/", StringComparison.OrdinalIgnoreCase) &&
                file.RelativePath.EndsWith("page.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pageOrderFile = reportFiles.FirstOrDefault(file =>
            file.RelativePath.Replace('\\', '/').EndsWith("/pages/pages.json", StringComparison.OrdinalIgnoreCase));
        var pageIds = (pageOrderFile is null
                ? Array.Empty<string>()
                : ReadJsonStringArray(pageOrderFile.Content, "pageOrder"))
            .Concat(pageFiles.Select(file => GetPageId(file.RelativePath)))
            .Concat(visualFiles.Select(file => GetPageId(file.RelativePath)))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reportWide = reportFiles.FirstOrDefault(file =>
            file.RelativePath.EndsWith("report.json", StringComparison.OrdinalIgnoreCase));
        var reportText = reportWide?.Content ?? string.Empty;
        var reportName = reportFiles.FirstOrDefault(file =>
                file.RelativePath.EndsWith("definition.pbir", StringComparison.OrdinalIgnoreCase))
            is { } descriptor
                ? Path.GetFileName(Path.GetDirectoryName(descriptor.RelativePath))
                : projectName;
        if (string.IsNullOrWhiteSpace(reportName))
        {
            reportName = projectName;
        }
        bool? hasBookmarks = reportFiles.Count == 0
            ? null
            : reportFiles.Any(file =>
                file.RelativePath.Contains("bookmark", StringComparison.OrdinalIgnoreCase)) ||
                ContainsJsonProperty(reportText, "bookmarks");
        var hasCrossFiltering = ContainsJsonProperty(reportText, "visualInteractions");
        bool? hasConditionalFormatting = visualFiles.Length == 0
            ? null
            : visualFiles.Any(file => ContainsJsonProperty(file.Content, "conditionalFormatting"));

        return pageIds.Select(pageId =>
        {
            var pageFile = pageFiles.FirstOrDefault(file =>
                string.Equals(GetPageId(file.RelativePath), pageId, StringComparison.OrdinalIgnoreCase));
            var pageName = pageFile is null
                ? pageId
                : ReadJsonString(pageFile.Content, "displayName") ?? ReadJsonString(pageFile.Content, "name") ?? pageId;
            var pageVisuals = visualFiles
                .Where(file => string.Equals(GetPageId(file.RelativePath), pageId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var types = pageVisuals.Select(file => ReadJsonString(file.Content, "visualType")).ToArray();
            var slicerCount = types.Count(type => string.Equals(type, "slicer", StringComparison.OrdinalIgnoreCase));
            var pageText = pageFile?.Content ?? string.Empty;
            bool? tooltip = pageFile is null
                ? null
                : ReadJsonString(pageText, "pageType")?.Contains("tooltip", StringComparison.OrdinalIgnoreCase) == true ||
                    ContainsJsonProperty(pageText, "tooltip")
                    ? true
                    : null;
            bool? drillthrough = pageFile is null
                ? null
                : ContainsJsonProperty(pageText, "drillthrough") ||
                    ContainsJsonProperty(pageText, "drillThrough")
                    ? true
                    : null;
            var customVisuals = types.Count(type => type is not null &&
                (type.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("customVisual", StringComparison.OrdinalIgnoreCase)));
            var bottlenecks = pageVisuals.Length >= 12
                ? new[] { $"{pageVisuals.Length} visuals are defined on this page; density is a review signal only, not a measured bottleneck." }
                : Array.Empty<string>();
            var opportunities = new List<string>
            {
                "Use Performance Analyzer to record visual rendering and DAX query durations before prioritizing changes."
            };
            if (types.Any(type => type is null))
            {
                opportunities.Add("Some visual types could not be identified from the uploaded JSON.");
            }
            if (customVisuals == 0)
            {
                opportunities.Add("Custom visual use could not be conclusively classified from the uploaded JSON.");
            }

            return new PerformanceReportInventory(
                reportName,
                pageName,
                pageVisuals.Length,
                slicerCount,
                customVisuals > 0 ? customVisuals : null,
                hasBookmarks,
                drillthrough,
                tooltip,
                hasConditionalFormatting,
                hasCrossFiltering ? true : null,
                bottlenecks,
                opportunities,
                "Static PBIR definition files only; rendering times and interaction telemetry are unavailable.");
        }).ToArray();
    }

    private static IReadOnlyList<PerformanceOpportunity> BuildOpportunities(
        PbipProjectModel project,
        IReadOnlyList<PerformanceDaxInventory> daxInventory)
    {
        var opportunities = new List<PerformanceOpportunity>();
        if (daxInventory.Any(measure => measure.Issues.Any(issue =>
                issue.Issue == "SUMX iterates a direct column reference")))
        {
            opportunities.Add(new PerformanceOpportunity(
                "OPT-001",
                "Safe implementation candidate — low risk, high confidence",
                "A direct SUMX(column) pattern was statically identified. This is a candidate pattern, not a measured bottleneck or a guaranteed faster rewrite.",
                "Confirm expression semantics and result equivalence; capture a representative baseline and compare DAX Studio or Performance Analyzer traces.",
                "The rewritten measure returns identical results for representative filter contexts and the agreed benchmark shows no regression; report any gain only if measured.",
                "Low; no rewrite is applied automatically, and semantic equivalence must be proven.",
                "High for pattern detection; not a confidence claim about performance impact."));
        }

        if (project.HasSemanticModel)
        {
            var reviewOptions = new ReviewOptions();
            var unusedFindings = new IReviewRule[]
            {
                new UnusedTablesRule(),
                new UnusedColumnsRule(),
                new UnusedMeasuresRule()
            }.SelectMany(rule => rule.Evaluate(project, reviewOptions).Findings).ToArray();
            foreach (var finding in unusedFindings)
            {
                opportunities.Add(new PerformanceOpportunity(
                    "OPT-002",
                    "Requires validation — moderate risk, moderate confidence",
                    finding.Message + " " + finding.Recommendation,
                    "Check all report pages, hidden/sort columns, RLS, refresh dependencies, external reports, downstream models, and consumer queries before changing the model.",
                    "A model owner confirms no supported consumer or dependency uses the object and regression tests pass before removal.",
                    "Moderate; removing referenced or externally consumed objects can break reports or refresh.",
                    "Medium for the included static evidence only."));
            }
        }

        opportunities.Add(new PerformanceOpportunity(
            "OPT-003",
            "Requires telemetry or benchmarking — high uncertainty",
            "The uploaded definitions do not provide query duration, visual rendering duration, refresh duration, resource usage, or capacity traces; performance impact is therefore unknown.",
            "Collect representative Performance Analyzer logs, DAX Studio traces, refresh history, and capacity metrics for the same workload and model version.",
            "A reproducible baseline and post-change comparison is reviewed by the model owner; no improvement claim is made without measured evidence.",
            "High uncertainty; static analysis cannot establish a runtime bottleneck.",
            "Low until telemetry is supplied."));
        return opportunities;
    }

    private static IReadOnlyList<PerformanceFinding> BuildFindings(
        PbipProjectModel project,
        PerformanceModelInventory model,
        IReadOnlyList<PerformanceReportInventory> reports,
        IReadOnlyList<PerformanceDaxInventory> dax,
        IReadOnlyList<PerformanceOpportunity> opportunities)
    {
        var findings = new List<PerformanceFinding>();
        foreach (var measure in dax)
        {
            findings.AddRange(measure.Issues.Select(issue => new PerformanceFinding(
                $"{measure.TableName}[{measure.MeasureName}]: {issue.Issue}",
                issue.TechnicalExplanation,
                $"Static DAX text: {measure.Expression}",
                issue.RecommendedRewritePattern,
                issue.Severity is "Medium" or "High" or "Critical" ? "Medium" : "Low",
                "Not measured; no performance improvement is claimed.",
                issue.EvidenceType,
                issue.Confidence,
                measure.ObjectType == "Calculated column" ? "Model Design" : "DAX Design",
                issue.Issue == "SUMX iterates a direct column reference" ? "OPT-001" :
                    issue.EvidenceType == "Optimization hypothesis" ? "OPT-003" : null,
                issue.Severity,
                issue.RiskOfChange)));
        }

        foreach (var opportunity in opportunities.Where(item => item.Tag == "OPT-002"))
        {
            findings.Add(new PerformanceFinding(
                opportunity.Reason.Split('.', 2)[0],
                "Potential maintainability/model-footprint opportunity; no runtime effect is established.",
                opportunity.Reason,
                opportunity.RequiredValidation,
                "Low until consumer and dependency validation is complete.",
                "Not measured; no performance improvement is claimed.",
                "Static analysis observation",
                opportunity.Confidence,
                "Model Design",
                opportunity.Tag,
                "Low",
                opportunity.Risk));
        }

        var namedFacts = project.Tables.Where(table =>
            table.Name.StartsWith("Fact", StringComparison.OrdinalIgnoreCase)).ToArray();
        var namedDimensions = project.Tables.Where(table =>
            table.Name.StartsWith("Dim", StringComparison.OrdinalIgnoreCase)).ToArray();
        findings.Add(new PerformanceFinding(
            "Star-schema compliance is not verified",
            "Table names and relationship endpoints do not establish fact grain, dimension quality, key uniqueness, or intended filter behavior.",
            $"Static evidence: {namedFacts.Length} Fact*-named table(s), {namedDimensions.Length} Dim*-named table(s), and {project.Relationships.Count} relationship(s).",
            "Confirm business grain, one-to-many cardinality, dimension key uniqueness, active paths, and filter direction with the model owner and source data.",
            "Medium for design validation.",
            "Not measured; no performance improvement is claimed.",
            "Static analysis observation",
            "High",
            "Model Design"));

        var snowflakeRelationships = project.Relationships.Where(relationship =>
            relationship.FromTable.StartsWith("Dim", StringComparison.OrdinalIgnoreCase) &&
            relationship.ToTable.StartsWith("Dim", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (snowflakeRelationships.Length > 0)
        {
            findings.Add(new PerformanceFinding(
                "Possible snowflake pattern between Dim*-named tables",
                "Additional dimension joins may affect model complexity; naming does not prove that these tables are dimensions or that the pattern is problematic.",
                $"{snowflakeRelationships.Length} relationship(s) connect tables whose names begin with 'Dim'.",
                "Validate the intended dimensional design, relationship cardinality, and source grain; compare a flattened design only if workload evidence justifies it.",
                "Low until validated.",
                "Not measured; no performance improvement is claimed.",
                "Optimization hypothesis",
                "Low",
                "Model Design",
                "OPT-003",
                "Low",
                "High uncertainty"));
        }

        var bidirectionalRelationships = project.Relationships.Where(relationship => relationship.IsBidirectional).ToArray();
        if (bidirectionalRelationships.Length > 0)
        {
            findings.Add(new PerformanceFinding(
                $"{bidirectionalRelationships.Length} bidirectional relationship(s) are defined",
                "Bidirectional filtering can make filter propagation and results harder to reason about; performance impact is not established.",
                string.Join("; ", bidirectionalRelationships.Select(relationship =>
                    $"{relationship.FromTable}[{relationship.FromColumn}] ↔ {relationship.ToTable}[{relationship.ToColumn}]")),
                "Verify the required filter path and ambiguity behavior; retain or change direction only after functional tests and workload traces.",
                "Medium for model validation.",
                "Not measured; no performance improvement is claimed.",
                "Static analysis observation",
                "High",
                "Model Design"));
        }

        findings.Add(new PerformanceFinding(
            "Incremental refresh and aggregation suitability are not established",
            "Refresh partitions, data volume, and query workload are needed to determine whether incremental refresh or aggregations are appropriate.",
            model.IncrementalRefreshReadiness,
            "Collect refresh policy/history, data volume and query workload; assess incremental refresh and aggregations against supported source/model constraints.",
            "High for evidence collection.",
            "Not measured; no performance improvement is claimed.",
            "Static analysis observation",
            "High",
            "Refresh Design"));

        findings.Add(new PerformanceFinding(
            "Fabric/Premium capacity readiness is not assessed",
            "Capacity limits, model footprint, storage mode, and resource usage are unavailable.",
            "The uploaded artifact contains no capacity or resource telemetry.",
            "Confirm target capacity/SKU, model size, storage mode, large-model settings, Direct Lake/DirectQuery suitability, and measured peak utilization.",
            "High for evidence collection.",
            "Not measured; no performance improvement is claimed.",
            "Static analysis observation",
            "High",
            "Capacity Considerations"));

        foreach (var relationship in model.Relationships.Where(item =>
                     item.FromCardinality is null || item.ToCardinality is null))
        {
            findings.Add(new PerformanceFinding(
                $"Relationship cardinality is unavailable: {relationship.From} → {relationship.To}",
                "Star-schema compliance, uniqueness, and relationship filtering implications cannot be verified.",
                relationship.Evidence,
                "Collect model metadata that includes from/to cardinality and validate key uniqueness against source data.",
                "High for evidence collection.",
                "Not applicable; no optimization benefit is claimed.",
                "Static analysis observation",
                "High",
                "Model Design"));
        }

        foreach (var report in reports.Where(item => item.VisualCount >= 12))
        {
            findings.Add(new PerformanceFinding(
                $"Page '{report.PageName}' contains {report.VisualCount} visuals",
                "A high visual count can increase work on interactions, but no rendering bottleneck is demonstrated.",
                report.Evidence,
                "Use Performance Analyzer to identify which visuals, if any, have material rendering or DAX duration.",
                "Low; prioritize only after telemetry.",
                "Not measured; visual density is not a performance result.",
                "Optimization hypothesis",
                "Low",
                "Visual Design",
                "OPT-003",
                "Low",
                "High uncertainty"));
        }

        if (!project.HasSemanticModel)
        {
            findings.Add(new PerformanceFinding(
                "Semantic model not included in the uploaded artifact",
                "Model, relationship, DAX-measure, partition, and refresh design could not be reviewed.",
                "Only report definition files were available.",
                "Upload the PBIP project including its semantic-model folder or a supported Model.bim/TMDL artifact.",
                "High for completing static assessment.",
                "Not applicable.",
                "Static analysis observation",
                "High",
                "Model Design"));
        }

        return findings;
    }

    private static IReadOnlyList<PerformanceEvidenceGap> BuildEvidenceGaps(
        PbipProjectModel project,
        IReadOnlyList<PerformanceReportInventory> reports)
    {
        var gaps = new List<PerformanceEvidenceGap>
        {
            new("Model memory footprint", "Memory limits and model-size pressure cannot be assessed from names or project files.", "Collect XMLA/TOM model size or approved capacity metrics for the exact semantic-model version.", "High", "Capacity Considerations"),
            new("Dataset size and row/distinct counts", "Cardinality, compression, and aggregation decisions depend on actual data distributions.", "Collect per-table row counts, column cardinality, and encoded size from an approved model metadata/VertiPaq tool.", "High", "Model Design"),
            new("Refresh duration and history", "Static partitions do not reveal refresh cost, failures, or incremental-refresh effectiveness.", "Export Power BI refresh history for representative periods and identify gateway/source timings.", "High", "Refresh Design"),
            new("Query duration and DAX Studio traces", "DAX syntax alone cannot show storage-engine/formula-engine work or hot queries.", "Capture representative DAX Studio Server Timings and query plans with a documented filter context.", "High", "DAX Design"),
            new("Visual rendering duration and Performance Analyzer logs", "Visual counts do not measure render time, DAX duration, or interaction delay.", "Capture Performance Analyzer logs for representative pages and interactions.", "High", "Visual Design"),
            new("Capacity/resource metrics", "CPU, memory, throttling, and concurrency determine Fabric/Premium readiness.", "Use approved Fabric Capacity Metrics or Power BI Premium capacity metrics over representative peak periods.", "High", "Capacity Considerations"),
            new("Storage mode and DirectQuery telemetry", "The model artifact does not expose runtime source-query amplification or latency.", "Collect model storage-mode metadata and source query traces for representative interactions.", "Medium", "Model Design"),
            new("Fabric/Premium dataset-size indicators", "Large-model and Direct Lake suitability depend on service limits and model/data footprint.", "Record workspace capacity/SKU, semantic-model size, Direct Lake eligibility, and relevant service limits.", "Medium", "Capacity Considerations"),
            new("DAX Studio traces", "A static measure scan cannot confirm whether a flagged expression is a bottleneck.", "Run DAX Studio against the same model version and workload; retain server timings and query plans.", "High", "DAX Design"),
            new("Performance Analyzer logs", "Visual definitions do not include rendering duration or visual-level DAX timings.", "Export Performance Analyzer results for the pages and user paths under review.", "High", "Visual Design")
        };
        if (project.Relationships.Any(relationship =>
                relationship.FromCardinality is null || relationship.ToCardinality is null) ||
            project.Relationships.Count == 0)
        {
            gaps.Add(new PerformanceEvidenceGap(
                "Relationship cardinality",
                "One-to-many/one-to-one assumptions, key uniqueness, and filter propagation cannot be verified.",
                "Export complete relationship metadata and validate key uniqueness/cardinality against the source data.",
                "High",
                "Model Design"));
        }
        if (reports.Count == 0)
        {
            gaps.Add(new PerformanceEvidenceGap(
                "Report definitions",
                "Visual count, page structure, custom visuals, slicers, and report interactions are unavailable.",
                "Upload the PBIP/PBIR report definition files with pages and visual JSON definitions.",
                "High",
                "Visual Design"));
        }

        return gaps;
    }

    private static IReadOnlyList<PerformanceGate> BuildGates(
        PbipProjectModel project,
        IReadOnlyList<PerformanceReportInventory> reports,
        IReadOnlyList<PerformanceOpportunity> opportunities,
        IReadOnlyList<PerformanceEvidenceGap> gaps)
    {
        var po0Missing = new List<string>();
        if (!project.HasSemanticModel || project.Tables.Count == 0)
        {
            po0Missing.Add("Semantic model inventory");
        }
        if (reports.Count == 0)
        {
            po0Missing.Add("Report/page inventory");
        }
        var po1Missing = new List<string>();
        if (!project.HasSemanticModel)
        {
            po1Missing.Add("Static semantic-model review");
        }
        if (reports.Count == 0)
        {
            po1Missing.Add("Static visual/report review");
        }

        var po2Missing = gaps.Select(gap => gap.MissingEvidence).ToArray();
        var po2Passed = po2Missing.Length == 0;
        var po3Passed = po0Missing.Count == 0 &&
            po1Missing.Count == 0 &&
            po2Passed &&
            opportunities.Count > 0;
        return
        [
            new PerformanceGate(
                "PO-0 Discovery Complete",
                po0Missing.Count == 0 ? "Complete for the uploaded artifact" : "Incomplete",
                po0Missing.Count == 0,
                po0Missing,
                po0Missing.Count == 0 ? [] : ["Upload the semantic model and report definitions together for a complete inventory."]),
            new PerformanceGate(
                "PO-1 Static Analysis Complete",
                po1Missing.Count == 0 ? "Complete for parsed evidence" : "Incomplete",
                po1Missing.Count == 0,
                po1Missing,
                po1Missing.Count == 0 ? [] : ["Provide the missing model or report definitions and rerun static analysis."]),
            new PerformanceGate(
                "PO-2 Evidence Available",
                "Blocked — telemetry and resource benchmarks were not supplied",
                false,
                po2Missing,
                ["Collect the priority telemetry and benchmark artifacts before making runtime optimization conclusions."]),
            new PerformanceGate(
                "PO-3 Optimization Ready",
                po3Passed
                    ? "Ready for owner-reviewed implementation planning"
                    : "Blocked — PO-2 evidence is unavailable; a static plan is prepared",
                po3Passed,
                po3Passed ? [] : ["PO-2 telemetry, resource usage, and benchmark evidence"],
                ["Prioritize candidates with the model owner; establish a baseline and acceptance test before making changes."])
        ];
    }

    private static FabricReadiness BuildFabricReadiness(
        PbipProjectModel project,
        IReadOnlyList<PerformanceEvidenceGap> gaps) =>
        new(
            project.HasSemanticModel
                ? "A static semantic-model inventory is available; service/capacity state and model size are unknown."
                : "No semantic model was included; Fabric/Premium readiness cannot be assessed.",
            [
                "Model size, memory footprint, capacity SKU, and throttling metrics are unavailable.",
                "Direct Lake, DirectQuery, composite-model behavior, and large-model settings are not verified.",
                "Incremental-refresh policy and refresh history are not established from the available metadata."
            ],
            "Not scored — required size, mode, capacity, and telemetry evidence is missing.",
            [
                "Collect model size and storage-mode metadata.",
                "Confirm the target Fabric/Premium capacity and applicable model limits.",
                "Benchmark representative refreshes, queries, and report interactions."
            ]);

    private static IReadOnlyList<string> BuildRisks(PbipProjectModel project)
    {
        var risks = new List<string>
        {
            "Static patterns do not prove a runtime bottleneck or a performance gain.",
            "The DAX scan is lexical and does not fully parse DAX or execute measures.",
            "Uploaded files may omit external reports, downstream semantic models, RLS dependencies, and refresh consumers."
        };
        if (!project.HasSemanticModel)
        {
            risks.Add("No semantic model was included, so model and measure recommendations are incomplete.");
        }
        return risks;
    }

    private static IReadOnlyList<string> BuildNextSteps(
        PbipProjectModel project,
        IReadOnlyList<PerformanceReportInventory> reports)
    {
        var steps = new List<string>();
        if (!project.HasSemanticModel || reports.Count == 0)
        {
            steps.Add("Upload the semantic model and report definitions together to complete PO-0 and PO-1 inventory.");
        }
        steps.Add("Have the model owner validate candidate semantics, consumers, relationships, and refresh dependencies.");
        steps.Add("Collect DAX Studio, Performance Analyzer, refresh, model-size, and capacity evidence for representative workloads.");
        steps.Add("Define a reproducible baseline and acceptance tests before implementing any recommendation.");
        return steps;
    }

    private static bool ContainsNestedFunction(
        string expression,
        string outerFunction,
        IReadOnlySet<string> innerFunctions)
    {
        foreach (Match outer in FunctionCall.Matches(expression))
        {
            if (!outer.Groups["name"].Value.Equals(outerFunction, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var openIndex = expression.IndexOf('(', outer.Index + outer.Length - 1);
            var closeIndex = FindMatchingParenthesis(expression, openIndex);
            if (closeIndex > openIndex &&
                FunctionCall.Matches(expression[(openIndex + 1)..closeIndex])
                    .Any(match => innerFunctions.Contains(match.Groups["name"].Value)))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindMatchingParenthesis(string expression, int openIndex)
    {
        var depth = 0;
        for (var index = openIndex; index < expression.Length; index++)
        {
            if (expression[index] == '(')
            {
                depth++;
            }
            else if (expression[index] == ')' && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeIdentifier(string value) =>
        value.Trim().Trim('\'');

    private static string NormalizeExpression(string value) =>
        Regex.Replace(value, @"\s+", string.Empty).ToUpperInvariant();

    private static string GetArtifactType(string projectPath, string? uploadedExtension)
    {
        var extension = uploadedExtension ?? Path.GetExtension(projectPath);
        if (extension.Length > 0)
        {
            return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                ? "PBIP/PBIR or model artifact ZIP"
                : extension.TrimStart('.').ToUpperInvariant();
        }
        return Directory.GetFiles(projectPath, "*.pbip", SearchOption.TopDirectoryOnly).Length > 0
            ? "PBIP"
            : Directory.GetFiles(projectPath, "*.bim", SearchOption.AllDirectories).Length > 0
                ? "Model.bim"
                : Directory.GetFiles(projectPath, "*.tmdl", SearchOption.AllDirectories).Length > 0
                    ? "TMDL"
                    : Directory.GetFiles(projectPath, "*.dax", SearchOption.AllDirectories).Length > 0
                        ? "DAX"
                        : "PBIR";
    }

    private static string GetPageId(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pagesIndex = Array.FindIndex(segments, segment =>
            segment.Equals("pages", StringComparison.OrdinalIgnoreCase));
        return pagesIndex >= 0 && pagesIndex + 1 < segments.Length
            ? segments[pagesIndex + 1]
            : string.Empty;
    }

    private static bool ContainsJsonProperty(string content, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            return ContainsJsonProperty(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsJsonProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) ||
                    ContainsJsonProperty(property.Value, propertyName))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item => ContainsJsonProperty(item, propertyName));
        }
        return false;
    }

    private static string? ReadJsonString(string content, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            return ReadJsonString(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadJsonStringArray(string content, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(content);
            return TryGetProperty(document.RootElement, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .ToArray()
                    : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = ReadJsonString(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ReadJsonString(item, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        return null;
    }
}
