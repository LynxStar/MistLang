using System.Diagnostics;
using System.Text.Json;
using Audit.Models;

namespace Audit;

public sealed record AgentResult(bool Succeeded, IReadOnlyList<Finding>? Findings, string? ErrorMessage);

public sealed class AgentDispatcher
{
    private readonly string workingDirectory;
    private readonly TimeSpan timeout;

    public AgentDispatcher(string workingDirectory, TimeSpan? timeout = null)
    {
        this.workingDirectory = workingDirectory;
        this.timeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// Dispatches an audit prompt and returns parsed findings, or an error message on failure.
    /// </summary>
    public AgentResult DispatchAudit(string filePath, string fileContent, FileMetrics metrics)
    {
        var prompt = PromptBuilder.BuildAuditPrompt(filePath, fileContent, metrics);
        var (exitCode, stdout, stderr, timedOut) = RunProcess(prompt);

        if(timedOut)
        {
            return new AgentResult(false, null, "Agent timed out");
        }

        if(exitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(stderr)
                ? $"Agent exited with code {exitCode}"
                : stderr.Trim();
            return new AgentResult(false, null, errorMessage);
        }

        var findings = ExtractFindings(stdout);
        if(findings is null)
        {
            return new AgentResult(false, null, "No JSON array found in agent output");
        }

        return new AgentResult(true, findings, null);
    }

    /// <summary>
    /// Dispatches a remediation prompt for specific findings on a file.
    /// </summary>
    public AgentResult DispatchRemediation(string filePath, IReadOnlyList<Finding> findings)
    {
        var prompt = PromptBuilder.BuildRemediationPrompt(filePath, findings);
        var (exitCode, _, stderr, timedOut) = RunProcess(prompt);

        if(timedOut)
        {
            return new AgentResult(false, null, "Agent timed out");
        }

        if(exitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(stderr)
                ? $"Agent exited with code {exitCode}"
                : stderr.Trim();
            return new AgentResult(false, null, errorMessage);
        }

        return new AgentResult(true, null, null);
    }

    private (int ExitCode, string Stdout, string Stderr, bool TimedOut) RunProcess(string prompt)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "kiro-cli",
            Arguments = "chat --no-interactive --trust-all-tools",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        using var process = new Process { StartInfo = startInfo };
        _ = process.Start();

        process.StandardInput.Write(prompt);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        var exited = process.WaitForExit((int)timeout.TotalMilliseconds);
        if(!exited)
        {
            process.Kill();
            return (1, stdout, stderr, true);
        }

        return (process.ExitCode, stdout, stderr, false);
    }

    private static IReadOnlyList<Finding>? ExtractFindings(string stdout)
    {
        var firstBracket = stdout.IndexOf('[');
        var lastBracket = stdout.LastIndexOf(']');

        if(firstBracket < 0 || lastBracket < 0 || lastBracket <= firstBracket)
        {
            return null;
        }

        var json = stdout[firstBracket..(lastBracket + 1)];

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        FindingDto[]? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<FindingDto[]>(json, options);
        }
        catch(JsonException)
        {
            return null;
        }

        if(dtos is null)
        {
            return null;
        }

        var findings = new List<Finding>();
        foreach(var dto in dtos)
        {
            if(!Enum.TryParse<FindingCategory>(dto.Category, ignoreCase: true, out var category))
            {
                Console.Error.WriteLine($"Warning: discarding finding with unrecognized category '{dto.Category}'");
                continue;
            }

            if(category != FindingCategory.Maintainable && string.IsNullOrWhiteSpace(dto.Note))
            {
                Console.Error.WriteLine($"Warning: discarding {category} finding with empty note");
                continue;
            }

            findings.Add(new Finding(
                category,
                dto.Note ?? string.Empty,
                dto.Targets ?? [],
                FindingStatus.Open));
        }

        findings = EnforceMaintainableExclusivity(findings);

        return findings;
    }

    private static List<Finding> EnforceMaintainableExclusivity(List<Finding> findings)
    {
        var hasMaintainable = false;
        var hasOther = false;

        foreach(var finding in findings)
        {
            if(finding.Category == FindingCategory.Maintainable)
            {
                hasMaintainable = true;
            }
            else
            {
                hasOther = true;
            }
        }

        if(hasMaintainable && hasOther)
        {
            Console.Error.WriteLine("Warning: discarding Maintainable finding because other categories are present");
            _ = findings.RemoveAll(f => f.Category == FindingCategory.Maintainable);
        }

        return findings;
    }

    private sealed class FindingDto
    {
        public string? Category { get; set; }
        public string? Note { get; set; }
        public IReadOnlyList<string>? Targets { get; set; }
    }
}