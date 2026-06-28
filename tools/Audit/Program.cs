using Audit.Models;

namespace Audit;

internal class Program
{
    private static int Main(string[] args)
    {
        var command = CommandParser.Parse(args, Console.Error);
        if(command is null)
        {
            return 1;
        }

        var repoRoot = FindRepositoryRoot();
        if(repoRoot is null)
        {
            Console.Error.WriteLine("Error: Could not find repository root (no Mist.slnx found in parent directories).");
            return 1;
        }

        var store = new StateStore(repoRoot);

        return command.Command switch
        {
            Command.Enumerate => RunEnumerate(store, repoRoot),
            Command.Audit => RunAudit(store, repoRoot, command),
            Command.Remediate => RunRemediate(store, repoRoot, command),
            Command.Report => RunReport(store, repoRoot),
            Command.Status => RunStatus(store),
            _ => 1,
        };
    }

    private static string? FindRepositoryRoot()
    {
        var directory = Environment.CurrentDirectory;

        while(directory is not null)
        {
            if(File.Exists(Path.Combine(directory, "Mist.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static void WriteReport(AuditState state, string repoRoot)
    {
        var auditDir = Path.Combine(repoRoot, ".audit");
        Directory.CreateDirectory(auditDir);
        var reportPath = Path.Combine(auditDir, "audit.md");
        File.WriteAllText(reportPath, ReportGenerator.Generate(state));
    }

    private static int RunEnumerate(StateStore store, string repoRoot)
    {
        IReadOnlyList<string> files;

        try
        {
            files = FileEnumerator.Enumerate(repoRoot);
        }
        catch(DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        var existingState = store.Load();
        var existingByPath = existingState.Files
            .ToDictionary(f => f.RelativePath, StringComparer.Ordinal);

        var enumeratedSet = new HashSet<string>(files, StringComparer.Ordinal);
        var mergedEntries = new List<FileEntry>();

        foreach(var relativePath in files)
        {
            var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var metrics = MetricsComputer.Compute(absolutePath);

            if(existingByPath.TryGetValue(relativePath, out var existing))
            {
                mergedEntries.Add(existing with { Metrics = metrics });
            }
            else
            {
                mergedEntries.Add(new FileEntry(
                    relativePath,
                    AuditStatus.Unstarted,
                    metrics,
                    [],
                    null));
            }
        }

        foreach(var existing in existingState.Files)
        {
            if(!enumeratedSet.Contains(existing.RelativePath))
            {
                mergedEntries.Add(existing with { Status = AuditStatus.Removed });
            }
        }

        var updatedState = new AuditState(mergedEntries);
        store.Save(updatedState);

        WriteReport(updatedState, repoRoot);

        var activeCount = mergedEntries.Count(f => f.Status != AuditStatus.Removed);
        Console.WriteLine($"Enumerated {activeCount} files.");

        return 0;
    }

    private static int RunAudit(StateStore store, string repoRoot, ParsedCommand command)
    {
        var state = store.Load();

        List<FileEntry> filesToAudit;

        if(command.FilePath is not null)
        {
            var entry = state.Files.FirstOrDefault(
                f => string.Equals(f.RelativePath, command.FilePath, StringComparison.Ordinal));

            if(entry is null)
            {
                Console.Error.WriteLine($"Error: File '{command.FilePath}' is not tracked in audit state.");
                return 1;
            }

            filesToAudit = [entry];
        }
        else
        {
            filesToAudit = state.Files
                .Where(f => f.Status == AuditStatus.Unstarted)
                .ToList();
        }

        if(filesToAudit.Count == 0)
        {
            Console.WriteLine("No files to audit.");
            return 0;
        }

        var dispatcher = new AgentDispatcher(
            repoRoot,
            TimeSpan.FromSeconds(command.TimeoutSeconds));

        foreach(var file in filesToAudit)
        {
            Console.WriteLine($"Auditing: {file.RelativePath}");

            // Transition to InReview
            state = ReplaceFileEntry(state, file.RelativePath,
                file with { Status = AuditStatus.InReview });
            store.Save(state);

            // Read file content
            var absolutePath = Path.Combine(
                repoRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string fileContent;

            try
            {
                fileContent = File.ReadAllText(absolutePath);
            }
            catch(IOException ex)
            {
                state = ReplaceFileEntry(state, file.RelativePath,
                    file with { Status = AuditStatus.Failed, FailureReason = ex.Message });
                store.Save(state);
                Console.WriteLine($"  Failed: {ex.Message}");
                continue;
            }

            // Dispatch audit
            var result = dispatcher.DispatchAudit(file.RelativePath, fileContent, file.Metrics);

            if(result.Succeeded)
            {
                var findings = result.Findings ?? [];
                state = ReplaceFileEntry(state, file.RelativePath,
                    file with
                    {
                        Status = AuditStatus.Completed,
                        Findings = findings,
                    });
                store.Save(state);
                Console.WriteLine($"  Completed: {findings.Count} finding(s)");
            }
            else
            {
                state = ReplaceFileEntry(state, file.RelativePath,
                    file with
                    {
                        Status = AuditStatus.Failed,
                        FailureReason = result.ErrorMessage,
                    });
                store.Save(state);
                Console.WriteLine($"  Failed: {result.ErrorMessage}");
            }
        }

        // Regenerate AUDIT.md
        WriteReport(state, repoRoot);

        return 0;
    }

    private static AuditState ReplaceFileEntry(AuditState state, string relativePath, FileEntry updated)
    {
        var files = state.Files
            .Select(f => string.Equals(f.RelativePath, relativePath, StringComparison.Ordinal) ? updated : f)
            .ToList();
        return new AuditState(files);
    }

    private static int RunRemediate(StateStore store, string repoRoot, ParsedCommand command)
    {
        var state = store.Load();

        List<FileEntry> filesToRemediate;

        if(command.FilePath is not null)
        {
            var entry = state.Files.FirstOrDefault(
                f => string.Equals(f.RelativePath, command.FilePath, StringComparison.Ordinal));

            if(entry is null)
            {
                Console.Error.WriteLine($"Error: File '{command.FilePath}' is not tracked in audit state.");
                return 1;
            }

            filesToRemediate = [entry];
        }
        else
        {
            filesToRemediate = state.Files
                .Where(f => f.Findings.Any(finding => finding.Status == FindingStatus.Open))
                .ToList();
        }

        if(filesToRemediate.Count == 0)
        {
            Console.WriteLine("No files to remediate.");
            return 0;
        }

        var dispatcher = new AgentDispatcher(
            repoRoot,
            TimeSpan.FromSeconds(command.TimeoutSeconds));

        foreach(var file in filesToRemediate)
        {
            Console.WriteLine($"Remediating: {file.RelativePath}");

            var openFindings = file.Findings
                .Where(f => f.Status == FindingStatus.Open)
                .ToList();

            // Transition Open findings to InProgress
            var inProgressFindings = file.Findings
                .Select(f => f.Status == FindingStatus.Open
                    ? f with { Status = FindingStatus.InProgress }
                    : f)
                .ToList();

            state = ReplaceFileEntry(state, file.RelativePath,
                file with { Findings = inProgressFindings });
            store.Save(state);

            // Dispatch remediation
            var result = dispatcher.DispatchRemediation(file.RelativePath, openFindings);

            if(result.Succeeded)
            {
                // Resolve all InProgress findings
                var resolvedFindings = inProgressFindings
                    .Select(f => f.Status == FindingStatus.InProgress
                        ? f with { Status = FindingStatus.Resolved }
                        : f)
                    .ToList();

                // Check if ALL findings for the file are now Resolved
                var allResolved = resolvedFindings.All(f => f.Status == FindingStatus.Resolved);
                var updatedStatus = allResolved ? AuditStatus.Remediated : file.Status;

                state = ReplaceFileEntry(state, file.RelativePath,
                    file with { Status = updatedStatus, Findings = resolvedFindings });
                store.Save(state);
                Console.WriteLine($"  Resolved: {openFindings.Count} finding(s)");
            }
            else
            {
                // Revert InProgress findings back to Open, mark file as Failed
                var revertedFindings = inProgressFindings
                    .Select(f => f.Status == FindingStatus.InProgress
                        ? f with { Status = FindingStatus.Open }
                        : f)
                    .ToList();

                state = ReplaceFileEntry(state, file.RelativePath,
                    file with
                    {
                        Status = AuditStatus.Failed,
                        Findings = revertedFindings,
                        FailureReason = result.ErrorMessage,
                    });
                store.Save(state);
                Console.WriteLine($"  Failed: {result.ErrorMessage}");
            }
        }

        // Regenerate AUDIT.md
        WriteReport(state, repoRoot);

        return 0;
    }

    private static int RunReport(StateStore store, string repoRoot)
    {
        var state = store.Load();
        WriteReport(state, repoRoot);
        Console.WriteLine("Report written to .audit/audit.md");
        return 0;
    }

    private static int RunStatus(StateStore store)
    {
        var state = store.Load();

        var activeFiles = state.Files
            .Where(f => f.Status != AuditStatus.Removed)
            .ToList();

        var allFindings = activeFiles
            .SelectMany(f => f.Findings)
            .ToList();

        // Determine column width for file status counts
        var fileStatusEntries = Enum.GetValues<AuditStatus>()
            .Where(s => s != AuditStatus.Removed)
            .Select(s => (Name: s.ToString(), Count: activeFiles.Count(f => f.Status == s)))
            .ToList();

        var findingStatusEntries = Enum.GetValues<FindingStatus>()
            .Select(s => (Name: s.ToString(), Count: allFindings.Count(f => f.Status == s)))
            .ToList();

        var maxLabelWidth = fileStatusEntries
            .Concat(findingStatusEntries)
            .Max(e => e.Name.Length);

        var maxCount = fileStatusEntries
            .Concat(findingStatusEntries)
            .Max(e => e.Count);

        var countWidth = Math.Max(maxCount.ToString().Length, 1);

        Console.WriteLine("File Status:");
        foreach(var (name, count) in fileStatusEntries)
        {
            var label = $"{name}:".PadRight(maxLabelWidth + 1);
            var value = count.ToString().PadLeft(countWidth);
            Console.WriteLine($"  {label} {value}");
        }

        Console.WriteLine();
        Console.WriteLine("Finding Status:");
        foreach(var (name, count) in findingStatusEntries)
        {
            var label = $"{name}:".PadRight(maxLabelWidth + 1);
            var value = count.ToString().PadLeft(countWidth);
            Console.WriteLine($"  {label} {value}");
        }

        return 0;
    }
}