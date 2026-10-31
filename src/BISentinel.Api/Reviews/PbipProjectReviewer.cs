using Microsoft.Extensions.Options;

namespace BISentinel.Api.Reviews;

public interface IPbipProjectReviewer
{
    Task<PbipReviewResult> ReviewAsync(
        string projectPath,
        string projectId,
        string? projectName,
        CancellationToken cancellationToken);
}

public sealed class PbipProjectReviewer(
    IPbipProjectReader projectReader,
    IEnumerable<IReviewRule> rules,
    IOptionsMonitor<ReviewOptions> optionsMonitor,
    IReviewConfigurationStore configurationStore) : IPbipProjectReviewer
{
    public async Task<PbipReviewResult> ReviewAsync(
        string projectPath,
        string projectId,
        string? projectName,
        CancellationToken cancellationToken)
    {
        var configuration = await configurationStore.GetAsync(cancellationToken);
        var options = configuration.Configuration.ToReviewOptions(optionsMonitor.CurrentValue);
        var project = projectReader.Read(projectPath);
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            project = project with { ProjectName = projectName };
        }

        project = project with
        {
            Tables = PbipProjectMetadata.DeduplicateTables(project.Tables),
            Relationships = PbipProjectMetadata.DeduplicateRelationships(project.Relationships)
        };

        var findings = new List<PbipReviewFinding>();
        var checksRun = 0;
        var executedRuleKeys = new List<string>();

        foreach (var missingFile in project.RequiredFiles.Where(file => !File.Exists(file)))
        {
            checksRun++;
            findings.Add(new PbipReviewFinding(
                "PBIP-STRUCT-001",
                "Error",
                $"Required project file '{Path.GetFileName(missingFile)}' is missing.",
                Path.GetFileName(missingFile),
                "Restore the missing PBIP file or regenerate the project from Power BI Desktop."));
        }

        foreach (var rule in rules)
        {
            if (!project.HasSemanticModel && rule.RequiresSemanticModel)
            {
                continue;
            }

            var policy = options.GetPolicy(rule.Key);
            if (!policy.Enabled)
            {
                continue;
            }

            var result = rule.Evaluate(project, options);
            checksRun += result.ChecksRun;
            executedRuleKeys.Add(rule.Key);
            findings.AddRange(result.Findings.Select(finding =>
                finding with { Severity = policy.Severity }));
        }

        if (!project.HasSemanticModel)
        {
            checksRun++;
            findings.Add(new PbipReviewFinding(
                "FORMAT-001",
                "Information",
                "This report-only PBIR project has no semantic model; model rules were not run.",
                project.ProjectName,
                "Review a PBIP project ZIP or PBIT template to run semantic-model rules."));
        }

        var modelTables = project.Tables
            .Where(table => !IsGeneratedTable(table.Name, options.GeneratedTablePrefixes))
            .ToArray();
        var modelRelationships = project.Relationships
            .Where(relationship =>
                !IsGeneratedTable(relationship.FromTable, options.GeneratedTablePrefixes) &&
                !IsGeneratedTable(relationship.ToTable, options.GeneratedTablePrefixes))
            .ToArray();

        return new PbipReviewResult(
            project.ProjectName,
            projectId,
            modelTables.Length,
            project.Tables.Sum(static table => table.Measures.Count),
            modelRelationships.Length,
            checksRun,
            findings)
        {
            ExecutedRuleKeys = executedRuleKeys,
            ReportReferencesAvailable = project.ReportReferences is { Count: > 0 }
        };
    }

    private static bool IsGeneratedTable(string name, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
