// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying diagnostic messages for union type errors and warnings.
/// Each test compiles a minimal Mist program that triggers a specific diagnostic
/// and asserts the expected diagnostic ID and message content.
/// </summary>
public class UnionDiagnosticsTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles Mist source and returns all diagnostics from both the semantic model
    /// builder and the type checker (to capture both MIST_TC_0075 from the builder
    /// and MIST_TC_0070–0074 from the type checker).
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
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

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        // Combine semantic model builder diagnostics and type checker diagnostics.
        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0070: Non-constituent assigned to union
    // Requirement 12.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NonConstituentAssignedToUnion_EmitsMIST_TC_0070()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int | string x = true;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0070");
        Assert.Contains("bool", diag.Message);
        Assert.Contains("int | string", diag.Message);
        Assert.Contains("not a constituent", diag.Message);
    }

    [Fact]
    public void NonConstituentAssignedToUnion_WithClassType_EmitsMIST_TC_0070()
    {
        const string source = """
            class Cat { }
            class Dog { }
            class Fish { }
            class Program {
                public static void Main() {
                    Cat | Dog x = new Fish();
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0070");
        Assert.Contains("Fish", diag.Message);
        Assert.Contains("Cat | Dog", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0071: Union assigned to non-union without narrowing
    // Requirement 12.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnionAssignedToNonUnion_EmitsMIST_TC_0071()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int | string x = 42;
                    int y = x;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0071");
        Assert.Contains("int | string", diag.Message);
        Assert.Contains("int", diag.Message);
        Assert.Contains("narrow", diag.Message);
    }

    [Fact]
    public void UnionAssignedToNonUnion_WithClassTypes_EmitsMIST_TC_0071()
    {
        const string source = """
            class Cat { }
            class Dog { }
            class Program {
                public static void Main() {
                    Cat | Dog x = new Cat();
                    Cat y = x;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0071");
        Assert.Contains("Cat | Dog", diag.Message);
        Assert.Contains("Cat", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0072: Union-to-union with incompatible constituents
    // Requirement 12.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnionToUnionIncompatible_EmitsMIST_TC_0072()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int | string | bool x = 42;
                    int | string y = x;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0072");
        Assert.Contains("bool", diag.Message);
        Assert.Contains("not present in the target union", diag.Message);
    }

    [Fact]
    public void UnionToUnionIncompatible_MultipleExtraConstituents_EmitsMIST_TC_0072()
    {
        const string source = """
            class Cat { }
            class Dog { }
            class Fish { }
            class Program {
                public static void Main() {
                    Cat | Dog | Fish x = new Cat();
                    Cat y2 = 0;
                    Cat | Dog y = x;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0072" && d.Message.Contains("Fish"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0073: Member not present on all constituents
    // Requirement 12.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MemberNotOnAllConstituents_EmitsMIST_TC_0073()
    {
        const string source = """
            class Cat {
                public int Meow() { return 1; }
            }
            class Dog {
                public int Bark() { return 2; }
            }
            class Program {
                public static void Main() {
                    Cat | Dog x = new Cat();
                    int r = x.Meow();
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0073");
        Assert.Contains("Meow", diag.Message);
        Assert.Contains("Dog", diag.Message);
    }

    [Fact]
    public void MemberNotOnAllConstituents_MultipleClassesMissing_EmitsMIST_TC_0073()
    {
        const string source = """
            class A {
                public int Shared() { return 1; }
            }
            class B { }
            class C { }
            class Program {
                public static void Main() {
                    A | B | C x = new A();
                    int r = x.Shared();
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0073");
        Assert.Contains("Shared", diag.Message);
        Assert.Contains("B", diag.Message);
        Assert.Contains("C", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0074: Pattern type not a constituent (warning)
    // Requirement 12.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PatternTypeNotConstituent_EmitsMIST_TC_0074_Warning()
    {
        const string source = """
            class Cat { }
            class Dog { }
            class Fish { }
            class Program {
                public static void Main() {
                    Cat | Dog x = new Cat();
                    if (x is Fish f) {
                    }
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0074");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("Fish", diag.Message);
        Assert.Contains("Cat | Dog", diag.Message);
        Assert.Contains("never match", diag.Message);
    }

    [Fact]
    public void PatternTypeNotConstituent_WithPrimitiveUnion_EmitsMIST_TC_0074_Warning()
    {
        const string source = """
            class Unrelated { }
            class Program {
                public static void Main() {
                    int | string x = 42;
                    if (x is Unrelated u) {
                    }
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0074");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("Unrelated", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0075: Degenerate union (warning)
    // Requirement 12.1
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DegenerateUnion_DuplicateTypes_EmitsMIST_TC_0075_Warning()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int | int x = 42;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0075");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("fewer than 2 distinct types", diag.Message);
    }

    [Fact]
    public void DegenerateUnion_TripleDuplicate_EmitsMIST_TC_0075_Warning()
    {
        const string source = """
            class Program {
                public static void Main() {
                    string | string | string x = "hi";
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0075");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("fewer than 2 distinct types", diag.Message);
        Assert.Contains("string", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Unresolved constituent type (MIST_UNDEFINED_TYPE)
    // Requirement 12.6
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnresolvedConstituentType_EmitsMIST_UNDEFINED_TYPE()
    {
        const string source = """
            class Program {
                public static void Main() {
                    int | NonExistentType x = 42;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" &&
            d.Message.Contains("NonExistentType"));
    }

    [Fact]
    public void UnresolvedConstituentType_MultipleUnresolved_EmitsDiagnosticsForEach()
    {
        const string source = """
            class Program {
                public static void Main() {
                    Foo | Bar x = 0;
                }
            }
            """;

        var diagnostics = CompileAndGetAllDiagnostics(source);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" &&
            d.Message.Contains("Foo"));
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" &&
            d.Message.Contains("Bar"));
    }
}