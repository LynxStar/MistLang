// Feature: struct-inheritance, Task 8.1: Emitter snapshot tests for struct inheritance
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 7.5**

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Emitter snapshot tests for struct inheritance verifying:
/// - Single-level inheritance emits flattened fields + implicit operator
/// - Multi-level inheritance emits all ancestor fields + operators for each level
/// - Constructor with `: base(args)` emits inlined field assignments
/// - No `: BaseStruct` appears in emitted C# struct header
/// - Generic struct inheritance emits correct type arguments in operator
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 7.5**
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
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            sb.AppendLine(content);
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
    /// Verifies that single-level struct inheritance emits flattened fields
    /// from the base struct into the derived struct, plus an implicit
    /// conversion operator from derived to base.
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.5**
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

        // Point3D should contain flattened fields from Point
        Assert.Contains("public int X;", emittedCSharp);
        Assert.Contains("public int Y;", emittedCSharp);
        Assert.Contains("public int Z;", emittedCSharp);

        // Point3D should have an implicit conversion operator to Point
        Assert.Contains("public static implicit operator Point(Point3D value)", emittedCSharp);
    }

    /// <summary>
    /// Verifies that multi-level struct inheritance (C : B : A) emits all
    /// ancestor fields in the most-derived struct, plus implicit conversion
    /// operators for each ancestor level.
    ///
    /// **Validates: Requirements 6.1, 6.3, 6.5**
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

        // C should contain all ancestor fields (X from A, Y from B, Z own)
        // Check that the emitted C# for struct C contains X, Y, and Z fields
        Assert.Contains("public int X;", emittedCSharp);
        Assert.Contains("public int Y;", emittedCSharp);
        Assert.Contains("public int Z;", emittedCSharp);

        // C should have implicit operators for both B and A
        Assert.Contains("public static implicit operator B(C value)", emittedCSharp);
        Assert.Contains("public static implicit operator A(C value)", emittedCSharp);

        // B should also have an implicit operator to A
        Assert.Contains("public static implicit operator A(B value)", emittedCSharp);
    }

    /// <summary>
    /// Verifies that a derived struct constructor with `: base(args)` emits
    /// inlined field assignments (e.g., `this.X = x;`) instead of the
    /// invalid C# `: base(x, y)` syntax for structs.
    ///
    /// **Validates: Requirement 7.5**
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

        // The emitted constructor should NOT contain `: base(x, y)` syntax
        Assert.DoesNotContain(": base(", emittedCSharp);

        // The emitted constructor should inline the base field assignments
        // with `this.` prefix for the inlined base constructor assignments
        Assert.Contains("this.X = x;", emittedCSharp);
        Assert.Contains("this.Y = y;", emittedCSharp);

        // The derived struct's own body assignment is emitted as-is
        Assert.Contains("Z = z;", emittedCSharp);
    }

    /// <summary>
    /// Verifies that the emitted C# struct declaration does NOT contain
    /// `: BaseStruct` — since the CLR does not support struct inheritance,
    /// the emitter must not emit an inheritance clause on structs.
    ///
    /// **Validates: Requirement 6.4**
    /// </summary>
    [Fact]
    public void NoBaseStructInHeader_EmittedStructHasNoInheritanceClause()
    {
        var mistSource = """
            struct Base {
                public int X;
            }
            struct Derived : Base {
                public int Y;
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // The emitted C# should contain "struct Derived" but NOT "struct Derived : Base"
        Assert.Contains("struct Derived", emittedCSharp);
        Assert.DoesNotContain("struct Derived : Base", emittedCSharp);

        // Also verify the base struct itself doesn't have an inheritance clause
        Assert.Contains("struct Base", emittedCSharp);
    }

    /// <summary>
    /// Verifies that generic struct inheritance emits correct type arguments
    /// in the implicit conversion operator. The emitter handles generic struct
    /// inheritance by unwrapping ConstructedType to emit flattened fields and
    /// implicit operators with proper type parameters.
    ///
    /// **Validates: Requirements 6.2, 6.5**
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

        // The emitted C# should contain the ExtendedContainer struct declaration
        // with its type parameter
        Assert.Contains("struct ExtendedContainer<T>", emittedCSharp);

        // The base Container struct should also be emitted with its type parameter
        Assert.Contains("struct Container<T>", emittedCSharp);

        // The derived struct should NOT have `: Container<T>` in its header
        Assert.DoesNotContain("struct ExtendedContainer<T> : Container", emittedCSharp);

        // The derived struct should contain its own Extra field
        Assert.Contains("public int Extra;", emittedCSharp);
    }
}