// Feature: ownership-v2, Property 12: Zero runtime ownership artifacts
// **Validates: Requirements 14.1, 14.3, 14.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for Property 12: Zero runtime ownership artifacts.
///
/// For all compiled programs, emitted C# contains no <c>owned</c> keyword, no reference
/// counting, no ownership state fields, no move-nulling — only <c>.Clone()</c> at resolved
/// clone sites.
///
/// In-process compile, inspect emitted C# text. No process spawning.
///
/// **Validates: Requirements 14.1, 14.3, 14.4**
/// </summary>
public sealed class ZeroRuntimeOwnershipArtifactsPropertyTests : IDisposable
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public ZeroRuntimeOwnershipArtifactsPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistZeroRuntimePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private IReadOnlyDictionary<string, string>? CompileAndGetEmittedFiles(string source)
    {
        var sourcePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(sourcePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);

        if (!result.Succeeded)
            return null;

        return result.EmittedFiles;
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A program that exercises ownership constructs and may trigger implicit cloning.
    /// </summary>
    public sealed record OwnershipProgram(string Source, string Label, bool ExpectsClone)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "value", "item", "content"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "instance", "item"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a program with an owned type declaration, owned binding, and move —
    /// no clone expected (already-owned source moved into owned destination).
    /// </summary>
    private static Gen<OwnershipProgram> GenOwnedTypeWithMove()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               let source = $$"""
                   using Standard;

                   owned class {{typeName}} {
                       int {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} {{varName}} = new {{typeName}}();
                           owned {{typeName}} moved = {{varName}};
                       }
                   }
                   """
               select new OwnershipProgram(source,
                   $"owned type move: {typeName}.{fieldName}, var {varName}",
                   ExpectsClone: false);
    }

    /// <summary>
    /// Generates a program with an owned parameter pass — no clone expected
    /// (already-owned source passed to owned parameter).
    /// </summary>
    private static Gen<OwnershipProgram> GenOwnedParameterPass()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = $$"""
                   using Standard;

                   owned class {{typeName}} {
                       int data;
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} {{varName}} = new {{typeName}}();
                           Consume({{varName}});
                       }
                       static void Consume(owned {{typeName}} p) {
                       }
                   }
                   """
               select new OwnershipProgram(source,
                   $"owned param pass: {typeName}, var {varName}",
                   ExpectsClone: false);
    }

    /// <summary>
    /// Generates a program with an owned return type — no clone expected.
    /// </summary>
    private static Gen<OwnershipProgram> GenOwnedReturnType()
    {
        return from typeName in Gen.Elements(TypeNames)
               let source = $$"""
                   using Standard;

                   owned class {{typeName}} {
                       int data;
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} r = Create();
                       }
                       static owned {{typeName}} Create() {
                           return new {{typeName}}();
                       }
                   }
                   """
               select new OwnershipProgram(source,
                   $"owned return: {typeName}",
                   ExpectsClone: false);
    }

    /// <summary>
    /// Generates a program with a non-owned clonable source assigned to an owned binding —
    /// Clone IS expected (IClonable bridging).
    /// </summary>
    private static Gen<OwnershipProgram> GenImplicitCloneScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = $$"""
                   using Standard;

                   class {{typeName}} : IClonable<{{typeName}}> {
                       int data;
                       public owned {{typeName}} Clone() {
                           return new {{typeName}}();
                       }
                   }
                   class Program {
                       public static void Main() {
                           var {{varName}} = new {{typeName}}();
                           owned {{typeName}} cloned = {{varName}};
                       }
                   }
                   """
               select new OwnershipProgram(source,
                   $"implicit clone: {typeName}, var {varName}",
                   ExpectsClone: true);
    }

    /// <summary>
    /// Generates a program with an owned type that has field assignment from a constructor
    /// call — no clone expected (constructor yields ownership).
    /// </summary>
    private static Gen<OwnershipProgram> GenOwnedTypeFieldAssignment()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $$"""
                   using Standard;

                   owned class Inner {
                       int amount;
                   }
                   owned class {{typeName}} {
                       Inner {{fieldName}};
                       public {{typeName}}() {
                           this.{{fieldName}} = new Inner();
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} obj = new {{typeName}}();
                       }
                   }
                   """
               select new OwnershipProgram(source,
                   $"owned field assignment: {typeName}.{fieldName}",
                   ExpectsClone: false);
    }

    /// <summary>
    /// Generates a program with lending tiers (borrowed/frozen) — no clone expected,
    /// no runtime ownership artifacts.
    /// </summary>
    private static Gen<OwnershipProgram> GenLendingScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               let source = $$"""
                   using Standard;

                   owned class {{typeName}} {
                       int data;
                       public int GetData() {
                           return this.data;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} owner = new {{typeName}}();
                           ReadOnly(owner);
                       }
                       static void ReadOnly(frozen {{typeName}} f) {
                           var v = f.GetData();
                       }
                   }
                   """
               select new OwnershipProgram(source,
                   $"lending: frozen {typeName}",
                   ExpectsClone: false);
    }

    /// <summary>
    /// Generates a combined scenario from all variants.
    /// </summary>
    private static Gen<OwnershipProgram> GenOwnershipProgram()
    {
        return Gen.OneOf(
            GenOwnedTypeWithMove(),
            GenOwnedParameterPass(),
            GenOwnedReturnType(),
            GenImplicitCloneScenario(),
            GenOwnedTypeFieldAssignment(),
            GenLendingScenario());
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<OwnershipProgram> ArbitraryOwnershipProgram() =>
        GenOwnershipProgram().ToArbitrary();

    // ─── Forbidden patterns ──────────────────────────────────────────────

    /// <summary>
    /// Patterns that must NEVER appear in emitted C# — they indicate runtime
    /// ownership tracking, reference counting, state fields, or move-nulling.
    /// </summary>
    private static readonly string[] ForbiddenPatterns =
    [
        // The owned keyword must be erased (Req 14.3)
        "owned ",
        " owned",
        // Reference counting artifacts
        "RefCount",
        "refCount",
        "_refCount",
        "AddRef",
        "Release",
        // Ownership state fields
        "OwnershipState",
        "_ownershipState",
        "__ownership",
        "BorrowTracker",
        "__borrow",
        // Move-nulling patterns (Req 14.4 — no runtime move tracking)
        "__moved",
        "= null; // move",
        "MoveTracker",
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12a: Emitted C# contains no runtime ownership artifacts
    // **Validates: Requirements 14.1, 14.3, 14.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ZeroRuntimeOwnershipArtifactsPropertyTests) })]
    public Property EmittedCSharp_ContainsNoRuntimeOwnershipArtifacts(OwnershipProgram program)
    {
        var emittedFiles = CompileAndGetEmittedFiles(program.Source);

        if (emittedFiles is null)
            return false.ToProperty().Label($"Compilation failed for: {program.Label}");

        foreach (var (fileName, content) in emittedFiles)
        {
            // Skip .csproj files — they aren't emitted code
            if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var pattern in ForbiddenPatterns)
            {
                if (content.Contains(pattern, StringComparison.Ordinal))
                {
                    return false.ToProperty().Label(
                        $"File '{fileName}' contains forbidden artifact: '{pattern}'\n" +
                        $"Scenario: {program.Label}\n" +
                        $"Content snippet: ...{GetContextAround(content, pattern)}...");
                }
            }
        }

        return true.ToProperty().Label("No runtime ownership artifacts");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12b: .Clone() IS present where implicit cloning was resolved
    // **Validates: Requirements 14.1, 14.3, 14.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ZeroRuntimeOwnershipArtifactsPropertyTests) })]
    public Property EmittedCSharp_ContainsCloneAtResolvedCloneSites(OwnershipProgram program)
    {
        if (!program.ExpectsClone)
        {
            // For non-clone scenarios, just verify compilation succeeds and no artifacts
            var files = CompileAndGetEmittedFiles(program.Source);
            if (files is null)
                return false.ToProperty().Label($"Compilation failed for: {program.Label}");
            return true.ToProperty().Label("No clone expected — compilation succeeded");
        }

        // For clone scenarios, verify .Clone() appears in the emitted C#
        var emittedFiles = CompileAndGetEmittedFiles(program.Source);

        if (emittedFiles is null)
            return false.ToProperty().Label($"Compilation failed for: {program.Label}");

        var hasCloneCall = false;
        foreach (var (fileName, content) in emittedFiles)
        {
            if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;

            if (content.Contains(".Clone()", StringComparison.Ordinal))
            {
                hasCloneCall = true;
                break;
            }
        }

        return hasCloneCall.ToProperty().Label(
            hasCloneCall
                ? ".Clone() found in emitted C# at clone site"
                : $".Clone() NOT found in emitted C# — expected for: {program.Label}");
    }

    // ─── Helper ──────────────────────────────────────────────────────────

    private static string GetContextAround(string content, string pattern)
    {
        var idx = content.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0) return "(not found)";

        var start = Math.Max(0, idx - 40);
        var end = Math.Min(content.Length, idx + pattern.Length + 40);
        return content[start..end].Replace('\n', ' ').Replace('\r', ' ');
    }
}
