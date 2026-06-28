using System.Text;
using Audit.Models;

namespace Audit;

public static class ReportGenerator
{
    private static readonly string[] ProjectPrefixes =
    [
        "Source/Mist/",
        "Source/Mist.CLI/",
        "Source/Storm.SourceGenerator/",
    ];

    private static readonly string[] ProjectNames =
    [
        "Mist",
        "Mist.CLI",
        "Storm.SourceGenerator",
    ];

    /// <summary>
    /// Generates AUDIT.md content from the current audit state.
    /// Groups files by project, sorts alphabetically, includes summary tables.
    /// Excludes files with Removed status.
    /// </summary>
    public static string Generate(AuditState state)
    {
        var sb = new StringBuilder();

        var activeFiles = state.Files
            .Where(f => f.Status != AuditStatus.Removed)
            .ToList();

        _ = sb.AppendLine("# Compiler Audit Report");
        _ = sb.AppendLine();

        AppendSummaryTable(sb, activeFiles);
        AppendFindingsSummaryTable(sb, activeFiles);
        AppendProjectSections(sb, activeFiles);

        return sb.ToString();
    }

    private static void AppendSummaryTable(StringBuilder sb, List<FileEntry> files)
    {
        _ = sb.AppendLine("## Summary");
        _ = sb.AppendLine("| Status | Count |");
        _ = sb.AppendLine("|--------|-------|");

        foreach(var status in Enum.GetValues<AuditStatus>())
        {
            if(status == AuditStatus.Removed)
            {
                continue;
            }

            var count = files.Count(f => f.Status == status);
            if(count > 0)
            {
                _ = sb.AppendLine($"| {status} | {count} |");
            }
        }

        _ = sb.AppendLine();
    }

    private static void AppendFindingsSummaryTable(StringBuilder sb, List<FileEntry> files)
    {
        _ = sb.AppendLine("## Findings Summary");
        _ = sb.AppendLine("| Status | Count |");
        _ = sb.AppendLine("|--------|-------|");

        var allFindings = files.SelectMany(f => f.Findings).ToList();

        foreach(var status in Enum.GetValues<FindingStatus>())
        {
            var count = allFindings.Count(f => f.Status == status);
            if(count > 0)
            {
                _ = sb.AppendLine($"| {status} | {count} |");
            }
        }

        _ = sb.AppendLine();
    }

    private static void AppendProjectSections(StringBuilder sb, List<FileEntry> files)
    {
        for(var i = 0; i < ProjectPrefixes.Length; i++)
        {
            var prefix = ProjectPrefixes[i];
            var projectName = ProjectNames[i];

            var projectFiles = files
                .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
                .ToList();

            if(projectFiles.Count == 0)
            {
                continue;
            }

            _ = sb.AppendLine($"## {projectName}/");
            _ = sb.AppendLine();
            _ = sb.AppendLine("| File | Lines | Status | Findings |");
            _ = sb.AppendLine("|------|-------|--------|----------|");

            foreach(var file in projectFiles)
            {
                var relativePath = file.RelativePath[prefix.Length..];
                var findings = FormatFindings(file.Findings);
                _ = sb.AppendLine($"| {relativePath} | {file.Metrics.LineCount} | {file.Status} | {findings} |");
            }

            _ = sb.AppendLine();
        }
    }

    private static string FormatFindings(IReadOnlyList<Finding> findings)
    {
        if(findings.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", findings.Select(f =>
        {
            var parts = new List<string> { f.Category.ToString() };
            if(!string.IsNullOrWhiteSpace(f.Note))
            {
                parts.Add(f.Note);
            }

            parts.Add(f.Status.ToString());
            return string.Join(": ", parts);
        }));
    }
}