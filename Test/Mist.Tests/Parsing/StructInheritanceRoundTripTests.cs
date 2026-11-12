// Feature: struct-inheritance, Round-trip test for struct inheritance syntax
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class StructInheritanceRoundTripTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
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

    private static void AssertRoundTrip(string source)
    {
        var ast1 = ParseOrFail(source);

        var printed1 = MistPrettyPrinter.Print(ast1, source);
        Assert.False(string.IsNullOrWhiteSpace(printed1),
            $"Pretty printer produced empty output for source:\n{source}");

        var ast2 = ParseOrFail(printed1);

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

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