using System.Collections;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Parsing;

[Trait("Tier", "3")]
public sealed class GeneratedAstFidelityTests
{
    private const string Declarations = """
        class Plain {
            int Evaluate(int a, int b) {
                var values = [a + b * 2, a - b];
                (int first, int second, int third) = (a, b, 2);
                return values[0];
            }
            type NestedAlias = int;
            type NestedClone = new int;
        }
        class Derived : Plain, IOther { }
        enum Bare { A }
        enum Sized : byte { A, B }
        public abstract IHolder {
            type Loose;
            type Bound<TA, TB> : IStringable, IOther where TA : struct where TB : class;
        }
        type Alias = int;
        type Clone = new int;
        """;

    [Fact]
    public void EveryNodeANodeHoldsIsOneOfItsChildren_OnceAndInSourceOrder()
    {
        var root = Parse(Declarations);
        root.SetParentLinks();
        var origin = Assert.IsType<ParsedOrigin>(root.Origin);

        var pending = new Stack<SyntaxNode>([root]);
        while(pending.TryPop(out var node))
        {
            var children = node.Children().ToList();
            var held = Held(node).Distinct(ReferenceEqualityComparer.Instance).Cast<SyntaxNode>().ToList();

            Assert.DoesNotContain(null, children);
            Assert.Equal(held.OrderBy(child => child.Span.Start), children);
            Assert.All(held, child => Assert.Same(node, child.Parent));
            Assert.All(held, child => Assert.Same(origin, child.Origin));

            foreach(var child in held)
            {
                pending.Push(child);
            }
        }
    }

    [Fact]
    public void AnOptionalGroupsLabelHoldsTheNodeItsInnerLabelHolds()
    {
        var types = TypesOf(Parse(Declarations));
        var plain = Assert.IsType<TypeBodyDeclarationNode>(types[0]);
        var derived = Assert.IsType<TypeBodyDeclarationNode>(types[1]);
        var bare = Assert.IsType<EnumDeclarationNode>(types[2]);
        var sized = Assert.IsType<EnumDeclarationNode>(types[3]);

        Assert.Null(plain.BaseClause);
        Assert.Null(plain.BaseList);
        Assert.Null(bare.TypeAnnotation);
        Assert.Null(bare.Type);
        Assert.NotNull(derived.BaseClause);
        Assert.Same(derived.BaseList, derived.BaseClause);
        Assert.NotNull(sized.TypeAnnotation);
        Assert.Same(sized.Type, sized.TypeAnnotation);
    }

    [Fact]
    public void AnEnumsUnderlyingTypeSurvivesPrinting()
    {
        const string source = "enum Sized : byte { A, B }";

        var printed = MistPrettyPrinter.Print(Parse(source), source);
        var reparsed = Assert.IsType<EnumDeclarationNode>(TypesOf(Parse(printed)).Single());

        Assert.Contains("enum Sized : byte {", printed, StringComparison.Ordinal);
        Assert.Equal("byte", printed.Substring(reparsed.TypeAnnotation!.Span.Start, reparsed.TypeAnnotation.Span.Length));
    }

    private static CompilationUnitNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var text = new SourceText("probe.mist", source);
        var result = new MistParser().Parse(new MistLexer().Lex(text, diagnostics), diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static List<SyntaxNode> TypesOf(CompilationUnitNode root)
    {
        return root.Items
            .Select(item => Assert.IsType<TypeItemTopLevelItemNode>(item).TypeDeclaration)
            .Select(declaration => declaration switch
            {
                BodyTypeTypeDeclarationNode body => (SyntaxNode)body.TypeBody,
                EnumTypeTypeDeclarationNode enumType => enumType.EnumDeclaration,
                _ => declaration,
            })
            .ToList();
    }

    // Read off the declared properties and the group structs inside them, so a node Children() misses is still found.
    private static IEnumerable<SyntaxNode> Held(SyntaxNode node)
    {
        return node.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite)
            .SelectMany(property => NodesIn(property.GetValue(node)));
    }

    private static IEnumerable<SyntaxNode> NodesIn(object? value)
    {
        return value switch
        {
            SyntaxNode node => [node],
            IEnumerable items and not string => items.Cast<object?>().SelectMany(NodesIn),
            { } group when group.GetType().IsValueType && group.GetType().Namespace == typeof(CompilationUnitNode).Namespace
                => group.GetType().GetFields().SelectMany(field => NodesIn(field.GetValue(group))),
            _ => [],
        };
    }
}