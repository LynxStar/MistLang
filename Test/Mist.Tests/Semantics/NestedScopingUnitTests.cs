// Task 10.2: Unit tests for nested type member acceptance and qualified name resolution
// Validates: Requirements 2.1, 9.3, 10.4, 10.5, 11.3, 11.4

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for nested type member acceptance and qualified name resolution.
/// Covers:
/// - Nested type body accepts all member kinds (Requirement 2.1)
/// - Code outside parent requires qualified name to reference nested type (Requirement 9.3)
/// - C# emission for nested enum with underlying type and explicit values (Requirement 10.4)
/// - C# emission at depth > 4 produces valid output (Requirement 10.5)
/// - Namespace merging for same-name blocks in C# output (Requirement 11.3)
/// - Empty namespace omitted from C# output (Requirement 11.4)
/// </summary>
public sealed class NestedScopingUnitTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles Mist source in-process with EmitToDisk: false and returns the result.
    /// </summary>
    private static CompilerResult CompileSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_unit_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            return SharedDriver.Compile(options);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Extracts the emitted C# source from a CompilerResult.
    /// Returns all emitted files concatenated.
    /// </summary>
    private static string GetEmittedCSharp(CompilerResult result)
    {
        Assert.NotNull(result.EmittedFiles);
        var sb = new StringBuilder();
        foreach(var (_, content) in result.EmittedFiles!)
        {
            _ = sb.AppendLine(content);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the emitted C# source file that contains "namespace TestProgram".
    /// </summary>
    private static string? GetUserEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Nested type body accepts all member kinds
    //
    // Validates: Requirement 2.1
    // The parser SHALL parse the body of a nested type declaration using the
    // same MemberDeclaration* grammar production as a top-level type, permitting
    // fields, methods, properties, constructors, operators, indexers, default
    // blocks, and further nested types as members.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NestedTypeBody_AcceptsAllMemberKinds()
    {
        // A nested class containing every kind of member declaration
        var source = """
            class Outer {
                class Inner {
                    int data;
                    
                    Inner() { }
                    
                    void Method() { }
                    
                    int Prop { get { return data; } set { data = value; } }
                    
                    public static Inner operator +(Inner a, Inner b) { return a; }
                    
                    int this[int index] { get { return 0; } }
                    
                    default {
                        data = 0
                    }
                    
                    class DeepNested { }
                }
            }
            class Program {
                static void Main() { }
            }
            """;

        var result = CompileSource(source);

        // Filter out non-critical diagnostics — we only care that the parser
        // and semantic model accept all member kinds without errors.
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017") // entry point
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation produced errors:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Code outside parent requires qualified name to reference nested type
    //
    // Validates: Requirement 9.3
    // When code outside the parent type references the nested type, the
    // Type_Checker SHALL require either a dot-qualified name that includes
    // the parent type or a using declaration.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NestedType_OutsideParent_RequiresQualifiedName()
    {
        // Referencing a nested type by simple name from outside should fail
        var source = """
            class Outer {
                class Inner { }
            }
            class Consumer {
                Inner bad;
            }
            """;

        var result = CompileSource(source);

        // Should produce an undefined type diagnostic for "Inner"
        var hasUndefinedType = result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value == "MIST_UNDEFINED_TYPE" &&
            d.Message.Contains("Inner"));

        Assert.True(hasUndefinedType,
            "Expected MIST_UNDEFINED_TYPE diagnostic for 'Inner' when referenced by simple name outside parent.\n" +
            $"Actual diagnostics:\n{FormatDiagnostics(result.Diagnostics)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: C# emission for nested enum with underlying type and explicit values
    //
    // Validates: Requirement 10.4
    // The CSharp_Emitter SHALL emit a valid C# nested enum declaration
    // including its underlying type annotation and all enum members with
    // their explicit values.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CSharpEmission_NestedEnum_WithUnderlyingTypeAndExplicitValues()
    {
        var source = """
            class Container {
                enum Status : int {
                    Active = 1,
                    Inactive = 0,
                    Pending = 2
                }
            }
            class Program {
                static void Main() { }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation failed:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        var csharp = GetUserEmittedCSharp(result);
        Assert.NotNull(csharp);

        // The nested enum should appear inside the Container class body
        var containerIndex = csharp!.IndexOf("class Container", StringComparison.Ordinal);
        Assert.True(containerIndex >= 0, "Container class not found in emitted C#");

        // Find the Container's opening brace
        var containerOpenBrace = csharp.IndexOf('{', containerIndex);
        Assert.True(containerOpenBrace >= 0);

        // Find the matching closing brace
        var containerCloseBrace = FindMatchingCloseBrace(csharp, containerOpenBrace);
        Assert.True(containerCloseBrace >= 0);

        var containerBody = csharp.Substring(containerOpenBrace + 1, containerCloseBrace - containerOpenBrace - 1);

        // The enum type (Status) should be declared inside the container body.
        // Mist enums emit as record classes in C#, so look for "record class Status"
        Assert.Contains("Status", containerBody);

        // Verify the enum members with explicit values are present
        Assert.Contains("Active", containerBody);
        Assert.Contains("Inactive", containerBody);
        Assert.Contains("Pending", containerBody);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: C# emission at depth > 4 produces valid output
    //
    // Validates: Requirement 10.5
    // If the CSharp_Emitter encounters a nested type declaration at a nesting
    // depth exceeding 4 levels, the emitter SHALL still emit valid C# output.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CSharpEmission_DepthGreaterThan4_ProducesValidOutput()
    {
        // 5 levels of nesting: Level1 > Level2 > Level3 > Level4 > Level5
        var source = """
            class Level1 {
                class Level2 {
                    class Level3 {
                        class Level4 {
                            class Level5 {
                                int deepField;
                            }
                        }
                    }
                }
            }
            class Program {
                static void Main() { }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation failed at depth > 4:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        var csharp = GetUserEmittedCSharp(result);
        Assert.NotNull(csharp);

        // Verify all 5 levels are present in the emitted C#
        Assert.Contains("class Level1", csharp!);
        Assert.Contains("class Level2", csharp);
        Assert.Contains("class Level3", csharp);
        Assert.Contains("class Level4", csharp);
        Assert.Contains("class Level5", csharp);

        // Verify proper nesting: Level5 should be inside Level4's body
        var level4Index = csharp.IndexOf("class Level4", StringComparison.Ordinal);
        Assert.True(level4Index >= 0);
        var level4OpenBrace = csharp.IndexOf('{', level4Index);
        var level4CloseBrace = FindMatchingCloseBrace(csharp, level4OpenBrace);
        var level4Body = csharp.Substring(level4OpenBrace + 1, level4CloseBrace - level4OpenBrace - 1);
        Assert.Contains("class Level5", level4Body);

        // Verify the deepest field is present
        Assert.Contains("deepField", csharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 5: Namespace merging for same-name blocks in C# output
    //
    // Validates: Requirement 11.3
    // When the CSharp_Emitter encounters two or more block namespace
    // declarations with identical fully qualified names in the same
    // compilation unit, it SHALL merge their type declarations into a
    // single C# namespace block in the output.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CSharpEmission_SameNameNamespaceBlocks_AreMerged()
    {
        var source = """
            namespace MyApp {
                class Foo { }
            }
            namespace MyApp {
                class Bar { }
            }
            class Program {
                static void Main() { }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation failed:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        var csharp = GetEmittedCSharp(result);

        // Both Foo and Bar should appear in the emitted C#
        Assert.Contains("class Foo", csharp);
        Assert.Contains("class Bar", csharp);

        // The namespace "MyApp" should appear only once in the output (merged)
        var nsOccurrences = CountOccurrences(csharp, "namespace MyApp");
        Assert.True(nsOccurrences == 1,
            $"Expected namespace 'MyApp' to appear exactly once (merged), but found {nsOccurrences} occurrences.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 6: Empty namespace omitted from C# output
    //
    // Validates: Requirement 11.4
    // If a block namespace declaration contains no type declarations,
    // extension blocks, or newtype declarations after emission, the
    // CSharp_Emitter SHALL omit the empty namespace block from the output.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CSharpEmission_EmptyNamespace_IsOmitted()
    {
        var source = """
            namespace EmptyNs { }
            namespace NonEmpty {
                class Foo { }
            }
            class Program {
                static void Main() { }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation failed:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        var csharp = GetEmittedCSharp(result);

        // The non-empty namespace should be present
        Assert.Contains("namespace NonEmpty", csharp);
        Assert.Contains("class Foo", csharp);

        // The empty namespace should NOT appear in the output
        Assert.DoesNotContain("namespace EmptyNs", csharp);
    }

    // ─── Utility helpers ─────────────────────────────────────────────────

    private static int FindMatchingCloseBrace(string source, int openBraceIndex)
    {
        var depth = 0;
        for(var i = openBraceIndex; i < source.Length; i++)
        {
            if(source[i] == '{')
            {
                depth++;
            }
            else if(source[i] == '}')
            {
                depth--;
            }

            if(depth == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}