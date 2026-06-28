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
        _ = sb.AppendLine($"- Types: {metrics.Types.Count}");
        _ = sb.AppendLine();

        if(metrics.Types.Count > 0)
        {
            _ = sb.AppendLine("### Types in this file");
            _ = sb.AppendLine();
            _ = sb.AppendLine("| Type | Kind | Pure Data | Methods | External Dependents |");
            _ = sb.AppendLine("|------|------|-----------|---------|---------------------|");

            foreach(var type in metrics.Types)
            {
                var pureData = type.IsPureData ? "yes" : "no";
                _ = sb.AppendLine($"| {type.Name} | {type.Kind} | {pureData} | {type.MethodCount} | {type.ExternalDependents} |");
            }

            _ = sb.AppendLine();
        }

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
        _ = sb.AppendLine("- Helper types that are private or exclusively used by the file's primary class (External Dependents = 0) are OK to keep in the same file.");
        _ = sb.AppendLine("- Any type with External Dependents > 0 that is NOT the file's primary type should be split into its own file. Other files already depend on it directly, so it deserves independent discoverability.");
        _ = sb.AppendLine("- Split is especially warranted when secondary types exceed approximately 50 lines and distract from the primary class's readability.");
        _ = sb.AppendLine("- Split is warranted when a file contains multiple public or internal classes that are each referenced from outside the file.");
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
    /// Builds the remediation prompt for a single finding, including project context and build instructions.
    /// </summary>
    public static string BuildRemediationPrompt(string filePath, Finding finding)
    {
        var sb = new StringBuilder();

        _ = sb.AppendLine("# Code Remediation");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Project Context");
        _ = sb.AppendLine();
        _ = sb.AppendLine("- Solution: `Mist.slnx` (at the repository root)");
        _ = sb.AppendLine("- Build: `dotnet build Mist.slnx`");
        _ = sb.AppendLine("- Format check: `dotnet format --verify-no-changes Mist.slnx`");
        _ = sb.AppendLine("- Zero warnings policy: the build must produce no warnings after your changes");
        _ = sb.AppendLine("- Source projects: `Source/Mist/` (compiler core), `Source/Mist.CLI/` (CLI frontend), `Source/Storm.SourceGenerator/` (parser generator)");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Coding Style");
        _ = sb.AppendLine();
        _ = sb.AppendLine("- Always use braces for control flow (even single-line bodies)");
        _ = sb.AppendLine("- Private fields use plain camelCase (no `_` prefix)");
        _ = sb.AppendLine("- Use `var` when the type is apparent from the right-hand side");
        _ = sb.AppendLine("- Use block bodies for methods, not expression bodies");
        _ = sb.AppendLine("- Discard unused return values: `_ = sb.Append(...);`");
        _ = sb.AppendLine("- File-scoped namespaces");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"## File: `{filePath}`");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Finding to Resolve");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"**Category:** {finding.Category}");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"**Note:** {finding.Note}");

        if(finding.Targets.Count > 0)
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine($"**Targets:** {string.Join(", ", finding.Targets)}");
        }

        _ = sb.AppendLine();
        _ = sb.AppendLine("## Instructions");
        _ = sb.AppendLine();
        _ = sb.AppendLine("1. Read the file listed above.");
        _ = sb.AppendLine("2. Apply the minimal set of changes to resolve this specific finding.");
        _ = sb.AppendLine("3. If you need to create new files, place them in the same directory as the original.");
        _ = sb.AppendLine("4. Do not change any public API signatures (other files depend on them).");
        _ = sb.AppendLine("5. Build the solution (`dotnet build Mist.slnx`) and verify zero warnings.");
        _ = sb.AppendLine("6. Run `dotnet format --verify-no-changes Mist.slnx` and fix any violations.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("Focus only on this one finding. Do not address other code quality issues you may notice.");
        _ = sb.AppendLine();
        _ = sb.AppendLine("## Required Output");
        _ = sb.AppendLine();
        _ = sb.AppendLine("When finished, print exactly one line starting with `RESULT:` followed by one of:");
        _ = sb.AppendLine();
        _ = sb.AppendLine("- `RESULT: RESOLVED: <brief summary of what you changed>`");
        _ = sb.AppendLine("- `RESULT: DISMISSED: <why this finding is invalid or does not apply>`");
        _ = sb.AppendLine();
        _ = sb.AppendLine("Use DISMISSED if the finding is incorrect (e.g., the code it flags is actually used, the issue doesn't exist). Use RESOLVED if you made changes to fix it.");

        return sb.ToString();
    }
}