// Feature: iterators-enumerables, Property 6: Emitter maps Mist iterator interfaces to C# System.Collections.Generic counterparts
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for verifying that a type implementing IEnumerable&lt;TElement&gt; emits
/// System.Collections.Generic.IEnumerable&lt;TElement&gt; in C#.
/// </summary>
public sealed class EnumerableEmissionScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string ClassName { get; }
    public string Description { get; }

    public EnumerableEmissionScenario(string source, string elementType, string className, string description)
    {
        Source = source;
        ElementType = elementType;
        ClassName = className;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Scenario for verifying that foreach statements are emitted as plain C# foreach
/// (no desugaring into GetEnumerator/MoveNext/Current calls).
/// </summary>
public sealed class ForeachEmissionScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string Description { get; }

    public ForeachEmissionScenario(string source, string elementType, string description)
    {
        Source = source;
        ElementType = elementType;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for C# emission of iterator/enumerable interfaces.
///
/// Property 6: Emitter maps Mist iterator interfaces to C# System.Collections.Generic counterparts
///
/// For any type implementing Mist's IEnumerable&lt;TElement&gt; or IEnumerator&lt;TElement&gt;,
/// the emitted C# source SHALL declare the type as implementing
/// System.Collections.Generic.IEnumerable&lt;TElement&gt; or System.Collections.Generic.IEnumerator&lt;TElement&gt;
/// respectively. The emitted foreach statement SHALL be a plain C# foreach with no desugaring
/// into explicit GetEnumerator()/MoveNext()/Current calls.
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
///
/// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5**
/// </summary>
public sealed class IteratorEmissionPropertyTests : IDisposable
{
    private readonly string tempDir;

    public IteratorEmissionPropertyTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "MistIteratorEmissionPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ElementTypes = ["int", "string", "bool", "double", "float", "long", "char"];
    private static readonly string[] ClassNames = ["MyCollection", "DataStream", "ItemList", "Sequence", "Elements", "Series"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a class implementing IEnumerable&lt;TElement&gt; with a
    /// nested struct Enumerator implementing IEnumerator&lt;TElement&gt;.
    /// </summary>
    private static Gen<EnumerableEmissionScenario> GenEnumerableScenario()
    {
        return from elementType in Gen.Elements(ElementTypes)
               from className in Gen.Elements(ClassNames)
               let source = BuildEnumerableSource(className, elementType)
               select new EnumerableEmissionScenario(
                   source, elementType, className,
                   $"class {className} : IEnumerable<{elementType}>");
    }

    /// <summary>
    /// Generates a scenario with a foreach statement over a user-defined IEnumerable type.
    /// </summary>
    private static Gen<ForeachEmissionScenario> GenForeachScenario()
    {
        return from elementType in Gen.Elements(ElementTypes)
               from className in Gen.Elements(ClassNames)
               let source = BuildForeachSource(className, elementType)
               select new ForeachEmissionScenario(
                   source, elementType,
                   $"foreach ({elementType} x in {className})");
    }

    // ─── Source builders ─────────────────────────────────────────────────

    /// <summary>
    /// The IEnumerator and IEnumerable interfaces are defined in the Standard
    /// namespace in the stdlib. The CompilerDriver loads them automatically.
    /// User code references them via <c>using Standard;</c>.
    /// </summary>
    private static string BuildEnumerableSource(string className, string elementType)
    {
        // Place the test class in the Standard namespace so it can directly
        // reference IEnumerable/IEnumerator (which are also in Standard).
        // The using-directive import happens in Pass 2, but interface list
        // resolution happens in Pass 1, so cross-namespace references via
        // `using` aren't available during interface resolution.
        return $$"""
            namespace Standard;

            class {{className}} : IEnumerable<{{elementType}}>
            {
                public struct Enumerator : IEnumerator<{{elementType}}>
                {
                    private int index;
                    public Enumerator()
                    {
                        this.index = 0;
                    }
                    public bool MoveNext()
                    {
                        this.index = this.index + 1;
                        return false;
                    }
                    public {{elementType}} Current { get; }
                }

                public Enumerator GetEnumerator()
                {
                    return new Enumerator();
                }
            }

            public class Program
            {
                public static void Main()
                {
                }
            }
            """;
    }

    private static string BuildForeachSource(string className, string elementType)
    {
        return $$"""
            namespace Standard;

            class {{className}} : IEnumerable<{{elementType}}>
            {
                public struct Enumerator : IEnumerator<{{elementType}}>
                {
                    private int index;
                    public Enumerator()
                    {
                        this.index = 0;
                    }
                    public bool MoveNext()
                    {
                        this.index = this.index + 1;
                        return false;
                    }
                    public {{elementType}} Current { get; }
                }

                public Enumerator GetEnumerator()
                {
                    return new Enumerator();
                }
            }

            public class Program
            {
                public static void Main()
                {
                    var collection = new {{className}}();
                    foreach ({{elementType}} item in collection)
                    {
                    }
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(tempDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: tempDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: tempDir,
            BinDirectory: Path.Combine(tempDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "Standard",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Gets the emitted C# content from the compilation result for the user namespace.
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        // Find the user file — the one containing our test class (not the stdlib files).
        // Since we put our test code in the Standard namespace, look for the file
        // that contains our generated class names.
        foreach(var (fileName, content) in result.EmittedFiles)
        {
            // Skip the .csproj file
            if(fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The user file will contain "class Program" (our test entry point)
            if(content.Contains("class Program"))
            {
                return content;
            }
        }

        return null;
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<EnumerableEmissionScenario> ArbitraryEnumerableEmission()
    {
        return GenEnumerableScenario().ToArbitrary();
    }

    public static Arbitrary<ForeachEmissionScenario> ArbitraryForeachEmission()
    {
        return GenForeachScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6a: IEnumerable<TElement> maps to System.Collections.Generic.IEnumerable<TElement>
    //
    // For any type implementing Mist's IEnumerable<TElement>, the emitted C#
    // SHALL declare the type as implementing System.Collections.Generic.IEnumerable<TElement>.
    //
    // **Validates: Requirements 4.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(IteratorEmissionPropertyTests) })]
    public Property IEnumerable_EmitsSystemCollectionsGenericIEnumerable(EnumerableEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found for namespace TestProject\nSource:\n{scenario.Source}");
        }

        var expectedIEnumerable = $"System.Collections.Generic.IEnumerable<{MapToCSharpType(scenario.ElementType)}>";
        var containsIEnumerable = emittedCSharp.Contains(expectedIEnumerable);

        return containsIEnumerable.ToProperty()
            .Label($"Expected emitted C# to contain '{expectedIEnumerable}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6b: IEnumerator<TElement> maps to System.Collections.Generic.IEnumerator<TElement>
    //
    // For any type implementing Mist's IEnumerator<TElement>, the emitted C#
    // SHALL declare the type as implementing System.Collections.Generic.IEnumerator<TElement>.
    //
    // **Validates: Requirements 4.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(IteratorEmissionPropertyTests) })]
    public Property IEnumerator_EmitsSystemCollectionsGenericIEnumerator(EnumerableEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found for namespace TestProject\nSource:\n{scenario.Source}");
        }

        var expectedIEnumerator = $"System.Collections.Generic.IEnumerator<{MapToCSharpType(scenario.ElementType)}>";
        var containsIEnumerator = emittedCSharp.Contains(expectedIEnumerator);

        return containsIEnumerator.ToProperty()
            .Label($"Expected emitted C# to contain '{expectedIEnumerator}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6c: Foreach emits plain C# foreach (no desugaring)
    //
    // The emitted foreach statement SHALL be a plain C# foreach with no
    // desugaring into explicit GetEnumerator()/MoveNext()/Current calls.
    //
    // **Validates: Requirements 4.3, 4.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(IteratorEmissionPropertyTests) })]
    public Property Foreach_EmitsPlainCSharpForeach_NoDesugaring(ForeachEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found for namespace TestProject\nSource:\n{scenario.Source}");
        }

        // The emitted C# MUST contain a foreach statement
        var containsForeach = emittedCSharp.Contains("foreach");

        // The emitted C# MUST NOT contain desugared iteration patterns
        // (explicit GetEnumerator/MoveNext/Current calls outside of the
        // Enumerator struct's own member declarations)
        var mainMethodSection = ExtractMainMethodSection(emittedCSharp);
        var containsGetEnumeratorCall = mainMethodSection is not null
            && mainMethodSection.Contains(".GetEnumerator()");
        var containsMoveNextCall = mainMethodSection is not null
            && mainMethodSection.Contains(".MoveNext()");
        var containsCurrentAccess = mainMethodSection is not null
            && mainMethodSection.Contains(".Current");

        var noDesugaring = !containsGetEnumeratorCall && !containsMoveNextCall && !containsCurrentAccess;

        return (containsForeach && noDesugaring).ToProperty()
            .Label($"Expected plain foreach (no desugaring). " +
                   $"containsForeach={containsForeach}, " +
                   $"GetEnumerator in Main={containsGetEnumeratorCall}, " +
                   $"MoveNext in Main={containsMoveNextCall}, " +
                   $"Current in Main={containsCurrentAccess}\n" +
                   $"Emitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6d: Enumerator struct emits IEnumerator<TElement> implementation
    //
    // The concrete enumerator type SHALL be emitted with its
    // IEnumerator<TElement> implementation intact.
    //
    // **Validates: Requirements 4.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(IteratorEmissionPropertyTests) })]
    public Property EnumeratorStruct_EmitsWithIEnumeratorImplementation(EnumerableEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found for namespace TestProject\nSource:\n{scenario.Source}");
        }

        // The Enumerator struct should implement IEnumerator<TElement>
        var expectedInterface = $"System.Collections.Generic.IEnumerator<{MapToCSharpType(scenario.ElementType)}>";
        var containsEnumeratorWithInterface = emittedCSharp.Contains(expectedInterface);

        // The Enumerator struct should have MoveNext and Current members
        var containsMoveNext = emittedCSharp.Contains("MoveNext()");
        var containsCurrent = emittedCSharp.Contains("Current");

        return (containsEnumeratorWithInterface && containsMoveNext && containsCurrent).ToProperty()
            .Label($"Expected Enumerator struct with '{expectedInterface}', MoveNext(), and Current.\n" +
                   $"HasInterface={containsEnumeratorWithInterface}, HasMoveNext={containsMoveNext}, HasCurrent={containsCurrent}\n" +
                   $"Emitted C#:\n{emittedCSharp}");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Maps Mist primitive type keywords to their C# equivalents as they appear
    /// in emitted code.
    /// </summary>
    private static string MapToCSharpType(string mistType)
    {
        return mistType switch
        {
            "int" => "int",
            "string" => "string",
            "bool" => "bool",
            "double" => "double",
            "float" => "float",
            "long" => "long",
            "char" => "char",
            _ => mistType
        };
    }

    /// <summary>
    /// Extracts the Main method body section from emitted C# to check for
    /// desugaring patterns without false positives from the Enumerator struct itself.
    /// </summary>
    private static string? ExtractMainMethodSection(string emittedCSharp)
    {
        // Find the Main method and extract its body
        var mainIndex = emittedCSharp.IndexOf("static void Main(", StringComparison.Ordinal);
        if(mainIndex < 0)
        {
            mainIndex = emittedCSharp.IndexOf("void Main(", StringComparison.Ordinal);
        }

        if(mainIndex < 0)
        {
            return null;
        }

        // Find the opening brace of Main
        var braceStart = emittedCSharp.IndexOf('{', mainIndex);
        if(braceStart < 0)
        {
            return null;
        }

        // Find the matching closing brace (simple brace counting)
        var depth = 1;
        var pos = braceStart + 1;
        while(pos < emittedCSharp.Length && depth > 0)
        {
            if(emittedCSharp[pos] == '{')
            {
                depth++;
            }
            else if(emittedCSharp[pos] == '}')
            {
                depth--;
            }

            pos++;
        }

        return emittedCSharp.Substring(braceStart, pos - braceStart);
    }
}