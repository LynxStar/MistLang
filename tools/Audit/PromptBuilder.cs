using System.Text;
using Audit.Models;

namespace Audit;

public static class PromptBuilder
{
    /// <summary>
    /// Builds the audit prompt including file content, metrics, category definitions, and split guidance.
    /// </summary>
    public static string BuildAuditPrompt(string filePath, string fileContent, FileMetrics metrics)
    {
        var sb = new StringBuilder();

        _ = sb.AppendLine("# Code Audit Request");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"## File: {filePath}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Metrics");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"- Lines: {metrics.LineCount}");
        _ = sb.AppendLine($"- Type declarations: {metrics.TypeCount}");
        _ = sb.AppendLine($"- Methods/constructors: {metrics.MethodCount}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## File Content");
        _ = sb.AppendLine();
        _ = sb.AppendLine("```csharp");
        _ = sb.AppendLine(fileContent);
        _ = sb.AppendLine("```");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Finding Categories");
        _ = sb.AppendLine();
        _ = sb.AppendLine("Assess this file against the following categories:");
        _ = sb.AppendLine();
        _ = sb.AppendLine("1. **Maintainable** - File is well-structured and needs no changes.");
        _ = sb.AppendLine("2. **Split** - Contains multiple types that should be in their own files.");
        _ = sb.AppendLine("3. **SRP** - Single Responsibility Principle violation: the class is doing too many unrelated things.");
        _ = sb.AppendLine("4. **Size** - File exceeds acceptable length (500-1000+ lines).");
        _ = sb.AppendLine("5. **Methods** - Individual methods are too large (bigger than a screen is a smell; many screens is a violation).");
        _ = sb.AppendLine("6. **Naming** - Names don't reflect behavior or are inconsistent, hindering navigation.");
        _ = sb.AppendLine("7. **Coupling** - Excessive dependencies or tight coupling that distracts from readability or makes refactoring significantly harder. High bar -- only the worst cases.");
        _ = sb.AppendLine("8. **DeadCode** - Unused methods, unreachable branches, or commented-out code that was never cleaned up.");
        _ = sb.AppendLine("9. **MisleadingDocs** - XML doc comments that are incorrect or misleading (not merely missing).");
        _ = sb.AppendLine("10. **Layout** - Poor internal organization. Expected layout: fields/properties, then constructors, then methods. Private methods should be grouped immediately after the public methods that use them.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Split Guidance");
        _ = sb.AppendLine();
        _ = sb.AppendLine("Apply these rules when considering the Split category:");
        _ = sb.AppendLine();
        _ = sb.AppendLine("- Helper types that are private or exclusively used by the file's primary class are OK to keep in the same file.");
        _ = sb.AppendLine("- Split is warranted when secondary types exceed approximately 50 lines and distract from the primary class's readability.");
        _ = sb.AppendLine("- Split is warranted when a file contains multiple public or internal classes that are referenced from outside the file.");
        _ = sb.AppendLine("- Split is warranted when model or data types used outside the file are defined alongside their consumer.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Instructions");
        _ = sb.AppendLine();
        _ = sb.AppendLine("Return your findings as a JSON array. Each element must have:");
        _ = sb.AppendLine();
        _ = sb.AppendLine("- `category` (string): One of the category names above (e.g., \"Maintainable\", \"Split\", \"SRP\", etc.).");
        _ = sb.AppendLine("- `note` (string): A description of the issue or observation.");
        _ = sb.AppendLine("- `targets` (string array): Specific methods, types, or identifiers involved. May be empty.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("If the file is well-structured, return a single Maintainable finding. Do not combine Maintainable with other categories.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Example Output");
        _ = sb.AppendLine();
        _ = sb.AppendLine("```json");
        _ = sb.AppendLine("[");
        _ = sb.AppendLine("  {");
        _ = sb.AppendLine("    \"category\": \"Size\",");
        _ = sb.AppendLine("    \"note\": \"File exceeds 800 lines. Consider extracting helper logic into focused classes.\",");
        _ = sb.AppendLine("    \"targets\": []");
        _ = sb.AppendLine("  },");
        _ = sb.AppendLine("  {");
        _ = sb.AppendLine("    \"category\": \"Methods\",");
        _ = sb.AppendLine("    \"note\": \"BuildExpression (120 lines) handles too many cases inline.\",");
        _ = sb.AppendLine("    \"targets\": [\"BuildExpression\"]");
        _ = sb.AppendLine("  }");
        _ = sb.AppendLine("]");
        _ = sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Builds the remediation prompt including file path, findings, and instructions.
    /// </summary>
    public static string BuildRemediationPrompt(string filePath, IReadOnlyList<Finding> findings)
    {
        var sb = new StringBuilder();

        _ = sb.AppendLine("# Code Remediation Request");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"## File: {filePath}");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Findings to Address");
        _ = sb.AppendLine();

        for(var i = 0; i < findings.Count; i++)
        {
            var finding = findings[i];
            _ = sb.AppendLine($"### Finding {i + 1}: {finding.Category}");
            _ = sb.AppendLine();
            _ = sb.AppendLine($"**Note:** {finding.Note}");

            if(finding.Targets.Count > 0)
            {
                _ = sb.AppendLine();
                _ = sb.AppendLine($"**Targets:** {string.Join(", ", finding.Targets)}");
            }

            _ = sb.AppendLine();
        }

        _ = sb.AppendLine("## Instructions");
        _ = sb.AppendLine();
        _ = sb.AppendLine("Apply code changes to the file above that resolve each finding listed. Make the minimal set of changes necessary to address the issues while maintaining correctness. Do not introduce new warnings or break existing functionality.");

        return sb.ToString();
    }
}