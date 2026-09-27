// Feature: definite-assignment
// Property 4: Bare-array stampability gate
// **Validates: Requirements 4.1, 4.2, 4.5, 4.6**

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
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for the bare-array stampability gate.
///
/// Property 4: <c>new T[size]</c> type-checks successfully if and only if
/// <c>T</c> is a primitive or a stampable struct; for every other <c>T</c>
/// it emits exactly one <c>MIST_ARRAY_REQUIRES_INITIALIZER</c>.
///
/// **Validates: Requirements 4.1, 4.2, 4.5, 4.6**
/// </summary>
public sealed class BareArrayStampabilityGatePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "mist_bare_array_pbt_" + Guid.NewGuid().ToString("N"));
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
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Describes the element type category for a bare array creation.
    /// </summary>
    public enum ElementTypeCategory
    {
        /// <summary>A primitive type (int, bool, char, float, double, etc.) — stampable.</summary>
        Primitive,

        /// <summary>A user-defined struct with only primitive fields — stampable.</summary>
        StampableStruct,

        /// <summary>A user-defined struct with a class-typed field — NOT stampable.</summary>
        StructWithClassField,

        /// <summary>A class type — NOT stampable (reference type).</summary>
        ClassType,

        /// <summary>String (reference type) — NOT stampable.</summary>
        StringType,
    }

    /// <summary>
    /// A generated scenario for testing the bare-array stampability gate.
    /// </summary>
    public sealed record BareArrayScenario(
        string Source,
        string ElementTypeName,
        ElementTypeCategory Category,
        bool ExpectStampable)
    {
        public override string ToString()
        {
            return $"new {ElementTypeName}[N] category={Category} expectStampable={ExpectStampable}";
        }
    }

#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "char", "float", "double", "long", "short", "byte"];

    private static readonly string[] StampableStructNames =
        ["Vector", "Coord", "Dims", "Pair", "Offset"];

    private static readonly string[] NonStampableStructNames =
        ["Container", "Holder", "Wrapper", "Box", "Cell"];

    private static readonly string[] ClassNames =
        ["Service", "Handler", "Manager", "Worker", "Agent"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<BareArrayScenario> GenBareArrayScenario()
    {
        return Gen.Elements(
            ElementTypeCategory.Primitive,
            ElementTypeCategory.StampableStruct,
            ElementTypeCategory.StructWithClassField,
            ElementTypeCategory.ClassType,
            ElementTypeCategory.StringType)
            .SelectMany(GenScenarioForCategory);
    }

    private static Gen<BareArrayScenario> GenScenarioForCategory(
        ElementTypeCategory category)
    {
        return category switch
        {
            ElementTypeCategory.Primitive => GenPrimitiveScenario(),
            ElementTypeCategory.StampableStruct => GenStampableStructScenario(),
            ElementTypeCategory.StructWithClassField => GenStructWithClassFieldScenario(),
            ElementTypeCategory.ClassType => GenClassTypeScenario(),
            ElementTypeCategory.StringType => Gen.Constant(GenStringScenario()),
            _ => Gen.Constant(GenStringScenario()),
        };
    }

    private static Gen<BareArrayScenario> GenPrimitiveScenario()
    {
        return from primType in Gen.Elements(PrimitiveTypes)
               from size in Gen.Choose(1, 10)
               let source = BuildPrimitiveSource(primType, size)
               select new BareArrayScenario(source, primType, ElementTypeCategory.Primitive, true);
    }

    private static Gen<BareArrayScenario> GenStampableStructScenario()
    {
        return from structName in Gen.Elements(StampableStructNames)
               from fieldCount in Gen.Choose(1, 4)
               from size in Gen.Choose(1, 10)
               let source = BuildStampableStructSource(structName, fieldCount, size)
               select new BareArrayScenario(source, structName, ElementTypeCategory.StampableStruct, true);
    }

    private static Gen<BareArrayScenario> GenStructWithClassFieldScenario()
    {
        return from structName in Gen.Elements(NonStampableStructNames)
               from size in Gen.Choose(1, 10)
               let source = BuildStructWithClassFieldSource(structName, size)
               select new BareArrayScenario(source, structName, ElementTypeCategory.StructWithClassField, false);
    }

    private static Gen<BareArrayScenario> GenClassTypeScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from size in Gen.Choose(1, 10)
               let source = BuildClassTypeSource(className, size)
               select new BareArrayScenario(source, className, ElementTypeCategory.ClassType, false);
    }

    private static BareArrayScenario GenStringScenario()
    {
        var source = BuildStringSource(3);
        return new BareArrayScenario(source, "string", ElementTypeCategory.StringType, false);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<BareArrayScenario> ArbitraryBareArrayScenario()
    {
        return GenBareArrayScenario().ToArbitrary();
    }

    // ─── Source generation ───────────────────────────────────────────────

    private static string BuildPrimitiveSource(string primitiveType, int size)
    {
        return $$"""
            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    var arr = new {{primitiveType}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildStampableStructSource(string structName, int fieldCount, int size)
    {
        var fields = new List<string>();
        var primitives = PrimitiveTypes;
        for(var i = 0; i < fieldCount; i++)
        {
            fields.Add($"    public {primitives[i % primitives.Length]} field{i};");
        }

        return $$"""
            namespace TestProgram;

            public struct {{structName}}
            {
            {{string.Join("\n", fields)}}
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    var arr = new {{structName}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildStructWithClassFieldSource(string structName, int size)
    {
        return $$"""
            namespace TestProgram;

            public class Dependency
            {
                public int id;
            }

            public struct {{structName}}
            {
                public int value;
                public Dependency dep;
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    var arr = new {{structName}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildClassTypeSource(string className, int size)
    {
        return $$"""
            namespace TestProgram;

            public class {{className}}
            {
                public int id;
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    var arr = new {{className}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildStringSource(int size)
    {
        return $$"""
            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    var arr = new string[{{size}}];
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Bare-array stampability gate
    //
    // **Validates: Requirements 4.1, 4.2, 4.5, 4.6**
    //
    // `new T[size]` type-checks successfully if and only if `T` is a
    // primitive or a stampable struct; for every other `T` it emits exactly
    // one MIST_ARRAY_REQUIRES_INITIALIZER.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 4: The bare-array creation <c>new T[size]</c> succeeds if and
    /// only if <c>T</c> is stampable (primitive or stampable struct). For non-stampable
    /// types (class, string, struct with class field), exactly one
    /// <c>MIST_ARRAY_REQUIRES_INITIALIZER</c> diagnostic is emitted.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.5, 4.6**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(BareArrayStampabilityGatePropertyTests) },
        MaxTest = 100)]
    public Property BareArrayGate_AcceptsIffStampable(BareArrayScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // Filter out unrelated diagnostics (entry point, etc.)
        var arrayDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_ARRAY_REQUIRES_INITIALIZER")
            .ToList();

        // Filter errors excluding entry-point and the array gate itself
        var otherErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017")
            .Where(d => d.Id.Value != "MIST_ARRAY_REQUIRES_INITIALIZER")
            .ToList();

        if(scenario.ExpectStampable)
        {
            // Stampable: should compile without the array diagnostic
            // and without other errors
            var success = arrayDiagnostics.Count == 0 && otherErrors.Count == 0;
            return success
                .ToProperty()
                .Label(
                    $"Expected stampable (no diagnostic) but got " +
                    $"{arrayDiagnostics.Count} array diagnostic(s) and " +
                    $"{otherErrors.Count} other error(s). " +
                    $"Scenario: {scenario}. " +
                    $"Diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
        else
        {
            // Not stampable: should emit exactly one MIST_ARRAY_REQUIRES_INITIALIZER
            var success = arrayDiagnostics.Count == 1;
            return success
                .ToProperty()
                .Label(
                    $"Expected exactly 1 MIST_ARRAY_REQUIRES_INITIALIZER but got " +
                    $"{arrayDiagnostics.Count}. " +
                    $"Scenario: {scenario}. " +
                    $"All diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
    }
}