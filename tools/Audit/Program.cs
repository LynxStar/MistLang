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
        _ = Directory.CreateDirectory(auditDir);
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

        // Cross-reference pass: compute external dependents for each type
        var declaredTypes = MetricsComputer.CollectDeclaredTypes(repoRoot, files.ToList());

        var typeToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var (typeName, (filePath, _)) in declaredTypes)
        {
            typeToFile[typeName] = filePath;
        }

        var dependents = MetricsComputer.ComputeExternalDependents(repoRoot, files.ToList(), typeToFile);

        // Update FileMetrics entries with dependents counts
        for(var i = 0; i < mergedEntries.Count; i++)
        {
            var entry = mergedEntries[i];
            if(entry.Metrics.Types.Count == 0)
            {
                continue;
            }

            var updatedTypes = new List<TypeInfo>(entry.Metrics.Types.Count);
            var changed = false;

            foreach(var typeInfo in entry.Metrics.Types)
            {
                if(dependents.TryGetValue(typeInfo.Name, out var count) && count > 0)
                {
                    updatedTypes.Add(typeInfo with { ExternalDependents = count });
                    changed = true;
                }
                else
                {
                    updatedTypes.Add(typeInfo);
                }
            }

            if(changed)
            {
                mergedEntries[i] = entry with { Metrics = new FileMetrics(entry.Metrics.LineCount, updatedTypes) };
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
                        CreditsUsed = file.CreditsUsed + result.CreditsUsed,
                    });
                store.Save(state);
                Console.WriteLine($"  Completed: {findings.Count} finding(s) ({result.CreditsUsed} credits)");
            }
            else
            {
                state = ReplaceFileEntry(state, file.RelativePath,
                    file with
                    {
                        Status = AuditStatus.Failed,
                        FailureReason = result.ErrorMessage,
                        CreditsUsed = file.CreditsUsed + result.CreditsUsed,
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
            var openFindings = file.Findings
                .Where(f => f.Status == FindingStatus.Open)
                .ToList();

            if(command.FindingIndex is not null)
            {
                var idx = command.FindingIndex.Value - 1;
                if(idx < 0 || idx >= file.Findings.Count)
                {
                    Console.Error.WriteLine($"Error: Finding index {command.FindingIndex.Value} is out of range (file has {file.Findings.Count} findings).");
                    return 1;
                }

                var targetFinding = file.Findings[idx];
                if(targetFinding.Status != FindingStatus.Open)
                {
                    Console.Error.WriteLine($"Error: Finding {command.FindingIndex.Value} is not open (status: {targetFinding.Status}).");
                    return 1;
                }

                openFindings = [targetFinding];
            }

            foreach(var finding in openFindings)
            {
                Console.WriteLine($"Remediating: {file.RelativePath} [{finding.Category}]");

                // Transition this finding to InProgress
                state = ReplaceFinding(state, file.RelativePath, finding,
                    finding with { Status = FindingStatus.InProgress });
                store.Save(state);

                // Dispatch remediation for this single finding
                var result = dispatcher.DispatchRemediation(file.RelativePath, finding);

                if(result.Succeeded)
                {
                    var newStatus = result.Dismissed ? FindingStatus.Dismissed : FindingStatus.Resolved;
                    state = ReplaceFinding(state, file.RelativePath,
                        finding with { Status = FindingStatus.InProgress },
                        finding with
                        {
                            Status = newStatus,
                            CreditsUsed = finding.CreditsUsed + result.CreditsUsed,
                            Resolution = result.Resolution,
                        });
                    store.Save(state);

                    if(result.Dismissed)
                    {
                        Console.WriteLine($"  Dismissed: {result.Resolution} ({result.CreditsUsed} credits)");
                    }
                    else
                    {
                        Console.WriteLine($"  Resolved: {result.Resolution} ({result.CreditsUsed} credits)");
                    }
                }
                else
                {
                    state = ReplaceFinding(state, file.RelativePath,
                        finding with { Status = FindingStatus.InProgress },
                        finding with { Status = FindingStatus.Open, CreditsUsed = finding.CreditsUsed + result.CreditsUsed });
                    store.Save(state);
                    Console.WriteLine($"  Failed: {result.ErrorMessage}");
                }
            }

            // Check if all findings for this file are resolved or dismissed
            var currentFile = state.Files.First(
                f => string.Equals(f.RelativePath, file.RelativePath, StringComparison.Ordinal));

            if(currentFile.Findings.All(f => f.Status is FindingStatus.Resolved or FindingStatus.Dismissed))
            {
                state = ReplaceFileEntry(state, file.RelativePath,
                    currentFile with { Status = AuditStatus.Remediated });
                store.Save(state);
            }
        }

        // Regenerate audit.md
        WriteReport(state, repoRoot);

        return 0;
    }

    private static AuditState ReplaceFinding(AuditState state, string relativePath, Finding original, Finding updated)
    {
        var files = state.Files.Select(f =>
        {
            if(!string.Equals(f.RelativePath, relativePath, StringComparison.Ordinal))
            {
                return f;
            }

            var updatedFindings = f.Findings.Select(finding =>
                ReferenceEquals(finding, original) || finding == original ? updated : finding)
                .ToList();
            return f with { Findings = updatedFindings };
        }).ToList();

        return new AuditState(files);
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