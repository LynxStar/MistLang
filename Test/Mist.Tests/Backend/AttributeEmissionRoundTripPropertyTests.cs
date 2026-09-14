// Feature: attributes-and-test-command, Property 1: Attribute Emission Round-Trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 3.1, 3.2, 3.3, 3.4, 3.6, 3.7, 3.8**
//
// For any valid Mist program containing declarations annotated with attributes
// (varying attribute count, argument types, named/positional arguments, type arguments,
// and qualified names), compiling and inspecting the emitted C# output SHALL produce
// attribute annotations that preserve:
// - The qualified attribute name
// - The number and order of positional arguments
// - Named arguments with correct names
// - Type argument lists (if present)
// - The grouping (single-list comma-separated vs. multi-list on separate lines)

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for attribute emission round-trip.
///
/// Property 1: Attribute Emission Round-Trip
/// Generates random attribute configurations, compiles in-memory using a shared
/// CompilerDriver instance, and verifies the emitted C# preserves the attribute
/// structure (name, positional args, named args, type args, grouping).
///
/// Uses batched compilation: one compile per test run (Gen.Sample generates 100+
/// scenarios compiled in separate iterations via the shared driver, no process spawning).
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 3.1, 3.2, 3.3, 3.4, 3.6, 3.7, 3.8**
/// </summary>
public sealed class AttributeEmissionRoundTripPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across all iterations) ────────────────────
    // No analyzers needed — we're testing emission, not type checking.
    // This avoids MIST_TC_0017 (entry point not found) on library-mode programs.

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray<Mist.Analysis.Contracts.IAnalyzer>.Empty,
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public AttributeEmissionRoundTripPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_attr_rt_" + Guid.NewGuid().ToString("N"));
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

    // ═══════════════════════════════════════════════════════════════════════
    // Test input model
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a single attribute argument (positional or named).
    /// </summary>
#pragma warning disable CA1034
    public record AttrArg(string? Name, string Value, string ExpectedEmitted)
    {
        public override string ToString()
        {
            return Name is not null ? $"{Name} = {Value}" : Value;
        }
    }

    /// <summary>
    /// Represents a single attribute with its name, type args, and arguments.
    /// </summary>
    public record AttrConfig(
        string QualifiedName,
        string? TypeArgs,
        IReadOnlyList<AttrArg> Args)
    {
        public override string ToString()
        {
            var sb = new StringBuilder(QualifiedName);
            if(TypeArgs is not null)
            {
                _ = sb.Append($"<{TypeArgs}>");
            }

            if(Args.Count > 0)
            {
                _ = sb.Append('(');
                _ = sb.Append(string.Join(", ", Args));
                _ = sb.Append(')');
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents an attribute list (one bracket pair with 1+ attributes).
    /// </summary>
    public record AttrList(IReadOnlyList<AttrConfig> Attributes)
    {
        public override string ToString()
        {
            return $"[{string.Join(", ", Attributes)}]";
        }
    }

    /// <summary>
    /// Represents a full scenario: multiple attribute lists on a declaration.
    /// </summary>
    public record AttributeScenario(
        IReadOnlyList<AttrList> Lists,
        DeclarationTarget Target)
    {
        public override string ToString()
        {
            var listsStr = string.Join(" ", Lists);
            return $"{listsStr} on {Target}";
        }
    }

    /// <summary>
    /// What kind of declaration the attributes go on.
    /// </summary>
    public enum DeclarationTarget
    {
        Class,
        Method,
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] SimpleNames =
        ["Serializable", "Obsolete", "Fact", "Theory", "Range", "Display", "Required"];

    private static readonly string[] QualifiedNames =
        ["System.Serializable", "System.ComponentModel.Display", "Xunit.Fact"];

    private static readonly string[] TypeArgOptions =
        ["int", "string", "bool", "MyConverter", "List<int>"];

    /// <summary>
    /// Generates a simple or qualified attribute name.
    /// </summary>
    private static Gen<string> GenAttrName()
    {
        return Gen.Frequency(
            (3, Gen.Elements(SimpleNames)),
            (1, Gen.Elements(QualifiedNames)));
    }

    /// <summary>
    /// Generates an optional type argument list for an attribute.
    /// </summary>
    private static Gen<string?> GenTypeArgs()
    {
        return Gen.Frequency(
            (3, Gen.Constant<string?>(null)),
            (1, Gen.Elements(TypeArgOptions).Select(t => (string?)t)));
    }

    /// <summary>
    /// Generates a positional attribute argument (string, int, or bool literal).
    /// </summary>
    private static Gen<AttrArg> GenPositionalArg()
    {
        var stringArg = Gen.Elements("hello", "world", "test", "reason", "category")
            .Select(s => new AttrArg(null, $"\"{s}\"", $"\"{s}\""));

        var intArg = Gen.Choose(0, 999)
            .Select(i => new AttrArg(null, i.ToString(), i.ToString()));

        var boolArg = Gen.Elements(true, false)
            .Select(b => new AttrArg(null, b ? "true" : "false", b ? "true" : "false"));

        return Gen.Frequency(
            (2, stringArg),
            (2, intArg),
            (1, boolArg));
    }

    /// <summary>
    /// Generates a named attribute argument.
    /// </summary>
    private static Gen<(string Source, string Expected)> GenNamedArgValue()
    {
        var stringValue = Gen.Elements("reason", "test", "category")
            .Select(s => ($"\"{s}\"", $"\"{s}\""));

        var intValue = Gen.Choose(0, 100)
            .Select(i => (i.ToString(), i.ToString()));

        var boolValue = Gen.Elements(true, false)
            .Select(b => (b ? "true" : "false", b ? "true" : "false"));

        return Gen.Frequency(
            (2, stringValue),
            (2, intValue),
            (1, boolValue));
    }

    /// <summary>
    /// The properties a generated named argument may assign to. Fewer names than
    /// <c>NamedArgLimit</c> would make a distinct selection impossible.
    /// </summary>
    private static readonly string[] NamedArgNames = ["Skip", "Name", "Description", "Order", "Timeout"];

    /// <summary>
    /// The most named arguments one generated attribute may carry.
    /// </summary>
    private const int NamedArgLimit = 3;

    /// <summary>
    /// Generates <paramref name="count"/> named arguments with DISTINCT names.
    /// <para>
    /// Distinctness is not incidental. Assigning the same property twice is not a valid attribute,
    /// so generating one would measure the emitter against input the language does not admit -- and
    /// drawing names independently collides often, since three draws from five names collide about
    /// half the time.
    /// </para>
    /// </summary>
    private static Gen<List<AttrArg>> GenNamedArgs(int count)
    {
        return from swaps in Gen.ArrayOf(Gen.Choose(0, NamedArgNames.Length - 1), NamedArgNames.Length)
               from values in Gen.ArrayOf(GenNamedArgValue(), count)
               select ChooseNames(swaps, count)
                   .Zip(values, (name, value) => new AttrArg(name, value.Source, value.Expected))
                   .ToList();
    }

    /// <summary>
    /// A distinct selection of <paramref name="count"/> names, shuffled by the generated
    /// <paramref name="swaps"/> so which names appear varies with the seed rather than always being
    /// the first few.
    /// </summary>
    private static IEnumerable<string> ChooseNames(int[] swaps, int count)
    {
        var pool = (string[])NamedArgNames.Clone();

        for(var i = pool.Length - 1; i > 0; i--)
        {
            var j = swaps[i] % (i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.Take(count);
    }

    /// <summary>
    /// Generates a single attribute configuration.
    /// </summary>
    private static Gen<AttrConfig> GenAttribute()
    {
        return from name in GenAttrName()
               from typeArgs in GenTypeArgs()
               from positionalCount in Gen.Choose(0, 5)
               from namedCount in Gen.Choose(0, NamedArgLimit)
               from positionalArgs in Gen.ArrayOf(GenPositionalArg(), positionalCount)
               from namedArgs in GenNamedArgs(namedCount)
               let allArgs = positionalArgs.Concat(namedArgs).ToList()
               select new AttrConfig(name, typeArgs, allArgs);
    }

    /// <summary>
    /// Generates an attribute list (1-4 attributes in a single bracket pair).
    /// </summary>
    private static Gen<AttrList> GenAttrList()
    {
        return from count in Gen.Choose(1, 4)
               from attrs in Gen.ArrayOf(GenAttribute(), count)
               select new AttrList(attrs);
    }

    /// <summary>
    /// Generates a full attribute scenario: 1-3 attribute lists on a declaration.
    /// </summary>
    private static Gen<AttributeScenario> GenScenario()
    {
        return from listCount in Gen.Choose(1, 3)
               from lists in Gen.ArrayOf(GenAttrList(), listCount)
               from target in Gen.Elements(DeclarationTarget.Class, DeclarationTarget.Method)
               select new AttributeScenario(lists, target);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mist source builders
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a Mist source fragment for a single attribute.
    /// </summary>
    private static string BuildAttrSource(AttrConfig attr)
    {
        var sb = new StringBuilder(attr.QualifiedName);
        if(attr.TypeArgs is not null)
        {
            _ = sb.Append($"<{attr.TypeArgs}>");
        }

        if(attr.Args.Count > 0)
        {
            _ = sb.Append('(');
            var parts = new List<string>();
            foreach(var arg in attr.Args)
            {
                if(arg.Name is not null)
                {
                    parts.Add($"{arg.Name} = {arg.Value}");
                }
                else
                {
                    parts.Add(arg.Value);
                }
            }

            _ = sb.Append(string.Join(", ", parts));
            _ = sb.Append(')');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a full Mist program with the given attribute scenario.
    /// </summary>
    private static string BuildMistProgram(AttributeScenario scenario, int index)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"namespace TestNs{index};");
        _ = sb.AppendLine();

        // Build the attribute annotation lines
        foreach(var list in scenario.Lists)
        {
            var attrParts = list.Attributes.Select(BuildAttrSource);
            _ = sb.AppendLine($"[{string.Join(", ", attrParts)}]");
        }

        // Build the declaration
        if(scenario.Target == DeclarationTarget.Class)
        {
            _ = sb.AppendLine($"class TestClass{index}");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("}");
        }
        else
        {
            _ = sb.AppendLine($"class Container{index}");
            _ = sb.AppendLine("{");

            // Re-emit attributes for the method
            foreach(var list in scenario.Lists)
            {
                var attrParts = list.Attributes.Select(BuildAttrSource);
                _ = sb.AppendLine($"    [{string.Join(", ", attrParts)}]");
            }

            _ = sb.AppendLine($"    public void TestMethod{index}()");
            _ = sb.AppendLine("    {");
            _ = sb.AppendLine("    }");
            _ = sb.AppendLine("}");
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Compilation helper
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compiles a Mist source in-process via the shared CompilerDriver and returns
    /// the emitted C# source file content. Since we're testing emission (not Roslyn
    /// validity), we read the .cs files even if Roslyn compilation fails.
    /// Only front-end errors (lex/parse/semantic) cause failure.
    /// </summary>
    private (string? CSharpOutput, string? Error) CompileAndGetOutput(string mistSource, int index)
    {
        var iterDir = Path.Combine(outputDir, $"iter_{index}");
        _ = Directory.CreateDirectory(iterDir);

        var sourcePath = Path.Combine(iterDir, "Program.mist");
        File.WriteAllText(sourcePath, mistSource);

        var options = new CompilerOptions(
            ProjectFolder: iterDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(iterDir, "out"),
            BinDirectory: Path.Combine(iterDir, "bin"),
            ProjectName: $"TestNs{index}",
            NamespaceRoot: $"TestNs{index}",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            OutputType: ProjectOutputType.Library,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);

        // Check for front-end errors only. With EmitToDisk:false the emitter
        // produces the C# in memory (result.EmittedFiles) and the downstream
        // Roslyn build never runs, so MIST_CS_ERROR cannot appear — but the
        // filter is kept for parity with the on-disk path.
        var frontendErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_CS_ERROR")
            .ToList();

        if(frontendErrors.Count > 0)
        {
            var diagnosticText = string.Join("\n",
                frontendErrors.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"));
            return (null, $"Front-end compilation failed:\n{diagnosticText}");
        }

        // Inspect the in-memory emitted C# instead of reading it back from disk.
        var emittedFiles = result.EmittedFiles;
        if(emittedFiles is null || emittedFiles.Count == 0)
        {
            return (null, "No .cs files emitted");
        }

        var allContent = new StringBuilder();
        foreach(var content in emittedFiles.Values)
        {
            _ = allContent.AppendLine(content);
        }

        return (allContent.ToString(), null);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Verification helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that the emitted C# contains the expected attribute annotations
    /// for a given scenario.
    /// </summary>
    private static (bool Passed, string? Reason) VerifyEmittedAttributes(
        string csOutput, AttributeScenario scenario)
    {
        // A list that is a whole line by itself is grouped and separate at once. Searching for an
        // attribute as a substring instead finds a bare `[Fact]` inside `[Fact("reason")]`.
        var lines = csOutput.Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);
        foreach(var list in scenario.Lists)
        {
            var expectedLine = $"[{string.Join(", ", list.Attributes.Select(BuildExpectedCSharpAttr))}]";
            if(!lines.Contains(expectedLine))
            {
                return (false, $"Expected attribute list alone on its own line: '{expectedLine}' in output:\n{csOutput}");
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Builds the expected C# representation of a single attribute.
    /// </summary>
    private static string BuildExpectedCSharpAttr(AttrConfig attr)
    {
        var sb = new StringBuilder(attr.QualifiedName);

        if(attr.TypeArgs is not null)
        {
            _ = sb.Append($"<{attr.TypeArgs}>");
        }

        if(attr.Args.Count > 0)
        {
            _ = sb.Append('(');
            var parts = new List<string>();
            foreach(var arg in attr.Args)
            {
                if(arg.Name is not null)
                {
                    parts.Add($"{arg.Name} = {arg.ExpectedEmitted}");
                }
                else
                {
                    parts.Add(arg.ExpectedEmitted);
                }
            }

            _ = sb.Append(string.Join(", ", parts));
            _ = sb.Append(')');
        }

        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property test
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1: Attribute Emission Round-Trip.
    /// Generates 100 random attribute configurations, compiles each in-memory,
    /// and verifies the emitted C# preserves the attribute structure.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 3.1, 3.2, 3.3, 3.4, 3.6, 3.7, 3.8**
    /// </summary>
    /// <summary>
    /// Guards the round-trip's premise: every generated attribute must be one the language admits.
    /// Assigning a property twice is not, so a duplicate named argument would measure the emitter
    /// against input no source could contain.
    /// <para>
    /// Cheap to check and worth checking, because a generator drifting into invalid input fails
    /// silently -- the round-trip compares the emitted output against the same scenario record, so a
    /// duplicate appears on both sides and matches.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "PropertyBased")]
    public void GeneratedAttributes_AssignEachNamedArgumentOnce()
    {
        var scenarios = GenScenario().Sample(IterationCount).ToList();

        foreach(var scenario in scenarios)
        {
            foreach(var list in scenario.Lists)
            {
                foreach(var attr in list.Attributes)
                {
                    var names = attr.Args
                        .Where(a => a.Name is not null)
                        .Select(a => a.Name!)
                        .ToList();

                    Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "PropertyBased")]
    public void AttributeEmission_PreservesStructure_RoundTrip()
    {
        var scenarios = GenScenario().Sample(IterationCount).ToList();

        var failures = new List<string>();

        for(var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var mistSource = BuildMistProgram(scenario, i);

            var (csOutput, error) = CompileAndGetOutput(mistSource, i);

            if(csOutput is null)
            {
                failures.Add(
                    $"Scenario {i} failed to compile: {error}\n" +
                    $"  Scenario: {scenario}\n" +
                    $"  Source:\n{mistSource}");
                continue;
            }

            var (passed, reason) = VerifyEmittedAttributes(csOutput, scenario);
            if(!passed)
            {
                failures.Add(
                    $"Scenario {i} verification failed: {reason}\n" +
                    $"  Scenario: {scenario}\n" +
                    $"  Source:\n{mistSource}");
            }
        }

        if(failures.Count > 0)
        {
            var firstFailure = failures[0];
            throw new Exception(
                $"Property failed: {failures.Count}/{scenarios.Count} scenarios failed.\n\n" +
                $"First failure:\n{firstFailure}");
        }
    }
}