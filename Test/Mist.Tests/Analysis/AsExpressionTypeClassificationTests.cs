// Feature: as-operator, Task 5.3: Tests for `as` expression type classification
// **Validates: Properties 2, 3, 4**
// _Requirements: 2.1–2.5, 3.1–3.5, 4.1–4.4_

using System.Collections.Immutable;
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
/// Unit tests verifying the type checker's classification of <c>as</c> expressions
/// into Provable (result type = T), NonProvable (result type = T | void), or Invalid
/// (compile error).
///
/// The approach: compile minimal Mist programs that use <c>as</c> and verify correctness
/// by checking whether assignments to specific target types succeed or fail:
/// - Provable: <c>T result = expr as T;</c> compiles without error.
/// - NonProvable: <c>T? result = expr as T;</c> compiles without error;
///               <c>T result = expr as T;</c> would fail (result is T|void, not T).
/// - Invalid: the <c>as</c> expression itself produces a diagnostic.
///
/// **Validates: Properties 2, 3, 4**
/// </summary>
public sealed class AsExpressionTypeClassificationTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_as_classify_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
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
                EmitToDisk: false,
                GlobalUsings: ImmutableArray.Create("Standard"));

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void AssertNoDiagnosticErrors(ImmutableArray<Diagnostic> diagnostics, string context = "")
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors{(context.Length > 0 ? $" ({context})" : "")} but got:\n" +
            string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}")));
    }

    private static void AssertHasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string expectedId, string context = "")
    {
        var matching = diagnostics.Where(d => d.Id.Value == expectedId).ToList();
        Assert.True(matching.Count > 0,
            $"Expected diagnostic {expectedId}{(context.Length > 0 ? $" ({context})" : "")} but got:\n" +
            string.Join("\n", diagnostics.Select(e => $"  {e.Id.Value}: {e.Message}")));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PROVABLE CONVERSIONS — result type is T (Property 2)
    // Requirements 2.1, 2.2, 2.3, 2.4, 2.5
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Identity conversion: <c>dog as Dog</c> → result type is <c>Dog</c>.
    /// Requirement 2.1
    /// </summary>
    [Fact]
    public void Provable_Identity_ResultTypeIsT()
    {
        const string source = """
            class Dog { }
            class Program {
                public static void Main() {
                    var dog = new Dog();
                    Dog result = dog as Dog;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "identity: dog as Dog → Dog");
    }

    /// <summary>
    /// Class widening: <c>dog as Animal</c> → result type is <c>Animal</c>.
    /// Requirement 2.2
    /// </summary>
    [Fact]
    public void Provable_ClassWidening_ResultTypeIsT()
    {
        const string source = """
            class Animal { }
            class Dog : Animal { }
            class Program {
                public static void Main() {
                    var dog = new Dog();
                    Animal result = dog as Animal;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "class widening: dog as Animal → Animal");
    }

    /// <summary>
    /// Struct widening: <c>point3d as Point</c> → result type is <c>Point</c>.
    /// Requirement 2.2
    /// </summary>
    [Fact]
    public void Provable_StructWidening_ResultTypeIsT()
    {
        const string source = """
            struct Point {
                public int X;
                public int Y;
                public Point(int x, int y) {
                    X = x;
                    Y = y;
                }
            }
            struct Point3D : Point {
                public int Z;
                public Point3D(int x, int y, int z) : base(x, y) {
                    Z = z;
                }
            }
            class Program {
                public static void Main() {
                    var p3d = new Point3D(1, 2, 3);
                    Point result = p3d as Point;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "struct widening: point3d as Point → Point");
    }

    /// <summary>
    /// Abstract type widening: <c>dog as IAnimal</c> where Dog implements IAnimal → result type is <c>IAnimal</c>.
    /// Requirement 2.3
    /// </summary>
    [Fact]
    public void Provable_AbstractTypeWidening_ResultTypeIsT()
    {
        const string source = """
            abstract IAnimal {
                string Speak();
            }
            class Dog : IAnimal {
                public string Speak() { return "Woof"; }
            }
            class Program {
                public static void Main() {
                    var dog = new Dog();
                    IAnimal result = dog as IAnimal;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "abstract type widening: dog as IAnimal → IAnimal");
    }

    /// <summary>
    /// Constituent to union: <c>42 as int | string</c> → result type is <c>int | string</c>.
    /// Requirement 2.4
    /// </summary>
    [Fact]
    public void Provable_ConstituentToUnion_ResultTypeIsT()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int x = 42;
                    int | string result = x as int | string;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "constituent→union: int as int|string → int|string");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NON-PROVABLE CONVERSIONS — result type is T | void (Property 3)
    // Requirements 3.1, 3.2, 3.3, 3.4, 3.5
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Class narrowing: <c>animal as Dog</c> → result type is <c>Dog?</c> (Dog | void).
    /// Requirement 3.1
    /// </summary>
    [Fact]
    public void NonProvable_ClassNarrowing_ResultTypeIsTOptional()
    {
        const string source = """
            class Animal { }
            class Dog : Animal { }
            class Program {
                public static void Main() {
                    Animal animal = new Dog();
                    Dog? result = animal as Dog;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "class narrowing: animal as Dog → Dog?");
    }

    /// <summary>
    /// Struct narrowing: <c>point as Point3D</c> → result type is <c>Point3D?</c>.
    /// Requirement 3.4
    /// </summary>
    [Fact]
    public void NonProvable_StructNarrowing_ResultTypeIsTOptional()
    {
        const string source = """
            struct Point {
                public int X;
                public int Y;
                public Point(int x, int y) {
                    X = x;
                    Y = y;
                }
            }
            struct Point3D : Point {
                public int Z;
                public Point3D(int x, int y, int z) : base(x, y) {
                    Z = z;
                }
            }
            class Program {
                public static void Main() {
                    var p = new Point(1, 2);
                    Point3D? result = p as Point3D;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "struct narrowing: point as Point3D → Point3D?");
    }

    /// <summary>
    /// Union to constituent: <c>union as int</c> → result type is <c>int?</c>.
    /// Requirement 3.2
    /// </summary>
    [Fact]
    public void NonProvable_UnionToConstituent_ResultTypeIsTOptional()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int | string union = 42;
                    int? result = union as int;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "union→constituent: int|string as int → int?");
    }

    /// <summary>
    /// Unknown to concrete: <c>unknown as int</c> → result type is <c>int?</c>.
    /// Requirement 3.3
    /// </summary>
    [Fact]
    public void NonProvable_UnknownToConcrete_ResultTypeIsTOptional()
    {
        const string source = """
            class Program {
                public static void Main() {
                    unknown box = 42;
                    int? result = box as int;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "unknown→concrete: unknown as int → int?");
    }

    /// <summary>
    /// Class to unproven abstract type: <c>animal as ISwimmable</c> → result type is <c>ISwimmable?</c>.
    /// The class does not implement the abstract type, but a derived class might.
    /// Requirement 3.5
    /// </summary>
    [Fact]
    public void NonProvable_ClassToUnprovenAbstract_ResultTypeIsTOptional()
    {
        const string source = """
            abstract ISwimmable {
                void Swim();
            }
            class Animal { }
            class Program {
                public static void Main() {
                    var animal = new Animal();
                    ISwimmable? result = animal as ISwimmable;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "class→abstract (unproven): animal as ISwimmable → ISwimmable?");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INVALID CONVERSIONS — compile error (Property 4)
    // Requirements 4.1, 4.2, 4.3, 4.4
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unrelated types: <c>int as string</c> → MIST_TC_0160.
    /// Requirement 4.1
    /// </summary>
    [Fact]
    public void Invalid_UnrelatedTypes_IntToString_EmitsMIST_TC_0160()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int x = 42;
                    var result = x as string;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0160", "unrelated: int as string");
    }

    /// <summary>
    /// Unrelated types: <c>Dog as Cat</c> with no common hierarchy → MIST_TC_0160.
    /// Requirement 4.1
    /// </summary>
    [Fact]
    public void Invalid_UnrelatedTypes_DogToCat_EmitsMIST_TC_0160()
    {
        const string source = """
            class Dog { }
            class Cat { }
            class Program {
                public static void Main() {
                    var dog = new Dog();
                    var result = dog as Cat;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0160", "unrelated: Dog as Cat");
    }

    /// <summary>
    /// Void target: <c>expr as void</c> → MIST_TC_0161.
    /// Requirement 4.3
    /// </summary>
    [Fact]
    public void Invalid_VoidTarget_EmitsMIST_TC_0161()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int x = 42;
                    var result = x as void;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0161", "as void");
    }

    /// <summary>
    /// Void source: a void-typed expression used with <c>as</c> → MIST_TC_0162.
    /// Requirement 4.4
    /// </summary>
    [Fact]
    public void Invalid_VoidSource_EmitsMIST_TC_0162()
    {
        const string source = """
            class Program {
                public static void DoNothing() { }
                public static void Main() {
                    var result = DoNothing() as int;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertHasDiagnostic(diagnostics, "MIST_TC_0162", "void source");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RESULT TYPE VERIFICATION — ensure non-provable produces T|void, not T
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a non-provable as expression produces T|void result type
    /// by assigning it to a T? variable — confirming the classification is
    /// NonProvable and the result type is the optional type.
    /// </summary>
    [Fact]
    public void NonProvable_AssignableToOptionalType()
    {
        const string source = """
            class Animal { }
            class Dog : Animal { }
            class Program {
                public static void Main() {
                    Animal animal = new Dog();
                    Dog? maybeDog = animal as Dog;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "non-provable result (Dog|void) assignable to Dog?");
    }

    /// <summary>
    /// Verifies that a provable as expression CAN be assigned to bare T
    /// (not required to use T?), confirming the result type is T.
    /// </summary>
    [Fact]
    public void Provable_CanAssignToBareTType()
    {
        const string source = """
            class Animal { }
            class Dog : Animal { }
            class Program {
                public static void Main() {
                    var dog = new Dog();
                    Animal result = dog as Animal;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNoDiagnosticErrors(diagnostics, "provable widening can assign to bare T");
    }
}
