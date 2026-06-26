// Feature: stack-only-types, Property 8: Element-type stampability gate (array parity)
//
// **Validates: Requirements 5.1, 5.2, 5.3**
//
// In-process, model-based against bare `new T[n]`: assert `stackalloc T[n]` is
// accepted iff `T` is `Stampable_Type` iff `new T[n]` is accepted.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Describes a paired stampability scenario: both a stackalloc program and a bare
/// array program using the same element type, to verify parity.
/// </summary>
public sealed record StampabilityParityScenario(
    string StackallocSource,
    string BareArraySource,
    string ElementTypeName,
    string Category,
    bool ExpectStampable)
{
    public override string ToString() =>
        $"T={ElementTypeName} category={Category} expectStampable={ExpectStampable}";
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for the element-type stampability gate (array parity).
///
/// Property 8: <c>stackalloc T[n]</c> is accepted if and only if <c>T</c> is a
/// <c>Stampable_Type</c> if and only if <c>new T[n]</c> is accepted. This is a
/// model-based property where <c>new T[n]</c> serves as the reference oracle.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 5.1, 5.2, 5.3**
/// </summary>
public sealed class StackallocStampabilityGateParityPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Diagnostic IDs ─────────────────────────────────────────────────

    /// <summary>
    /// Stackalloc stampability diagnostics (MIST_TC_0152..0155).
    /// </summary>
    private static readonly string[] StackallocStampabilityDiagIds =
        ["MIST_TC_0152", "MIST_TC_0153", "MIST_TC_0154", "MIST_TC_0155"];

    /// <summary>
    /// Bare array stampability diagnostic.
    /// </summary>
    private const string ArrayStampabilityDiagId = "MIST_ARRAY_REQUIRES_INITIALIZER";

    // ─── Element type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "char", "float", "double", "long", "short", "byte"];

    private static readonly string[] PureStructNames =
        ["Vector", "Coord", "Dims", "Pair", "Offset"];

    private static readonly string[] NonStampableStructNames =
        ["Container", "Holder", "Wrapper", "Box", "Cell"];

    private static readonly string[] ClassNames =
        ["Service", "Handler", "Manager", "Worker", "Agent"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where T is a primitive type (stampable).
    /// </summary>
    private static Gen<StampabilityParityScenario> GenPrimitiveScenario()
    {
        return from primType in Gen.Elements(PrimitiveTypes)
               from size in Gen.Choose(1, 10)
               let stackallocSource = BuildStackallocSource(primType, size, structDecl: null)
               let arraySource = BuildArraySource(primType, size, structDecl: null)
               select new StampabilityParityScenario(
                   stackallocSource, arraySource, primType, "Primitive", true);
    }

    /// <summary>
    /// Generates a scenario where T is a pure struct (all-primitive fields, stampable).
    /// </summary>
    private static Gen<StampabilityParityScenario> GenPureStructScenario()
    {
        return from structName in Gen.Elements(PureStructNames)
               from fieldCount in Gen.Choose(1, 4)
               from size in Gen.Choose(1, 10)
               let structDecl = BuildPureStructDecl(structName, fieldCount)
               let stackallocSource = BuildStackallocSource(structName, size, structDecl)
               let arraySource = BuildArraySource(structName, size, structDecl)
               select new StampabilityParityScenario(
                   stackallocSource, arraySource, structName, "PureStruct", true);
    }

    /// <summary>
    /// Generates a scenario where T is a struct with a reference-type field (not stampable).
    /// </summary>
    private static Gen<StampabilityParityScenario> GenStructWithClassFieldScenario()
    {
        return from structName in Gen.Elements(NonStampableStructNames)
               from size in Gen.Choose(1, 10)
               let structDecl = BuildNonStampableStructDecl(structName)
               let stackallocSource = BuildStackallocSource(structName, size, structDecl)
               let arraySource = BuildArraySource(structName, size, structDecl)
               select new StampabilityParityScenario(
                   stackallocSource, arraySource, structName, "StructWithClassField", false);
    }

    /// <summary>
    /// Generates a scenario where T is a class type (reference type, not stampable).
    /// </summary>
    private static Gen<StampabilityParityScenario> GenClassTypeScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from size in Gen.Choose(1, 10)
               let classDecl = BuildClassDecl(className)
               let stackallocSource = BuildStackallocSource(className, size, classDecl)
               let arraySource = BuildArraySource(className, size, classDecl)
               select new StampabilityParityScenario(
                   stackallocSource, arraySource, className, "ClassType", false);
    }

    /// <summary>
    /// Generates a scenario where T is string (reference type, not stampable).
    /// </summary>
    private static Gen<StampabilityParityScenario> GenStringScenario()
    {
        return from size in Gen.Choose(1, 10)
               let stackallocSource = BuildStackallocSource("string", size, structDecl: null)
               let arraySource = BuildArraySource("string", size, structDecl: null)
               select new StampabilityParityScenario(
                   stackallocSource, arraySource, "string", "StringType", false);
    }

    /// <summary>
    /// Combined generator covering all element type categories.
    /// </summary>
    public static Arbitrary<StampabilityParityScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenPrimitiveScenario(),
            GenPureStructScenario(),
            GenStructWithClassFieldScenario(),
            GenClassTypeScenario(),
            GenStringScenario()).ToArbitrary();
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildStackallocSource(string elementType, int size, string? structDecl)
    {
        var preamble = structDecl ?? "";
        return $$"""
            {{preamble}}
            class Program {
                static void Main() {
                    var s = stackalloc {{elementType}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildArraySource(string elementType, int size, string? structDecl)
    {
        var preamble = structDecl ?? "";
        return $$"""
            {{preamble}}
            class Program {
                static void Main() {
                    var a = new {{elementType}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildPureStructDecl(string structName, int fieldCount)
    {
        var fields = new List<string>();
        for(var i = 0; i < fieldCount; i++)
        {
            var fieldType = PrimitiveTypes[i % PrimitiveTypes.Length];
            fields.Add($"    public {fieldType} field{i};");
        }

        return $$"""
            public struct {{structName}} {
            {{string.Join("\n", fields)}}
            }
            """;
    }

    private static string BuildNonStampableStructDecl(string structName)
    {
        return $$"""
            public class Dependency {
                public int id;
            }

            public struct {{structName}} {
                public int value;
                public Dependency dep;
            }
            """;
    }

    private static string BuildClassDecl(string className)
    {
        return $$"""
            public class {{className}} {
                public int id;
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
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
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ─── Helper: check acceptance ────────────────────────────────────────

    /// <summary>
    /// Returns true if the stackalloc program was accepted w.r.t. the stampability gate
    /// (no MIST_TC_0152..0155 diagnostics emitted).
    /// </summary>
    private static bool IsStackallocAccepted(ImmutableArray<Diagnostic> diagnostics)
    {
        return !diagnostics.Any(d =>
            StackallocStampabilityDiagIds.Contains(d.Id.Value));
    }

    /// <summary>
    /// Returns true if the bare array program was accepted w.r.t. the stampability gate
    /// (no MIST_ARRAY_REQUIRES_INITIALIZER diagnostic emitted).
    /// </summary>
    private static bool IsArrayAccepted(ImmutableArray<Diagnostic> diagnostics)
    {
        return !diagnostics.Any(d => d.Id.Value == ArrayStampabilityDiagId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Element-type stampability gate (array parity)
    //
    // **Validates: Requirements 5.1, 5.2, 5.3**
    //
    // stackalloc T[n] is accepted iff T is Stampable_Type iff new T[n] is accepted.
    // The bare-array acceptance serves as the model oracle.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 8: <c>stackalloc T[n]</c> is accepted (w.r.t. element type) if and only
    /// if <c>new T[n]</c> is accepted (w.r.t. element type). Both use the same
    /// <c>IsStampable</c> / <c>IsPrimitiveStructByName</c> gate.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocStampabilityGateParityPropertyTests) })]
    public Property StackallocStampabilityGate_ParityWithBareArray(
        StampabilityParityScenario scenario)
    {
        var stackallocDiags = CompileAndGetDiagnostics(scenario.StackallocSource);
        var arrayDiags = CompileAndGetDiagnostics(scenario.BareArraySource);

        var stackallocAccepted = IsStackallocAccepted(stackallocDiags);
        var arrayAccepted = IsArrayAccepted(arrayDiags);

        // Parity: both must agree.
        var parity = stackallocAccepted == arrayAccepted;

        // Also verify the expected stampability classification.
        var matchesExpected = stackallocAccepted == scenario.ExpectStampable;

        var passed = parity && matchesExpected;

        return passed.ToProperty()
            .Label(
                $"Parity violated for T={scenario.ElementTypeName} ({scenario.Category}).\n" +
                $"stackalloc accepted={stackallocAccepted}, new T[n] accepted={arrayAccepted}, " +
                $"expected stampable={scenario.ExpectStampable}.\n" +
                $"Stackalloc diags: [{string.Join("; ", stackallocDiags.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Array diags: [{string.Join("; ", arrayDiags.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Stackalloc source:\n{scenario.StackallocSource}\n" +
                $"Array source:\n{scenario.BareArraySource}");
    }
}