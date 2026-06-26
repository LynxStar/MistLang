// Feature: global-usings, Property 8: Emitter deduplication and sorting
//
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that the C# emitter correctly deduplicates and sorts
/// using directives from all sources: global usings, explicit usings, and backend namespaces.
///
/// Property 8: For any compilation unit with global usings, explicit using declarations,
/// and backend namespaces, the emitted C# file SHALL contain each namespace as a using
/// directive exactly once, and the directives SHALL be sorted in ordinal alphabetical order.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
/// </summary>
public sealed class GlobalUsingsEmitterDeduplicationPropertyTests : IDisposable
{
    // ─── Shared driver (reused across all iterations) ────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public GlobalUsingsEmitterDeduplicationPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistGlobalUsingsDedup_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// A fixed pool of namespace segments that are guaranteed to NOT collide with
    /// any Mist keyword (if, else, for, int, bool, class, async, await, etc.).
    /// Using a fixed pool eliminates flakiness from random generation hitting keywords.
    /// </summary>
    private static readonly string[] SafeSegments =
    [
        "Alpha", "Bravo", "Charlie", "Delta", "Echo",
        "Foxtrot", "Golf", "Hotel", "India", "Juliet",
        "Kilo", "Lima", "Mike", "November", "Oscar",
        "Papa", "Quebec", "Romeo", "Sierra", "Tango",
        "Uniform", "Victor", "Whiskey", "Xray", "Yankee", "Zulu",
        "Mercury", "Venus", "Earth", "Mars", "Jupiter",
        "Saturn", "Uranus", "Neptune", "Pluto", "Ceres",
    ];

    /// <summary>
    /// Generates a valid namespace: 1-3 dot-separated segments from the safe pool.
    /// </summary>
    private static Gen<string> GenNamespace()
    {
        return Gen.Choose(1, 3).SelectMany(segCount =>
            Gen.ArrayOf(Gen.Elements(SafeSegments), segCount)
                .Select(segs => string.Join(".", segs)));
    }

    /// <summary>
    /// Represents a test scenario with global usings, explicit usings, and overlapping namespaces.
    /// </summary>
#pragma warning disable CA1034 // nested type is the FsCheck-friendly way to surface a scenario record
    public record EmitterDeduplicationScenario(
        ImmutableArray<string> GlobalUsings,
        ImmutableArray<string> ExplicitUsings,
        ImmutableArray<string> OverlappingNamespaces)
    {
        public override string ToString()
        {
            return $"Global=[{string.Join(", ", GlobalUsings)}], " +
            $"Explicit=[{string.Join(", ", ExplicitUsings)}], " +
            $"Overlapping=[{string.Join(", ", OverlappingNamespaces)}]";
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates a scenario with:
    /// - 1-4 unique global-only namespaces
    /// - 1-4 unique explicit-only namespaces
    /// - 0-2 overlapping namespaces (present in both global and explicit)
    /// </summary>
    public static Arbitrary<EmitterDeduplicationScenario> ArbitraryScenarios()
    {
        var gen =
            from globalCount in Gen.Choose(1, 4)
            from explicitCount in Gen.Choose(1, 4)
            from overlapCount in Gen.Choose(0, 2)
            from allNamespaces in Gen.ArrayOf(GenNamespace(), globalCount + explicitCount + overlapCount)
            select BuildScenario(allNamespaces, globalCount, explicitCount, overlapCount);

        return gen.ToArbitrary();
    }

    private static EmitterDeduplicationScenario BuildScenario(
        string[] namespaces, int globalCount, int explicitCount, int overlapCount)
    {
        // Deduplicate the generated namespaces to ensure we have distinct values
        var distinct = namespaces.Distinct(StringComparer.Ordinal).ToArray();

        // If we don't have enough distinct namespaces, pad with indexed suffixes
        var needed = globalCount + explicitCount + overlapCount;
        if(distinct.Length < needed)
        {
            var padded = new List<string>(distinct);
            var idx = 0;
            while(padded.Count < needed)
            {
                padded.Add($"Ns{idx}_Pad");
                idx++;
            }
            distinct = padded.ToArray();
        }

        var globalOnly = distinct.Take(globalCount).ToImmutableArray();
        var explicitOnly = distinct.Skip(globalCount).Take(explicitCount).ToImmutableArray();
        var overlapping = distinct.Skip(globalCount + explicitCount).Take(overlapCount).ToImmutableArray();

        return new EmitterDeduplicationScenario(globalOnly, explicitOnly, overlapping);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// Builds a Mist source file that defines namespaces (so explicit usings resolve)
    /// and uses explicit using declarations.
    /// </summary>
    private static string BuildSource(
        ImmutableArray<string> explicitUsings,
        ImmutableArray<string> overlappingUsings)
    {
        var sb = new System.Text.StringBuilder();

        // Emit explicit usings FIRST (before namespace declarations)
        var allExplicitNamespaces = explicitUsings.AddRange(overlappingUsings);
        foreach(var ns in allExplicitNamespaces)
        {
            _ = sb.AppendLine($"using {ns};");
        }

        if(allExplicitNamespaces.Length > 0)
        {
            _ = sb.AppendLine();
        }

        // Define namespaces so that explicit usings resolve during semantic analysis.
        // Each namespace gets a simple class so it's not empty.
        foreach(var ns in allExplicitNamespaces)
        {
            // Use a unique class name based on the namespace to avoid conflicts
            var className = "Marker_" + ns.Replace(".", "_");
            _ = sb.AppendLine($"namespace {ns} {{");
            _ = sb.AppendLine($"    class {className} {{ }}");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine();
        }

        // Simple class with Main so compilation succeeds
        _ = sb.AppendLine("class Program {");
        _ = sb.AppendLine("    public static void Main() {");
        _ = sb.AppendLine("        var x = 42;");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Extracts using directives from emitted C# content.
    /// Returns the namespace portion of each "using X.Y.Z;" line.
    /// </summary>
    private static List<string> ExtractUsingDirectives(string csContent)
    {
        return csContent
            .Split('\n')
            .Where(line => line.TrimStart().StartsWith("using ", StringComparison.Ordinal)
                        && line.TrimEnd().EndsWith(";", StringComparison.Ordinal))
            .Select(line => line.Trim().TrimEnd(';').Substring("using ".Length).Trim())
            .ToList();
    }

    // ─── Property 8: Emitter deduplication and sorting ──────────────────
    // Feature: global-usings, Property 8: Emitter deduplication and sorting

    /// <summary>
    /// Property 8: For any compilation unit with global usings, explicit using declarations,
    /// and backend namespaces, the emitted C# file SHALL contain each namespace as a using
    /// directive exactly once, and the directives SHALL be sorted in ordinal alphabetical order.
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(GlobalUsingsEmitterDeduplicationPropertyTests) }, MaxTest = 100)]
    public Property P8_EmittedUsings_AreDeduplicated_AndSorted(EmitterDeduplicationScenario scenario)
    {
        CleanOutputDir();

        // Build global usings: global-only + overlapping
        var allGlobalUsings = scenario.GlobalUsings.AddRange(scenario.OverlappingNamespaces);

        // Build source with explicit usings (explicit-only + overlapping)
        var source = BuildSource(scenario.ExplicitUsings, scenario.OverlappingNamespaces);

        var filePath = Path.Combine(outputDir, "Test.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(outputDir, "output"),
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray.Create("__none__"),
            EmitToDisk: false,
            GlobalUsings: allGlobalUsings);

        var result = SharedDriver.Compile(options);

        // Compilation must succeed for the property to be meaningful
        if(!result.Succeeded)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
                .Select(d => d.Message));
            return false.ToProperty()
                .Label($"Compilation failed: {errors}");
        }

        // Read the emitted C# content from in-memory results
        if(result.EmittedFiles is null || result.EmittedFiles.Count == 0)
        {
            return false.ToProperty()
                .Label("No emitted files produced");
        }

        // Find the user file (Test.cs)
        var userFileEntry = result.EmittedFiles
            .FirstOrDefault(kvp => kvp.Key.Equals("Test.cs", StringComparison.OrdinalIgnoreCase));

        if(userFileEntry.Value is null)
        {
            return false.ToProperty()
                .Label($"No Test.cs file found in emitted files. Available: [{string.Join(", ", result.EmittedFiles.Keys)}]");
        }

        var csContent = userFileEntry.Value;
        var usings = ExtractUsingDirectives(csContent);

        // Property assertion 1: No duplicates
        var distinctUsings = usings.Distinct(StringComparer.Ordinal).ToList();
        if(usings.Count != distinctUsings.Count)
        {
            var duplicates = usings.GroupBy(u => u, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            return false.ToProperty()
                .Label($"Duplicate using directives found: [{string.Join(", ", duplicates)}]");
        }

        // Property assertion 2: Sorted in ordinal alphabetical order
        var sorted = usings.OrderBy(u => u, StringComparer.Ordinal).ToList();
        for(var i = 0; i < usings.Count; i++)
        {
            if(usings[i] != sorted[i])
            {
                return false.ToProperty()
                    .Label($"Usings not sorted at index {i}: got '{usings[i]}' but expected '{sorted[i]}'. " +
                           $"Full list: [{string.Join(", ", usings)}]");
            }
        }

        // Property assertion 3: All global usings are present
        foreach(var globalNs in allGlobalUsings)
        {
            if(!usings.Contains(globalNs))
            {
                return false.ToProperty()
                    .Label($"Global using '{globalNs}' not found in emitted directives: [{string.Join(", ", usings)}]");
            }
        }

        // Property assertion 4: All explicit usings are present
        foreach(var explicitNs in scenario.ExplicitUsings)
        {
            if(!usings.Contains(explicitNs))
            {
                return false.ToProperty()
                    .Label($"Explicit using '{explicitNs}' not found in emitted directives: [{string.Join(", ", usings)}]");
            }
        }

        // Property assertion 5: Overlapping namespaces appear exactly once
        foreach(var overlapNs in scenario.OverlappingNamespaces)
        {
            var count = usings.Count(u => string.Equals(u, overlapNs, StringComparison.Ordinal));
            if(count != 1)
            {
                return false.ToProperty()
                    .Label($"Overlapping namespace '{overlapNs}' appears {count} times (expected 1)");
            }
        }

        return true.ToProperty();
    }
}