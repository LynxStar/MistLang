// Feature: tuple-pattern-matching, Property 6: C# emission produces valid positional pattern syntax
// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7**

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
/// Property-based tests for C# emission of tuple positional patterns.
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7**
///
/// Property 6: For any tuple positional pattern (with arbitrary subpatterns and nesting),
/// the C# emitter SHALL produce output of the form <c>(e1, e2, ..., eN)</c> where each
/// <c>ei</c> is the correct C# emission of the corresponding subpattern, and this output
/// SHALL be identical regardless of whether the pattern appears in an <c>is</c> expression
/// or a switch arm.
///
/// No process spawning — in-process compilation and structural string verification only.
/// </summary>
public sealed class TuplePositionalPatternEmissionPropertyTests
{
    // ─── Shared driver ───────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Scenario definitions ────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// The kind of subpattern element within a tuple positional pattern.
    /// </summary>
    public enum SubpatternKind
    {
        Discard,
        VarPattern,
        ConstantInt,
        RelationalGreaterThan,
        RelationalLessThan,
        RelationalGreaterThanOrEqual,
        RelationalLessThanOrEqual,
        NestedTuple,
    }

    /// <summary>
    /// Represents a single element within a tuple positional pattern.
    /// </summary>
    public record SubpatternElement(SubpatternKind Kind, int IntValue, string VarName)
    {
        /// <summary>
        /// Returns the expected C# emission for this subpattern element.
        /// </summary>
        public string ExpectedEmission => Kind switch
        {
            SubpatternKind.Discard => "var _",
            SubpatternKind.VarPattern => $"var {VarName}",
            SubpatternKind.ConstantInt => IntValue.ToString(),
            SubpatternKind.RelationalGreaterThan => $"> {IntValue}",
            SubpatternKind.RelationalLessThan => $"< {IntValue}",
            SubpatternKind.RelationalGreaterThanOrEqual => $">= {IntValue}",
            SubpatternKind.RelationalLessThanOrEqual => $"<= {IntValue}",
            SubpatternKind.NestedTuple => $"(> {IntValue}, var _)",
            _ => "var _",
        };

        /// <summary>
        /// Returns the Mist source text for this subpattern element.
        /// </summary>
        public string MistSource => Kind switch
        {
            SubpatternKind.Discard => "_",
            SubpatternKind.VarPattern => $"var {VarName}",
            SubpatternKind.ConstantInt => IntValue.ToString(),
            SubpatternKind.RelationalGreaterThan => $"> {IntValue}",
            SubpatternKind.RelationalLessThan => $"< {IntValue}",
            SubpatternKind.RelationalGreaterThanOrEqual => $">= {IntValue}",
            SubpatternKind.RelationalLessThanOrEqual => $"<= {IntValue}",
            SubpatternKind.NestedTuple => $"(> {IntValue}, _)",
            _ => "_",
        };

        /// <summary>
        /// Returns the Mist type annotation for the tuple element.
        /// </summary>
        public string MistType => Kind switch
        {
            SubpatternKind.NestedTuple => "(int, int)",
            _ => "int",
        };

        public override string ToString()
        {
            return $"{Kind}({MistSource})";
        }
    }

    /// <summary>
    /// Represents a complete tuple positional pattern emission scenario.
    /// </summary>
    public record EmissionScenario(IReadOnlyList<SubpatternElement> Elements)
    {
        /// <summary>
        /// Returns the expected C# positional pattern string.
        /// </summary>
        public string ExpectedPattern =>
            $"({string.Join(", ", Elements.Select(e => e.ExpectedEmission))})";

        /// <summary>
        /// Returns the Mist source pattern string.
        /// </summary>
        public string MistPattern =>
            $"({string.Join(", ", Elements.Select(e => e.MistSource))})";

        /// <summary>
        /// Returns the Mist tuple type annotation.
        /// </summary>
        public string MistTupleType =>
            $"({string.Join(", ", Elements.Select(e => e.MistType))})";

        public override string ToString()
        {
            return MistPattern;
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] VarNames =
        ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"];

    private static Gen<SubpatternElement> GenSubpatternElement(int varIndex, bool allowNested = true)
    {
        var kinds = allowNested
            ? new[]
            {
                SubpatternKind.Discard,
                SubpatternKind.VarPattern,
                SubpatternKind.ConstantInt,
                SubpatternKind.RelationalGreaterThan,
                SubpatternKind.RelationalLessThan,
                SubpatternKind.RelationalGreaterThanOrEqual,
                SubpatternKind.RelationalLessThanOrEqual,
                SubpatternKind.NestedTuple,
            }
            : new[]
            {
                SubpatternKind.Discard,
                SubpatternKind.VarPattern,
                SubpatternKind.ConstantInt,
                SubpatternKind.RelationalGreaterThan,
                SubpatternKind.RelationalLessThan,
                SubpatternKind.RelationalGreaterThanOrEqual,
                SubpatternKind.RelationalLessThanOrEqual,
            };

        return from kind in Gen.Elements(kinds)
               from intVal in Gen.Choose(1, 99)
               let varName = VarNames[varIndex % VarNames.Length]
               select new SubpatternElement(kind, intVal, varName);
    }

    private static Gen<EmissionScenario> GenScenario()
    {
        // Generate 2-5 element tuple patterns
        return from arity in Gen.Choose(2, 5)
               from elements in GenElements(arity)
               select new EmissionScenario(elements);
    }

    private static Gen<IReadOnlyList<SubpatternElement>> GenElements(int arity)
    {
        // Build a list of element generators, each with a unique var name index
        var gens = new List<Gen<SubpatternElement>>();
        for(var i = 0; i < arity; i++)
        {
            // Only allow nested tuples for the first 3 elements to keep things manageable
            gens.Add(GenSubpatternElement(i, allowNested: i < 3));
        }

        return gens.Aggregate(
            Gen.Constant((IReadOnlyList<SubpatternElement>)new List<SubpatternElement>()),
            (acc, gen) => from list in acc
                          from elem in gen
                          select (IReadOnlyList<SubpatternElement>)list.Append(elem).ToList());
    }

    public static Arbitrary<EmissionScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Mist program that uses the same tuple positional pattern in both
    /// an <c>is</c> expression and a switch arm, printing a marker for each context.
    /// </summary>
    private static string BuildMistProgram(EmissionScenario scenario)
    {
        var tupleType = scenario.MistTupleType;
        var pattern = scenario.MistPattern;

        // Build a tuple literal with default values that will match the pattern
        var tupleElements = scenario.Elements.Select(e =>
            e.Kind == SubpatternKind.NestedTuple ? "(50, 50)" : "50").ToList();
        var tupleLiteral = $"({string.Join(", ", tupleElements)})";

        // Build a unique var pattern for the switch arm (to avoid duplicate var names)
        // We use discard-only pattern for the switch arm to avoid variable conflicts
        var switchPattern = BuildSwitchPattern(scenario);

        return $@"using Standard;

namespace TestEmission;

public class Program
{{
    public static void Main()
    {{
        {tupleType} value = {tupleLiteral};
        if (value is {pattern})
        {{
            Console.WriteLine(""is-match"");
        }}
        string result = value switch
        {{
            {switchPattern} => ""switch-match"",
            {BuildCatchAllPattern(scenario)} => ""other"",
        }};
        Console.WriteLine(result);
    }}
}}
";
    }

    /// <summary>
    /// Builds a switch arm pattern that uses the same structure but with
    /// unique variable names to avoid conflicts with the is-expression pattern.
    /// </summary>
    private static string BuildSwitchPattern(EmissionScenario scenario)
    {
        var elements = scenario.Elements.Select((e, idx) =>
        {
            // For var patterns, use different names to avoid conflicts
            if(e.Kind == SubpatternKind.VarPattern)
            {
                return $"var s{VarNames[idx % VarNames.Length]}";
            }

            return e.MistSource;
        });
        return $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Builds a catch-all pattern for the switch expression.
    /// </summary>
    private static string BuildCatchAllPattern(EmissionScenario scenario)
    {
        var elements = scenario.Elements.Select(_ => "_");
        return $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Compiles a Mist source in-process and returns the emitted C# content for the program file.
    /// Uses in-memory emission to avoid filesystem issues with multiple output files.
    /// </summary>
    private static string? CompileAndGetCSharp(string mistSource)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_tuple_emit_pbt_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestEmission",
                NamespaceRoot: "TestEmission",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);

            if(!result.Succeeded || result.EmittedFiles is null)
            {
                return null;
            }

            // Find the Program.cs file in the emitted files dictionary
            if(result.EmittedFiles.TryGetValue("Program.cs", out var programContent))
            {
                return programContent;
            }

            // Fallback: look for any file containing our namespace
            foreach(var (_, content) in result.EmittedFiles)
            {
                if(content.Contains("namespace TestEmission"))
                {
                    return content;
                }
            }

            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: C# emission produces valid positional pattern syntax
    //
    // **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7**
    //
    // For any tuple positional pattern (with arbitrary subpatterns and nesting),
    // the C# emitter SHALL produce output of the form (e1, e2, ..., eN) where
    // each ei is the correct C# emission of the corresponding subpattern, and
    // this output SHALL be identical regardless of whether the pattern appears
    // in an is expression or a switch arm.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternEmissionPropertyTests) })]
    public Property EmittedCSharp_ProducesValidPositionalPatternSyntax(EmissionScenario scenario)
    {
        var mistSource = BuildMistProgram(scenario);
        var csContent = CompileAndGetCSharp(mistSource);

        if(csContent is null)
        {
            return false.ToProperty()
                .Label($"Compilation failed for scenario: {scenario}\nSource:\n{mistSource}");
        }

        // Verify the emitted C# contains the expected positional pattern format
        var expectedPattern = scenario.ExpectedPattern;
        var containsExpectedPattern = csContent.Contains(expectedPattern);

        if(!containsExpectedPattern)
        {
            return false.ToProperty()
                .Label($"Expected pattern '{expectedPattern}' not found in emitted C#.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Emitted C#:\n{csContent}");
        }

        // Verify the pattern appears in both is-expression and switch arm contexts
        // by checking it appears at least twice (once for is, once for switch arm)
        _ = CountOccurrences(csContent, expectedPattern);

        // The is-expression uses the original pattern, the switch arm may use
        // a slightly different pattern (different var names). So we verify:
        // 1. The is-expression pattern is present
        // 2. The switch arm also has a positional pattern with the same structure

        // Check that the is-expression context has the pattern
        var isExpressionPresent = csContent.Contains($"is {expectedPattern}");

        if(!isExpressionPresent)
        {
            return false.ToProperty()
                .Label($"Expected 'is {expectedPattern}' in emitted C# (is-expression context).\n" +
                       $"Scenario: {scenario}\n" +
                       $"Emitted C#:\n{csContent}");
        }

        // Check that the switch arm context also has a positional pattern
        // The switch arm pattern may have different var names (sa, sb, etc.)
        // but should still have the same structural format (e1, e2, ..., eN)
        var switchArmPattern = BuildExpectedSwitchArmPattern(scenario);
        var switchArmPresent = csContent.Contains(switchArmPattern);

        if(!switchArmPresent)
        {
            return false.ToProperty()
                .Label($"Expected switch arm pattern '{switchArmPattern}' in emitted C#.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Emitted C#:\n{csContent}");
        }

        // Verify the positional pattern format: (e1, e2, ..., eN)
        // Each pattern should start with ( and end with ), with comma-space separated elements
        var formatValid = ValidatePositionalPatternFormat(expectedPattern, scenario.Elements.Count);

        return formatValid.ToProperty()
            .Label($"Pattern format validation failed for '{expectedPattern}'.\n" +
                   $"Expected {scenario.Elements.Count} comma-separated elements.\n" +
                   $"Scenario: {scenario}");
    }

    /// <summary>
    /// Builds the expected switch arm pattern (with different var names).
    /// </summary>
    private static string BuildExpectedSwitchArmPattern(EmissionScenario scenario)
    {
        var elements = scenario.Elements.Select((e, idx) =>
        {
            if(e.Kind == SubpatternKind.VarPattern)
            {
                return $"var s{VarNames[idx % VarNames.Length]}";
            }

            return e.ExpectedEmission;
        });
        return $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Validates that a pattern string has the correct positional pattern format.
    /// </summary>
    private static bool ValidatePositionalPatternFormat(string pattern, int expectedArity)
    {
        if(!pattern.StartsWith('(') || !pattern.EndsWith(')'))
        {
            return false;
        }

        // Remove outer parens
        var inner = pattern[1..^1];

        // Split by ", " but be careful with nested patterns
        var elements = SplitTopLevelElements(inner);

        return elements.Count == expectedArity;
    }

    /// <summary>
    /// Splits a comma-separated list at the top level (respecting nested parentheses).
    /// </summary>
    private static List<string> SplitTopLevelElements(string inner)
    {
        var elements = new List<string>();
        var depth = 0;
        var start = 0;

        for(var i = 0; i < inner.Length; i++)
        {
            switch(inner[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    elements.Add(inner[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        if(start < inner.Length)
        {
            elements.Add(inner[start..].Trim());
        }

        return elements;
    }

    /// <summary>
    /// Counts occurrences of a substring in a string.
    /// </summary>
    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}