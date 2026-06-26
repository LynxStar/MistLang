// Feature: drop-semantics, Property 9: With-block composes drop with context pop
//
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
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

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Describes the with-form variant used to bind a droppable type.
/// </summary>
public enum WithFormKind
{
    /// <summary><c>with(expr) { ... }</c> — block form.</summary>
    BlockForm,

    /// <summary><c>with var x = expr;</c> — inferred variable declaration.</summary>
    InferredVar,

    /// <summary><c>with Type x = expr;</c> — typed variable declaration.</summary>
    TypedVar,

    /// <summary><c>with Type _ = expr;</c> — discard form.</summary>
    Discard,

    /// <summary><c>with expr;</c> — push statement form.</summary>
    PushStatement,
}

/// <summary>
/// Describes the substructural discipline of the droppable type being bound.
/// Only disciplines that fire Drop() in with-blocks are included.
/// </summary>
public enum WithDropDiscipline
{
    /// <summary>Unrestricted type with a Drop() method — Drop fires only in with-blocks.</summary>
    Unrestricted,

    /// <summary>Affine type — Drop fires only in with-blocks, at most once.</summary>
    Affine,

    /// <summary>Linear type — with-block exit satisfies the linear obligation.</summary>
    Linear,

    /// <summary>Relevant type — Drop fires at scope exit (idempotent).</summary>
    Relevant,
}

/// <summary>
/// A generated scenario for testing with-block drop composition.
/// </summary>
public sealed class WithBlockDropScenario
{
    public string Source { get; }
    public WithFormKind Form { get; }
    public WithDropDiscipline Discipline { get; }
    public string TypeName { get; }
    public string Description { get; }

    public WithBlockDropScenario(string source, WithFormKind form, WithDropDiscipline discipline,
        string typeName, string description)
    {
        Source = source;
        Form = form;
        Discipline = discipline;
        TypeName = typeName;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class WithBlockDropCompositionGenerators
{
    private static readonly string[] TypeSuffixes = ["Ctx", "Svc", "Hdl", "Res", "Mgr"];

    public static Gen<WithBlockDropScenario> GenWithBlockDropScenario()
    {
        return from suffix in Gen.Elements(TypeSuffixes)
               from form in Gen.Elements(
                   WithFormKind.BlockForm,
                   WithFormKind.InferredVar,
                   WithFormKind.TypedVar,
                   WithFormKind.Discard,
                   WithFormKind.PushStatement)
               from discipline in Gen.Elements(
                   WithDropDiscipline.Unrestricted,
                   WithDropDiscipline.Affine,
                   WithDropDiscipline.Linear,
                   WithDropDiscipline.Relevant)
               let typeName = $"My{suffix}"
               let source = BuildSource(typeName, form, discipline)
               select new WithBlockDropScenario(
                   source, form, discipline, typeName,
                   $"with-form={form}, discipline={discipline}, type={typeName}");
    }

    private static string GetModifier(WithDropDiscipline discipline) => discipline switch
    {
        WithDropDiscipline.Unrestricted => "",
        WithDropDiscipline.Affine => "affine ",
        WithDropDiscipline.Linear => "linear ",
        WithDropDiscipline.Relevant => "relevant ",
        _ => throw new ArgumentOutOfRangeException(nameof(discipline)),
    };

    private static string BuildSource(string typeName, WithFormKind form, WithDropDiscipline discipline)
    {
        var modifier = GetModifier(discipline);

        var typeDecl = $$"""
            {{modifier}}class {{typeName}}
            {
                public void Drop()
                {
                }

                public int GetValue()
                {
                    return 1;
                }
            }
            """;

        var withUsage = form switch
        {
            WithFormKind.BlockForm => $$"""
                        with(new {{typeName}}())
                        {
                            int x = 42;
                        }
            """,
            WithFormKind.InferredVar => $$"""
                        with var r = new {{typeName}}();
                        int x = 42;
            """,
            WithFormKind.TypedVar => $$"""
                        with {{typeName}} r = new {{typeName}}();
                        int x = 42;
            """,
            WithFormKind.Discard => $$"""
                        with {{typeName}} _ = new {{typeName}}();
                        int x = 42;
            """,
            WithFormKind.PushStatement => $$"""
                        with new {{typeName}}();
                        int x = 42;
            """,
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        return $$"""
            {{typeDecl}}
            class Program
            {
                public static void Main()
                {
            {{withUsage}}
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for Property 9: With-block composes drop with context pop at
/// block exit.
///
/// Feature: drop-semantics, Property 9: With-block composes drop with context pop
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7**
///
/// For any <c>with</c>-form binding whose type has a <c>Drop()</c> method, the compiler
/// SHALL emit a <c>finally</c> block containing BOTH <c>MistContext.Pop&lt;T&gt;()</c> AND
/// a <c>Drop()</c> call (or <c>await</c> for Task/ValueTask), with the <c>Pop</c> emitted
/// BEFORE the <c>Drop</c>. The existing structured-context push/pop semantics remain
/// unchanged (Requirement 5.3), and drop is composed additively.
///
/// All compilation runs fully in-process via a shared <see cref="CompilerDriver"/> with
/// <c>EmitToDisk: false</c>; the emitted C# text is inspected directly.
/// Spawns 0 external processes.
/// </summary>
public sealed class WithBlockDropCompositionPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public WithBlockDropCompositionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistWithDropComposition_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across iterations) ────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<WithBlockDropScenario> ArbitraryWithBlockDropScenario() =>
        WithBlockDropCompositionGenerators.GenWithBlockDropScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Returns the emitted user C# file (the one containing the test namespace), or null.
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
            return null;

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
                return content;
        }

        return null;
    }

    /// <summary>
    /// Finds the finally block region in the emitted C# that contains the Pop for the
    /// given type name. Returns the content of that finally block, or null if not found.
    /// </summary>
    private static string? ExtractFinallyBlockContent(string emittedCSharp, string typeName)
    {
        // Find `finally` keyword followed by `{` that contains the Pop<TypeName>()
        var popPattern = $"MistContext.Pop<{typeName}>();";
        var popIndex = emittedCSharp.IndexOf(popPattern, StringComparison.Ordinal);
        if(popIndex < 0)
            return null;

        // Walk backwards from the pop to find the enclosing finally { ... }
        // Look for the nearest `finally` keyword before the pop
        var searchRegion = emittedCSharp[..popIndex];
        var finallyIndex = searchRegion.LastIndexOf("finally", StringComparison.Ordinal);
        if(finallyIndex < 0)
            return null;

        // Find the opening brace after `finally`
        var openBrace = emittedCSharp.IndexOf('{', finallyIndex);
        if(openBrace < 0)
            return null;

        // Find the matching closing brace
        var closeBrace = FindMatchingCloseBrace(emittedCSharp, openBrace);
        if(closeBrace < 0)
            return null;

        return emittedCSharp.Substring(openBrace + 1, closeBrace - openBrace - 1);
    }

    private static int FindMatchingCloseBrace(string source, int openBraceIndex)
    {
        var depth = 0;
        for(var i = openBraceIndex; i < source.Length; i++)
        {
            if(source[i] == '{') depth++;
            else if(source[i] == '}') depth--;

            if(depth == 0)
                return i;
        }
        return -1;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: With-block composes drop with context pop at block exit
    //
    // **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7**
    //
    // For each generated with-form + discipline combination:
    // 1. The emitted finally block MUST contain Pop<T>().
    // 2. The emitted finally block MUST contain a Drop() call (or await).
    // 3. Pop<T>() MUST appear BEFORE Drop() in the finally block.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WithBlockDropCompositionPropertyTests) })]
    public Property WithForm_EmitsFinallyWithPopBeforeDrop(WithBlockDropScenario scenario)
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
                .Label($"No emitted C# file found.\nSource:\n{scenario.Source}");
        }

        var finallyBody = ExtractFinallyBlockContent(emittedCSharp, scenario.TypeName);
        if(finallyBody is null)
        {
            return false.ToProperty()
                .Label($"No finally block containing 'MistContext.Pop<{scenario.TypeName}>()' found.\n" +
                       $"Emitted C#:\n{emittedCSharp}");
        }

        // Check that Pop<T>() is present in the finally block
        var popPattern = $"MistContext.Pop<{scenario.TypeName}>();";
        if(!finallyBody.Contains(popPattern, StringComparison.Ordinal))
        {
            return false.ToProperty()
                .Label($"Pop<{scenario.TypeName}>() not found in finally block.\n" +
                       $"Finally body:\n{finallyBody}");
        }

        // Check that Drop() (or await) is present in the finally block
        var hasDropCall = finallyBody.Contains(".Drop();", StringComparison.Ordinal);
        var hasAwait = finallyBody.Contains("await ", StringComparison.Ordinal);
        if(!hasDropCall && !hasAwait)
        {
            return false.ToProperty()
                .Label($"Neither '.Drop()' nor 'await' found in finally block.\n" +
                       $"Finally body:\n{finallyBody}\n" +
                       $"Full emitted C#:\n{emittedCSharp}");
        }

        // Check ordering: Pop<T>() MUST appear BEFORE Drop()/await in the finally block
        var popIndex = finallyBody.IndexOf(popPattern, StringComparison.Ordinal);
        var dropIndex = hasDropCall
            ? finallyBody.IndexOf(".Drop();", StringComparison.Ordinal)
            : finallyBody.IndexOf("await ", StringComparison.Ordinal);

        if(popIndex >= dropIndex)
        {
            return false.ToProperty()
                .Label($"Pop<{scenario.TypeName}>() does not appear BEFORE Drop()/await in finally.\n" +
                       $"Pop at index {popIndex}, Drop/await at index {dropIndex}.\n" +
                       $"Finally body:\n{finallyBody}");
        }

        return true.ToProperty()
            .Label($"Pop<{scenario.TypeName}>() before Drop()/await in finally block — correct.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Example-based tests (deterministic, covering each with-form)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// with(expr) { ... } block form: Pop before Drop in finally.
    /// **Validates: Requirement 5.1**
    /// </summary>
    [Fact]
    public void WithBlock_DroppableType_PopBeforeDropInFinally()
    {
        var source = """
            class Resource
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                    with(new Resource())
                    {
                        int x = 42;
                    }
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;
        var finallyBody = ExtractFinallyBlockContent(csharp, "Resource");
        Assert.NotNull(finallyBody);
        Assert.Contains("MistContext.Pop<Resource>();", finallyBody);
        Assert.Contains(".Drop();", finallyBody);

        var popIdx = finallyBody.IndexOf("MistContext.Pop<Resource>();", StringComparison.Ordinal);
        var dropIdx = finallyBody.IndexOf(".Drop();", StringComparison.Ordinal);
        Assert.True(popIdx < dropIdx, $"Pop must come before Drop in finally. Pop@{popIdx}, Drop@{dropIdx}");
    }

    /// <summary>
    /// with var x = expr; inferred declaration form: Pop before Drop in finally.
    /// **Validates: Requirement 5.2**
    /// </summary>
    [Fact]
    public void WithVarDecl_DroppableType_PopBeforeDropInFinally()
    {
        var source = """
            class Resource
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                    with var r = new Resource();
                    int x = 42;
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;
        var finallyBody = ExtractFinallyBlockContent(csharp, "Resource");
        Assert.NotNull(finallyBody);
        Assert.Contains("MistContext.Pop<Resource>();", finallyBody);
        Assert.Contains("r.Drop();", finallyBody);

        var popIdx = finallyBody.IndexOf("MistContext.Pop<Resource>();", StringComparison.Ordinal);
        var dropIdx = finallyBody.IndexOf("r.Drop();", StringComparison.Ordinal);
        Assert.True(popIdx < dropIdx, $"Pop must come before Drop in finally. Pop@{popIdx}, Drop@{dropIdx}");
    }

    /// <summary>
    /// with Type x = expr; typed declaration form: Pop before Drop in finally.
    /// **Validates: Requirement 5.2**
    /// </summary>
    [Fact]
    public void WithTypedDecl_DroppableType_PopBeforeDropInFinally()
    {
        var source = """
            class Resource
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                    with Resource r = new Resource();
                    int x = 42;
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;
        var finallyBody = ExtractFinallyBlockContent(csharp, "Resource");
        Assert.NotNull(finallyBody);
        Assert.Contains("MistContext.Pop<Resource>();", finallyBody);
        Assert.Contains("r.Drop();", finallyBody);

        var popIdx = finallyBody.IndexOf("MistContext.Pop<Resource>();", StringComparison.Ordinal);
        var dropIdx = finallyBody.IndexOf("r.Drop();", StringComparison.Ordinal);
        Assert.True(popIdx < dropIdx, $"Pop must come before Drop in finally. Pop@{popIdx}, Drop@{dropIdx}");
    }

    /// <summary>
    /// with Type _ = expr; discard form: Pop before Drop in finally.
    /// **Validates: Requirement 5.4**
    /// </summary>
    [Fact]
    public void WithDiscard_DroppableType_PopBeforeDropInFinally()
    {
        var source = """
            class Resource
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                    with Resource _ = new Resource();
                    int x = 42;
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;
        var finallyBody = ExtractFinallyBlockContent(csharp, "Resource");
        Assert.NotNull(finallyBody);
        Assert.Contains("MistContext.Pop<Resource>();", finallyBody);
        Assert.Contains(".Drop();", finallyBody);

        var popIdx = finallyBody.IndexOf("MistContext.Pop<Resource>();", StringComparison.Ordinal);
        var dropIdx = finallyBody.IndexOf(".Drop();", StringComparison.Ordinal);
        Assert.True(popIdx < dropIdx, $"Pop must come before Drop in finally. Pop@{popIdx}, Drop@{dropIdx}");
    }

    /// <summary>
    /// with expr; push statement form: Pop before Drop in finally.
    /// **Validates: Requirement 5.1**
    /// </summary>
    [Fact]
    public void WithPushStmt_DroppableType_PopBeforeDropInFinally()
    {
        var source = """
            class Resource
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                    with new Resource();
                    int x = 42;
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;
        var finallyBody = ExtractFinallyBlockContent(csharp, "Resource");
        Assert.NotNull(finallyBody);
        Assert.Contains("MistContext.Pop<Resource>();", finallyBody);
        Assert.Contains(".Drop();", finallyBody);

        var popIdx = finallyBody.IndexOf("MistContext.Pop<Resource>();", StringComparison.Ordinal);
        var dropIdx = finallyBody.IndexOf(".Drop();", StringComparison.Ordinal);
        Assert.True(popIdx < dropIdx, $"Pop must come before Drop in finally. Pop@{popIdx}, Drop@{dropIdx}");
    }

    /// <summary>
    /// Affine type in with: Drop fires via with-block (the only auto-trigger for affine).
    /// **Validates: Requirement 5.4**
    /// </summary>
    [Fact]
    public void AffineType_InWithBlock_DropFires()
    {
        var source = """
            affine class Token
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                    with(new Token())
                    {
                        int x = 1;
                    }
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;
        var finallyBody = ExtractFinallyBlockContent(csharp, "Token");
        Assert.NotNull(finallyBody);
        Assert.Contains("MistContext.Pop<Token>();", finallyBody);
        Assert.Contains(".Drop();", finallyBody);
    }

    /// <summary>
    /// Linear type bound by with: with-exit satisfies the obligation (no double drop).
    /// **Validates: Requirement 5.5**
    /// </summary>
    [Fact]
    public void LinearType_InWithBlock_NoDoubleDrop()
    {
        var source = """
            linear class Handle
            {
                public void Drop()
                {
                }
            }
            class Program
            {
                public static void Main()
                {
                    with(new Handle())
                    {
                        int x = 1;
                    }
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;

        // The finally block should have exactly one Drop() call
        var finallyBody = ExtractFinallyBlockContent(csharp, "Handle");
        Assert.NotNull(finallyBody);
        Assert.Contains(".Drop();", finallyBody);

        // Count the number of Drop() calls in the entire emitted output — should be exactly 1
        var dropCount = Regex.Matches(csharp, @"\.Drop\(\)").Count;
        Assert.True(dropCount == 1,
            $"Expected exactly 1 Drop() call (no double drop), but found {dropCount}.\nEmitted:\n{csharp}");
    }

    /// <summary>
    /// Non-void Drop() in with context: return value is discarded.
    /// **Validates: Requirement 5.6**
    /// </summary>
    [Fact]
    public void WithBlock_NonVoidDrop_ReturnValueDiscarded()
    {
        var source = """
            affine class Box
            {
                public int Drop()
                {
                    return 42;
                }
            }
            class Program
            {
                public static void Main()
                {
                    with(new Box())
                    {
                        int x = 1;
                    }
                }
            }
            """;

        var result = CompileSource(source);
        Assert.True(result.Succeeded, FormatDiagnostics(result));

        var csharp = GetEmittedCSharp(result)!;
        var finallyBody = ExtractFinallyBlockContent(csharp, "Box");
        Assert.NotNull(finallyBody);

        // The Drop() call should be a statement (discarded), not assigned to anything
        Assert.Contains(".Drop();", finallyBody);
        // Should not contain an assignment like `var _ = ...Drop()` or `int _ = ...Drop()`
        Assert.DoesNotContain("= ", finallyBody.Split('\n')
            .FirstOrDefault(l => l.Contains(".Drop()")) ?? "");
    }

    // ─── Utility ─────────────────────────────────────────────────────────

    private static string FormatDiagnostics(CompilerResult result)
    {
        return string.Join("\n", result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }
}