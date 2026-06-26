// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Comprehensive diagnostic tests for refinement types. Verifies that each
/// refinement-related diagnostic contains the expected message content as
/// specified in Requirements 11.1–11.5.
///
/// Diagnostic ID mapping (design doc → actual):
/// - MIST_TC_0070 → MIST_TC_0097 (literal violating predicate)
/// - MIST_TC_0071 → MIST_TC_0098 (non-constant assignment)
/// - MIST_TC_0072 → MIST_TC_0072 (incompatible predicate, in SemanticModelBuilder)
/// - MIST_TC_0073 → MIST_TC_0100 (mutable member in predicate)
/// - MIST_TC_0074 → MIST_TC_0099 (non-provable subset relationship)
///
/// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5**
/// </summary>
public class RefinementDiagnosticTests
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
    // Requirement 11.1: MIST_TC_0097 — Literal violating refinement predicate
    // Message: "Value '{Value}' does not satisfy refinement '{Predicate}' on type '{TypeName}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_TC_0097_MessageContainsValue()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    AdultAge age = 7;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));

        Assert.Contains("7", diag.Message);
    }

    [Fact]
    public void MIST_TC_0097_MessageContainsPredicate()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    AdultAge age = 7;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));

        Assert.Contains(">= 18", diag.Message);
    }

    [Fact]
    public void MIST_TC_0097_MessageContainsTypeName()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    AdultAge age = 7;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));

        Assert.Contains("AdultAge", diag.Message);
    }

    [Fact]
    public void MIST_TC_0097_CompoundPredicate_MessageContainsAllParts()
    {
        const string source = """
            type Percentage = int >= 0 and <= 100;
            class Program {
                public static void Main() {
                    Percentage p = 200;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));

        // Message should contain the violating value
        Assert.Contains("200", diag.Message);
        // Message should contain the type name
        Assert.Contains("Percentage", diag.Message);
        // Message should reference the predicate
        Assert.Contains(">=", diag.Message);
        Assert.Contains("<=", diag.Message);
    }

    [Fact]
    public void MIST_TC_0097_SeverityIsError()
    {
        const string source = """
            type Positive = int >= 1;
            class Program {
                public static void Main() {
                    Positive x = 0;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0097"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.2: MIST_TC_0098 — Non-constant assignment
    // Message: "Cannot assign '{ExpressionType}' to '{RefinementTypeName}' without
    //           proving the refinement. Use pattern matching to validate at runtime"
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_TC_0098_MessageContainsExpressionType()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    int x = 25;
                    AdultAge age = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0098"));

        Assert.Contains("int", diag.Message);
    }

    [Fact]
    public void MIST_TC_0098_MessageContainsRefinementTypeName()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    int x = 25;
                    AdultAge age = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0098"));

        Assert.Contains("AdultAge", diag.Message);
    }

    [Fact]
    public void MIST_TC_0098_MessageContainsPatternMatchingSuggestion()
    {
        const string source = """
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    int x = 25;
                    AdultAge age = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0098"));

        Assert.Contains("pattern matching", diag.Message);
    }

    [Fact]
    public void MIST_TC_0098_SeverityIsError()
    {
        const string source = """
            type Percentage = int >= 0 and <= 100;
            class Program {
                public static void Main() {
                    int val = 50;
                    Percentage p = val;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0098"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void MIST_TC_0098_DifferentRefinementTypeName_AppearsInMessage()
    {
        const string source = """
            type Percentage = int >= 0 and <= 100;
            class Program {
                public static void Main() {
                    int val = 50;
                    Percentage p = val;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0098"));

        Assert.Contains("Percentage", diag.Message);
        Assert.Contains("int", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.3: MIST_TC_0072 — Incompatible predicate
    // Message: "Refinement predicate '{Predicate}' is not valid for base type
    //           '{BaseType}'. Relational patterns require a comparable type."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_TC_0072_MessageContainsPredicate()
    {
        const string source = "type Bad = string >= 18;";

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0072"));

        Assert.Contains(">= 18", diag.Message);
    }

    [Fact]
    public void MIST_TC_0072_MessageContainsBaseType()
    {
        const string source = "type Bad = string >= 18;";

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0072"));

        Assert.Contains("string", diag.Message);
    }

    [Fact]
    public void MIST_TC_0072_MessageContainsComparableTypeText()
    {
        const string source = "type Bad = string >= 18;";

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0072"));

        Assert.Contains("comparable type", diag.Message);
    }

    [Fact]
    public void MIST_TC_0072_SeverityIsError()
    {
        const string source = "type Bad = string >= 18;";

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0072"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void MIST_TC_0072_BoolBaseType_MessageContainsBool()
    {
        const string source = "type Bad = bool >= 1;";

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0072"));

        Assert.Contains("bool", diag.Message);
        Assert.Contains("comparable type", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.4: MIST_TC_0100 — Mutable member in predicate
    // Message: "Refinement predicate on type '{TypeName}' references mutable member
    //           '{MemberName}'. All members in a refinement predicate must be readonly."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_TC_0100_MessageContainsTypeName()
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
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0100"));

        Assert.Contains("ValidPerson", diag.Message);
    }

    [Fact]
    public void MIST_TC_0100_MessageContainsMemberName()
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
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0100"));

        Assert.Contains("Age", diag.Message);
    }

    [Fact]
    public void MIST_TC_0100_MessageContainsReadonlyText()
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
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0100"));

        Assert.Contains("readonly", diag.Message);
    }

    [Fact]
    public void MIST_TC_0100_SeverityIsError()
    {
        const string source = """
            class Config {
                public int Max;
                public Config(int max) { Max = max; }
            }
            type ValidConfig = Config { Max: <= 100 };
            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0100"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void MIST_TC_0100_MultipleMutableMembers_EachReported()
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
        var diags = WithId(diagnostics, "MIST_TC_0100");

        Assert.Equal(2, diags.Length);
        Assert.Contains(diags, d => d.Message.Contains("X"));
        Assert.Contains(diags, d => d.Message.Contains("Y"));
        Assert.All(diags, d => Assert.Contains("ValidPoint", d.Message));
        Assert.All(diags, d => Assert.Contains("readonly", d.Message));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 11.5: MIST_TC_0099 — Non-provable subset relationship
    // Message: "Cannot assign '{SourceType}' to '{TargetType}' because the compiler
    //           cannot prove that '{SourcePredicate}' implies '{TargetPredicate}'."
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_TC_0099_MessageContainsSourceType()
    {
        const string source = """
            type LargeAge = int >= 10;
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    LargeAge la = 20;
                    AdultAge aa = la;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0099"));

        Assert.Contains("LargeAge", diag.Message);
    }

    [Fact]
    public void MIST_TC_0099_MessageContainsTargetType()
    {
        const string source = """
            type LargeAge = int >= 10;
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    LargeAge la = 20;
                    AdultAge aa = la;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0099"));

        Assert.Contains("AdultAge", diag.Message);
    }

    [Fact]
    public void MIST_TC_0099_MessageContainsSourcePredicate()
    {
        const string source = """
            type LargeAge = int >= 10;
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    LargeAge la = 20;
                    AdultAge aa = la;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0099"));

        Assert.Contains(">= 10", diag.Message);
    }

    [Fact]
    public void MIST_TC_0099_MessageContainsTargetPredicate()
    {
        const string source = """
            type LargeAge = int >= 10;
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    LargeAge la = 20;
                    AdultAge aa = la;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0099"));

        Assert.Contains(">= 18", diag.Message);
    }

    [Fact]
    public void MIST_TC_0099_SeverityIsError()
    {
        const string source = """
            type LargeAge = int >= 10;
            type AdultAge = int >= 18;
            class Program {
                public static void Main() {
                    LargeAge la = 20;
                    AdultAge aa = la;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0099"));

        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void MIST_TC_0099_CompoundPredicates_MessageContainsBothPredicateDescriptions()
    {
        const string source = """
            type WideRange = int >= 0 and <= 200;
            type NarrowRange = int >= 50 and <= 60;
            class Program {
                public static void Main() {
                    WideRange w = 55;
                    NarrowRange n = w;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = Assert.Single(WithId(diagnostics, "MIST_TC_0099"));

        // Source predicate description
        Assert.Contains("WideRange", diag.Message);
        // Target predicate description
        Assert.Contains("NarrowRange", diag.Message);
    }
}