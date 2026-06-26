// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that refinement predicates using property patterns
/// only reference readonly members. Mutable members in a refinement predicate
/// produce <c>MIST_TC_0100</c>.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
/// </summary>
public class RefinementReadonlyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles Mist source and returns all diagnostics from both the semantic model
    /// builder and the type checker, with the ReadonlyInferenceEngine run in between.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        // Run readonly inference before the type checker (mirrors CompilerDriver pipeline).
        ReadonlyInferenceEngine.RunInference(model);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1: Property pattern referencing readonly field — accepted
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropertyPattern_ReadonlyField_NoError()
    {
        const string source = """
            class Person {
                public readonly int Age;
                public Person(int age) { Age = age; }
            }
            type ValidPerson = Person { Age: >= 18 };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0100"));
    }

    [Fact]
    public void PropertyPattern_ReadonlyProperty_NoError()
    {
        const string source = """
            class Range {
                public readonly int Start;
                public readonly int End;
                public Range(int start, int end) { Start = start; End = end; }
            }
            type ValidRange = Range { Start: >= 0 };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0100"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.2: Property pattern referencing mutable field — error
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropertyPattern_MutableField_EmitsMIST_TC_0100()
    {
        const string source = """
            class Person {
                public int Age;
                public Person(int age) { Age = age; }
            }
            type ValidPerson = Person { Age: >= 18 };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var readonlyDiags = WithId(diagnostics, "MIST_TC_0100");
        _ = Assert.Single(readonlyDiags);
        Assert.Contains("ValidPerson", readonlyDiags[0].Message);
        Assert.Contains("Age", readonlyDiags[0].Message);
        Assert.Contains("readonly", readonlyDiags[0].Message);
    }

    [Fact]
    public void PropertyPattern_MultipleMutableFields_EmitsMultipleDiagnostics()
    {
        const string source = """
            class Point {
                public int X;
                public int Y;
                public Point(int x, int y) { X = x; Y = y; }
            }
            type ValidPoint = Point { X: >= 0, Y: >= 0 };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var readonlyDiags = WithId(diagnostics, "MIST_TC_0100");
        Assert.Equal(2, readonlyDiags.Length);
        Assert.Contains(readonlyDiags, d => d.Message.Contains("X"));
        Assert.Contains(readonlyDiags, d => d.Message.Contains("Y"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.3: Primitive base type with relational pattern — no check
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PrimitiveBaseType_RelationalPattern_NoReadonlyCheck()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0100"));
    }

    [Fact]
    public void PrimitiveBaseType_CompoundPattern_NoReadonlyCheck()
    {
        const string source = """
            type Percentage = int >= 0 and <= 100;
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0100"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.4: Nested property pattern with mutable inner field
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NestedPropertyPattern_MutableInnerField_EmitsMIST_TC_0100()
    {
        const string source = """
            class Inner {
                public int Value;
                public Inner(int v) { Value = v; }
            }
            class Outer {
                public readonly Inner Data;
                public Outer(Inner d) { Data = d; }
            }
            type ValidOuter = Outer { Data: { Value: >= 0 } };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var readonlyDiags = WithId(diagnostics, "MIST_TC_0100");
        Assert.NotEmpty(readonlyDiags);
        Assert.Contains(readonlyDiags, d => d.Message.Contains("Value"));
    }

    [Fact]
    public void NestedPropertyPattern_AllReadonly_NoError()
    {
        const string source = """
            class Inner {
                public readonly int Value;
                public Inner(int v) { Value = v; }
            }
            class Outer {
                public readonly Inner Data;
                public Outer(Inner d) { Data = d; }
            }
            type ValidOuter = Outer { Data: { Value: >= 0 } };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TC_0100"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mixed: readonly outer, mutable outer
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropertyPattern_MixedReadonlyAndMutable_OnlyMutableEmitsError()
    {
        const string source = """
            class Config {
                public readonly int Min;
                public int Max;
                public Config(int min, int max) { Min = min; Max = max; }
            }
            type ValidConfig = Config { Min: >= 0, Max: <= 100 };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var readonlyDiags = WithId(diagnostics, "MIST_TC_0100");
        _ = Assert.Single(readonlyDiags);
        Assert.Contains("Max", readonlyDiags[0].Message);
    }
}