// Feature: ownership-system, Property 9: Emission erasure

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for emission erasure.
///
/// Property 9: Emission erasure
/// For any valid Mist program with ownership annotations, the emitted C# SHALL be
/// identical to the C# emitted for the same program with all ownership annotations
/// removed. No runtime ownership tracking, reference counting, state fields, or
/// runtime assertions SHALL be present in the output.
///
/// **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5**
/// </summary>
public class OwnershipErasurePropertyTests
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

    private static IReadOnlyDictionary<string, string>? CompileAndGetEmittedFiles(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
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

            var result = SharedDriver.Compile(options);

            if(!result.Succeeded)
            {
                return null;
            }

            return result.EmittedFiles;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A pair of programs: one with ownership annotations and one without.
    /// Both should compile successfully and produce identical emitted C#.
    /// </summary>
    public sealed record ErasureScenario(string WithOwnership, string WithoutOwnership, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Container", "Buffer", "Matrix", "Holder", "Wrapper", "Storage"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "value", "item", "content"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "instance", "item"];

    private static readonly string[] MethodNames =
        ["Process", "Transform", "Create", "Build", "Compute", "Handle"];

    private static readonly string[] ParamNames =
        ["input", "source", "target", "arg", "param", "element"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a program with an owned variable declaration and its
    /// equivalent without the ownership annotation.
    /// </summary>
    private static Gen<ErasureScenario> GenOwnedVariableScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let withOwnership = $$"""
                   owned class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} {{varName}} = new {{typeName}}();
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                           {{typeName}} {{varName}} = new {{typeName}}();
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"owned variable: owned {typeName} {varName}");
    }

    /// <summary>
    /// Generates a program with an owned parameter and its equivalent without.
    /// </summary>
    private static Gen<ErasureScenario> GenOwnedParameterScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let withOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       void {{methodName}}(owned {{typeName}} {{paramName}}) {
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       void {{methodName}}({{typeName}} {{paramName}}) {
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"owned param: void {methodName}(owned {typeName} {paramName})");
    }

    /// <summary>
    /// Generates a program with a frozen parameter and its equivalent without.
    /// </summary>
    private static Gen<ErasureScenario> GenFrozenRefParameterScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let withOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       void {{methodName}}(frozen {{typeName}} {{paramName}}) {
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       void {{methodName}}({{typeName}} {{paramName}}) {
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"frozen param: void {methodName}(frozen {typeName} {paramName})");
    }

    /// <summary>
    /// Generates a program with a borrowed parameter and its equivalent without.
    /// </summary>
    private static Gen<ErasureScenario> GenRefParameterScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let withOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       void {{methodName}}(borrowed {{typeName}} {{paramName}}) {
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       void {{methodName}}({{typeName}} {{paramName}}) {
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"borrowed param: void {methodName}(borrowed {typeName} {paramName})");
    }

    /// <summary>
    /// Generates a program with an owned return type and its equivalent without.
    /// </summary>
    private static Gen<ErasureScenario> GenOwnedReturnTypeScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               let withOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       owned {{typeName}} {{methodName}}() {
                           return new {{typeName}}();
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                       }
                       {{typeName}} {{methodName}}() {
                           return new {{typeName}}();
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"owned return: owned {typeName} {methodName}()");
    }

    /// <summary>
    /// Generates a program with a field of an owned type (no owned-field modifier — v2
    /// removed it) and its equivalent without the owned type modifier. This verifies that
    /// the <c>owned</c> type keyword is erased in emitted C# while fields remain plain.
    /// </summary>
    private static Gen<ErasureScenario> GenOwnedFieldScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let withOwnership = $$"""
                   owned class {{typeName}} {
                       int amount;
                   }
                   class Holder {
                       {{typeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Holder {
                       {{typeName}} {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"owned type field: owned class {typeName}, field {fieldName}");
    }

    /// <summary>
    /// Generates a program with a move (owned-to-owned assignment) and its
    /// equivalent without ownership annotations.
    /// </summary>
    private static Gen<ErasureScenario> GenMoveScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from var1 in Gen.Elements(new[] { "a", "b", "c" })
               from var2 in Gen.Elements(new[] { "d", "e", "f" })
               let withOwnership = $$"""
                   owned class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} {{var1}} = new {{typeName}}();
                           owned {{typeName}} {{var2}} = {{var1}};
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int amount;
                   }
                   class Program {
                       public static void Main() {
                           {{typeName}} {{var1}} = new {{typeName}}();
                           {{typeName}} {{var2}} = {{var1}};
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"move: owned {typeName} {var1} → owned {typeName} {var2}");
    }

    /// <summary>
    /// Generates a combined scenario: owned type declaration with owned fields,
    /// and its equivalent without the owned modifiers.
    /// Note: owned types require all ref fields to be owned, so the "without"
    /// version just uses a regular class with regular fields.
    /// </summary>
    private static Gen<ErasureScenario> GenOwnedTypeScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let withOwnership = $$"""
                   owned class {{typeName}} {
                       int {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                           owned {{typeName}} x = new {{typeName}}();
                       }
                   }
                   """
               let withoutOwnership = $$"""
                   class {{typeName}} {
                       int {{fieldName}};
                   }
                   class Program {
                       public static void Main() {
                           {{typeName}} x = new {{typeName}}();
                       }
                   }
                   """
               select new ErasureScenario(withOwnership, withoutOwnership,
                   $"owned type: owned class {typeName}");
    }

    /// <summary>
    /// Generates a combined scenario from all variants.
    /// Note: GenReadonlyRefParameterScenario is excluded because `readonly ref`
    /// is now a ref-parameter modifier (emitting `ref readonly` in C#), not a
    /// pure ownership annotation that gets erased.
    /// </summary>
    private static Gen<ErasureScenario> GenErasureScenario()
    {
        return Gen.OneOf(
            GenOwnedVariableScenario(),
            GenOwnedParameterScenario(),
            GenFrozenRefParameterScenario(),
            GenRefParameterScenario(),
            GenOwnedReturnTypeScenario(),
            GenOwnedFieldScenario(),
            GenMoveScenario(),
            GenOwnedTypeScenario());
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<ErasureScenario> ArbitraryErasureScenario()
    {
        return GenErasureScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Emission erasure — emitted C# is identical with and
    // without ownership annotations
    // **Validates: Requirements 17.1, 17.2, 17.3, 17.4, 17.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipErasurePropertyTests) })]
    public Property EmittedCSharp_IsIdentical_WithAndWithoutOwnershipAnnotations(ErasureScenario scenario)
    {
        var withFiles = CompileAndGetEmittedFiles(scenario.WithOwnership);
        var withoutFiles = CompileAndGetEmittedFiles(scenario.WithoutOwnership);

        // Both must compile successfully
        if(withFiles is null || withoutFiles is null)
        {
            return false.Label(
                $"Compilation failed. With ownership: {(withFiles is null ? "FAILED" : "OK")}, " +
                $"Without ownership: {(withoutFiles is null ? "FAILED" : "OK")}");
        }

        // Same number of emitted files
        if(withFiles.Count != withoutFiles.Count)
        {
            return false.Label(
                $"File count mismatch: with={withFiles.Count}, without={withoutFiles.Count}");
        }

        // Compare each emitted file's content
        foreach(var (fileName, withContent) in withFiles)
        {
            if(!withoutFiles.TryGetValue(fileName, out var withoutContent))
            {
                return false.Label($"File '{fileName}' present with ownership but missing without");
            }

            if(withContent != withoutContent)
            {
                return false.Label(
                    $"File '{fileName}' differs.\n" +
                    $"--- With ownership ---\n{withContent}\n" +
                    $"--- Without ownership ---\n{withoutContent}");
            }
        }

        return true.Label("Emitted C# is identical");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9b: No runtime tracking artifacts in emitted output
    // **Validates: Requirements 17.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipErasurePropertyTests) })]
    public Property EmittedCSharp_ContainsNoRuntimeOwnershipTracking(ErasureScenario scenario)
    {
        var emittedFiles = CompileAndGetEmittedFiles(scenario.WithOwnership);

        if(emittedFiles is null)
        {
            return false.Label("Compilation failed");
        }

        // Check that no emitted file contains ownership runtime artifacts
        var forbiddenPatterns = new[]
        {
            "OwnershipState",
            "BorrowTracker",
            "MoveTracker",
            "RefCount",
            "__ownership",
            "__borrow",
            "__moved",
            "Debug.Assert", // No runtime assertions for ownership
        };

        foreach(var (fileName, content) in emittedFiles)
        {
            // Skip .csproj files
            if(fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach(var pattern in forbiddenPatterns)
            {
                if(content.Contains(pattern, StringComparison.Ordinal))
                {
                    return false.Label(
                        $"File '{fileName}' contains forbidden runtime artifact: '{pattern}'");
                }
            }
        }

        return true.Label("No runtime ownership tracking found");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9c: Ownership keywords are not present in emitted C#
    // **Validates: Requirements 17.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipErasurePropertyTests) })]
    public Property EmittedCSharp_DoesNotContainOwnershipKeywords(ErasureScenario scenario)
    {
        var emittedFiles = CompileAndGetEmittedFiles(scenario.WithOwnership);

        if(emittedFiles is null)
        {
            return false.Label("Compilation failed");
        }

        // Ownership keywords that must not appear in emitted C#.
        // Note: "ref" is a valid C# keyword so we check for the Mist-specific
        // keywords "owned", "frozen", and "borrowed" which are not C# keywords.
        var forbiddenKeywords = new[]
        {
            "owned ",
            "frozen ",
            "borrowed ",
            "readonly ref ", // as a Mist ownership specifier (C# has "ref readonly" but not "readonly ref")
        };

        foreach(var (fileName, content) in emittedFiles)
        {
            if(fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach(var keyword in forbiddenKeywords)
            {
                if(content.Contains(keyword, StringComparison.Ordinal))
                {
                    return false.Label(
                        $"File '{fileName}' contains ownership keyword: '{keyword}'");
                }
            }
        }

        return true.Label("No ownership keywords in emitted C#");
    }
}