// Feature: struct-inheritance, Round-trip test for struct inheritance syntax
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Verifies the round-trip property for struct inheritance syntax:
/// parse → pretty-print → parse → pretty-print produces identical output.
///
/// Since the grammar and pretty printer already handle struct declarations with
/// base lists (the AST structure is unchanged), these tests confirm that
/// round-trip idempotence holds for all struct inheritance syntax variants.
///
/// All work runs in-process — no external process is spawned.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
/// </summary>
public class StructInheritanceRoundTripTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        var cu = Assert.IsType<CompilationUnitNode>(result.Root);
        return cu;
    }

    /// <summary>
    /// Asserts the full round-trip: source → AST₁ → print₁ → AST₂ → print₂,
    /// verifying print₁ == print₂ (round-trip idempotence).
    /// </summary>
    private static void AssertRoundTrip(string source)
    {
        // Step 1: Parse the original source
        var ast1 = ParseOrFail(source);

        // Step 2: Pretty-print AST₁ → print₁
        var printed1 = MistPrettyPrinter.Print(ast1, source);
        Assert.False(string.IsNullOrWhiteSpace(printed1),
            $"Pretty printer produced empty output for source:\n{source}");

        // Step 3: Parse print₁ → AST₂
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST₂ → print₂
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Assert print₁ == print₂ (idempotence)
        Assert.Equal(printed1, printed2);
    }

    /// <summary>
    /// Single base struct: struct Point3D : Point { int Z; }
    ///
    /// **Validates: Requirement 8.1**
    /// </summary>
    [Fact]
    public void RoundTrip_SingleBaseStruct()
    {
        const string source = """
            struct Point {
                int X;
                int Y;
            }
            struct Point3D : Point {
                int Z;
            }
            """;

        AssertRoundTrip(source);
    }

    /// <summary>
    /// Base struct + abstract types: struct Point3D : Point, IComparable { int Z; }
    ///
    /// **Validates: Requirement 8.2**
    /// </summary>
    [Fact]
    public void RoundTrip_BaseStructPlusAbstracts()
    {
        const string source = """
            abstract IComparable {
            }
            struct Point {
                int X;
                int Y;
            }
            struct Point3D : Point, IComparable {
                int Z;
            }
            """;

        AssertRoundTrip(source);
    }

    /// <summary>
    /// Base with type arguments: struct TypedPoint : Base&lt;int&gt; { }
    ///
    /// **Validates: Requirement 8.3**
    /// </summary>
    [Fact]
    public void RoundTrip_BaseWithTypeArguments()
    {
        const string source = """
            struct Base<T> {
                T Value;
            }
            struct TypedPoint : Base<int> {
            }
            """;

        AssertRoundTrip(source);
    }

    /// <summary>
    /// No base (control case): struct Point { int X; int Y; }
    ///
    /// **Validates: Requirement 8.5**
    /// </summary>
    [Fact]
    public void RoundTrip_NoBase()
    {
        const string source = """
            struct Point {
                int X;
                int Y;
            }
            """;

        AssertRoundTrip(source);
    }

    /// <summary>
    /// Multiple types in base list with type arguments on abstract types.
    ///
    /// **Validates: Requirements 8.2, 8.3**
    /// </summary>
    [Fact]
    public void RoundTrip_BaseStructWithGenericAbstract()
    {
        const string source = """
            abstract IEquatable<T> {
            }
            struct Point {
                int X;
                int Y;
            }
            struct Point3D : Point, IEquatable<Point3D> {
                int Z;
            }
            """;

        AssertRoundTrip(source);
    }

    /// <summary>
    /// Struct with type parameters and a base struct.
    ///
    /// **Validates: Requirements 8.1, 8.3, 8.4**
    /// </summary>
    [Fact]
    public void RoundTrip_GenericDerivedStruct()
    {
        const string source = """
            struct Base<T> {
                T Value;
            }
            struct Derived<T> : Base<T> {
                T Extra;
            }
            """;

        AssertRoundTrip(source);
    }
}