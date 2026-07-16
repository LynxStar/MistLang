// Feature: struct-inheritance + full-accessibility-model
// Struct-to-class lowering: structs that participate in inheritance are lowered to C# classes.
// **Validates: Requirements 15.4, 15.5 (full-accessibility-model)**

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Emitter snapshot tests for struct inheritance verifying the struct-to-class lowering:
/// - Structs with a base struct are lowered to C# classes with normal inheritance
/// - Structs that are the base of another struct are also lowered to C# classes
/// - Constructor `: base(args)` emits as normal C# `: base(args)` on the lowered class
/// - Plain structs (no inheritance) remain as C# structs
/// - Generic struct inheritance emits correct class declarations with type arguments
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
///
/// **Validates: Requirements 15.4, 15.5**
/// </summary>
public sealed class StructInheritanceEmitterTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    private static string CompileAndGetEmittedCSharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(tcErrors);

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return sb.ToString();
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that single-level struct inheritance lowers to C# classes with
    /// normal inheritance. The base struct becomes a C# class, and the derived
    /// struct becomes a C# class that extends it (`: Point`).
    ///
    /// **Validates: Requirements 15.4, 15.5**
    /// </summary>
    [Fact]
    public void SingleLevelInheritance_EmitsFlattenedFieldsAndImplicitOperator()
    {
        var mistSource = """
            struct Point {
                public int X;
                public int Y;
            }
            struct Point3D : Point {
                public int Z;
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // Both structs are lowered to classes due to inheritance participation.
        // Point3D extends Point via normal C# inheritance.
        Assert.Contains("class Point3D", emittedCSharp);
        Assert.Contains("class Point", emittedCSharp);
        Assert.Contains(": Point", emittedCSharp);

        // X and Y are declared on Point (base class), Z on Point3D.
        Assert.Contains("int X;", emittedCSharp);
        Assert.Contains("int Y;", emittedCSharp);
        Assert.Contains("int Z;", emittedCSharp);
    }

    /// <summary>
    /// Verifies that multi-level struct inheritance (C : B : A) lowers all structs
    /// to C# classes with a proper inheritance chain.
    ///
    /// **Validates: Requirements 15.4, 15.5**
    /// </summary>
    [Fact]
    public void MultiLevelInheritance_EmitsAllAncestorFieldsAndOperators()
    {
        var mistSource = """
            struct A {
                public int X;
            }
            struct B : A {
                public int Y;
            }
            struct C : B {
                public int Z;
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // All three structs are lowered to classes.
        Assert.Contains("class A", emittedCSharp);
        Assert.Contains("class B", emittedCSharp);
        Assert.Contains("class C", emittedCSharp);

        // B extends A, C extends B (normal C# inheritance).
        Assert.Contains(": A", emittedCSharp);
        Assert.Contains(": B", emittedCSharp);

        // Fields are declared on their respective classes.
        Assert.Contains("int X;", emittedCSharp);
        Assert.Contains("int Y;", emittedCSharp);
        Assert.Contains("int Z;", emittedCSharp);
    }

    /// <summary>
    /// Verifies that a derived struct constructor with `: base(args)` emits as
    /// a normal C# `: base(args)` call on the lowered class (not inlined assignments).
    ///
    /// **Validates: Requirement 15.4**
    /// </summary>
    [Fact]
    public void ConstructorWithBaseChain_EmitsInlinedFieldAssignments()
    {
        var mistSource = """
            struct Point {
                public int X;
                public int Y;
                Point(int x, int y) {
                    X = x;
                    Y = y;
                }
            }
            struct Point3D : Point {
                public int Z;
                Point3D(int x, int y, int z) : base(x, y) {
                    Z = z;
                }
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // When lowered to a class, `: base(x, y)` is emitted normally.
        Assert.Contains(": base(x, y)", emittedCSharp);

        // The derived class's own body assignment is emitted as-is.
        Assert.Contains("Z = z;", emittedCSharp);
    }

    /// <summary>
    /// Verifies that a plain struct (no base, not a base of any other struct, no protected
    /// members) is emitted as a C# struct (not lowered to a class).
    ///
    /// **Validates: Requirements 15.4, 15.5**
    /// </summary>
    [Fact]
    public void NoBaseStructInHeader_EmittedStructHasNoInheritanceClause()
    {
        var mistSource = """
            struct Plain {
                public int X;
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // A plain struct with no inheritance remains a C# struct.
        Assert.Contains("struct Plain", emittedCSharp);
        Assert.DoesNotContain("class Plain", emittedCSharp);
    }

    /// <summary>
    /// Verifies that generic struct inheritance lowers to C# classes with correct
    /// type parameters and inheritance.
    ///
    /// **Validates: Requirements 15.4, 15.5**
    /// </summary>
    [Fact]
    public void GenericStructInheritance_EmitsCorrectTypeArgumentsInOperator()
    {
        var mistSource = """
            struct Container<T> {
                public T Value;
            }
            struct ExtendedContainer<T> : Container<T> {
                public int Extra;
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // Both structs are lowered to classes due to inheritance participation.
        // They have no explicit access modifier, so they default to internal.
        Assert.Contains("class ExtendedContainer<T>", emittedCSharp);
        Assert.Contains("class Container<T>", emittedCSharp);

        // The derived class extends the base class with proper type arguments.
        Assert.Contains(": Container<T>", emittedCSharp);

        // The derived class should contain its own Extra field.
        Assert.Contains("int Extra;", emittedCSharp);
    }
}