using System.Diagnostics;
using System.Text.Json;
using Audit.Models;

namespace Audit;

public sealed record AgentResult(bool Succeeded, IReadOnlyList<Finding>? Findings, string? ErrorMessage, decimal CreditsUsed = 0, bool Dismissed = false, string? Resolution = null);

public sealed partial class AgentDispatcher
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
        var credits = ExtractCredits(stderr);

        if(timedOut)
        {
            return new AgentResult(false, null, "Agent timed out", credits);
        }

        if(exitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(stderr)
                ? $"Agent exited with code {exitCode}"
                : stderr.Trim();
            return new AgentResult(false, null, errorMessage, credits);
        }

        var findings = ExtractFindings(stdout);
        if(findings is null)
        {
            var truncated = stdout.Length > 500 ? stdout[..500] + "..." : stdout;
            return new AgentResult(false, null, $"No JSON array found in agent output. Response:\n{truncated}", credits);
        }

        return new AgentResult(true, findings, null, credits);
    }

    /// <summary>
    /// Dispatches a remediation prompt for specific findings on a file.
    /// </summary>
    public AgentResult DispatchRemediation(string filePath, Finding finding)
    {
        var prompt = PromptBuilder.BuildRemediationPrompt(filePath, finding);
        var (exitCode, stdout, stderr, timedOut) = RunProcess(prompt);
        var credits = ExtractCredits(stderr);

        if(timedOut)
        {
            return new AgentResult(false, null, "Agent timed out", credits);
        }

        if(exitCode != 0)
        {
            var errorMessage = string.IsNullOrWhiteSpace(stderr)
                ? $"Agent exited with code {exitCode}"
                : stderr.Trim();
            return new AgentResult(false, null, errorMessage, credits);
        }

        var (dismissed, resolution) = ExtractResolution(stdout);
        return new AgentResult(true, null, null, credits, dismissed, resolution);
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

    private static (bool Dismissed, string? Resolution) ExtractResolution(string stdout)
    {
        var cleaned = StripAnsiCodes(stdout);
        foreach(var line in cleaned.Split('\n'))
        {
            var trimmed = line.Trim();
            if(trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..];
            }

            if(trimmed.StartsWith("RESULT:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed["RESULT:".Length..].Trim();
                if(value.StartsWith("DISMISSED:", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, value["DISMISSED:".Length..].Trim());
                }

                if(value.StartsWith("RESOLVED:", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, value["RESOLVED:".Length..].Trim());
                }

                return (false, value);
            }
        }

        return (false, null);
    }

    private static decimal ExtractCredits(string stdout)
    {
        var cleaned = StripAnsiCodes(stdout);
        foreach(var line in cleaned.Split('\n'))
        {
            var trimmed = line.Trim();
            // Look for "Credits: 0.06" pattern
            var creditsIndex = trimmed.IndexOf("Credits:", StringComparison.OrdinalIgnoreCase);
            if(creditsIndex < 0)
            {
                continue;
            }

            var afterCredits = trimmed[(creditsIndex + "Credits:".Length)..].Trim();
            // Take until next space or bullet
            var endIndex = afterCredits.IndexOfAny([' ', '\u2022', '\u25b8']);
            var valueStr = endIndex >= 0 ? afterCredits[..endIndex] : afterCredits;
            if(decimal.TryParse(valueStr, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var credits))
            {
                return credits;
            }
        }

        return 0;
    }

    private static IReadOnlyList<Finding>? ExtractFindings(string stdout)
    {
        // Strip ANSI escape codes from kiro-cli output
        var cleaned = StripAnsiCodes(stdout);

        // Strip kiro-cli output prefixes (lines starting with "> ")
        var lines = cleaned.Split('\n')
            .Select(line => line.StartsWith("> ", StringComparison.Ordinal) ? line[2..] : line);
        cleaned = string.Join('\n', lines);

        var firstBracket = cleaned.IndexOf('[');
        var lastBracket = cleaned.LastIndexOf(']');

        if(firstBracket < 0 || lastBracket < 0 || lastBracket <= firstBracket)
        {
            return null;
        }

        var json = cleaned[firstBracket..(lastBracket + 1)];

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

    private static string StripAnsiCodes(string input)
    {
        return AnsiEscapeRegex().Replace(input, string.Empty);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial System.Text.RegularExpressions.Regex AnsiEscapeRegex();
}