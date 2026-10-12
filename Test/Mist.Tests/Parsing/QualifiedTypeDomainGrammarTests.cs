using System.Collections.Immutable;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using Storm.Runtime;
using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;
using SourceSpan = Storm.Runtime.SourceSpan;
using SourceText = Mist.Common.Source.SourceText;
using TokenStream = Mist.Lexing.Contracts.TokenStream;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public sealed class QualifiedTypeDomainGrammarTests
{
    [Theory]
    [InlineData("Probe . Base", "Probe.Base", false)]
    [InlineData("Probe . Base < int >", "Probe.Base<int>", false)]
    [InlineData("Probe . Outer < int > . Inner < bool >", "Probe.Outer<int>.Inner<bool>", true)]
    [InlineData("Outer < int > . Inner", "Outer<int>.Inner", true)]
    [InlineData("TArgs ...", "TArgs...", false)]
    public void NominalBaseCapturesPrintCompleteQualifiedAndConstructedNames(string entry, string printed, bool enclosing)
    {
        var (root, source) = Parse($"class Derived : {entry} {{ }}");
        var declaration = Assert.IsType<BodyTypeTypeDeclarationNode>(Assert.Single(root.GetTypes()));
        var baseType = Assert.IsType<BaseTypeRefNode>(declaration.TypeBody.BaseList!.First);

        Assert.Equal(enclosing, baseType.Enclosing is not null);
        Assert.Contains($": {printed}", MistPrettyPrinter.Print(root), StringComparison.Ordinal);
        Assert.Equal(entry, source.Contents.Substring(baseType.Span.Start, baseType.Span.Length));
        AssertOrigins(root, source);
    }

    [Theory]
    [InlineData("Positive", "Positive")]
    [InlineData("Probe . Positive", "Probe.Positive")]
    [InlineData("Probe . Nested . Positive", "Probe.Nested.Positive")]
    public void NamedDomainCaptureRetainsAllSegmentsAndItsOwnOrigin(string entry, string printed)
    {
        var (root, source) = Parse($"class Box < N : {entry} > {{ }}");
        var declaration = Assert.IsType<BodyTypeTypeDeclarationNode>(Assert.Single(root.GetTypes()));
        var parameter = Assert.IsType<NamedConstraintDependentTypeParameterTypeParameterNode>(declaration.TypeBody.TypeParams!.TypeParameter);
        var name = parameter.ConstraintName;

        Assert.Equal(entry.Split(' ').Count(part => part == "."), name.IDENTIFIERs.Length);
        Assert.Contains($"N:{printed}", MistPrettyPrinter.Print(root), StringComparison.Ordinal);
        Assert.Equal(entry, source.Contents.Substring(name.Span.Start, name.Span.Length));
        AssertOrigins(root, source);
    }

    [Theory]
    [InlineData("class Derived : int { }")]
    [InlineData("class Derived : Base [ ] { }")]
    [InlineData("class Derived : Base | Other { }")]
    [InlineData("class Box < N : Probe . Positive < int > > { }")]
    public void QualificationDoesNotAdmitUnrelatedBaseShapesOrDomainAnnotations(string text)
    {
        var stream = Tokens(text);
        var diagnostics = new DiagnosticBag();
        _ = new Mist.Parsing.MistParser().Parse(stream, diagnostics);

        Assert.Contains(diagnostics.ToImmutableArray(), diagnostic => diagnostic.Id.Value == "MIST_PARSE_INCOMPLETE");
    }

    private static (CompilationUnitNode Root, SourceText Source) Parse(string text)
    {
        var stream = Tokens(text);
        var diagnostics = new DiagnosticBag();
        var parsed = new Mist.Parsing.MistParser().Parse(stream, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return (Assert.IsType<CompilationUnitNode>(parsed.Root), stream.Source);
    }

    private static TokenStream Tokens(string text)
    {
        var tokens = ImmutableArray.CreateBuilder<Token>();
        var offset = 0;
        foreach(var part in text.Split(' '))
        {
            var kind = part switch
            {
                "class" => TokenKind.CLASS,
                "int" => TokenKind.INT,
                "bool" => TokenKind.BOOL,
                ":" => TokenKind.COLON,
                "." => TokenKind.DOT,
                "..." => TokenKind.ELLIPSIS,
                "<" => TokenKind.LESS_THAN,
                ">" => TokenKind.GREATER_THAN,
                "{" => TokenKind.LEFT_BRACE,
                "}" => TokenKind.RIGHT_BRACE,
                "[" => TokenKind.LEFT_BRACKET,
                "]" => TokenKind.RIGHT_BRACKET,
                "|" => TokenKind.PIPE,
                _ => TokenKind.IDENTIFIER,
            };
            tokens.Add(new Token((int)kind, new SourceSpan(offset, part.Length), new Storm.Runtime.LinePosition(1, offset + 1),
                new Storm.Runtime.LinePosition(1, offset + part.Length + 1), TokenChannel.Default, part));
            offset += part.Length + 1;
        }

        return new TokenStream(new SourceText("qualified-types.mist", text), tokens.ToImmutable());
    }

    private static void AssertOrigins(SyntaxNode node, SourceText source)
    {
        Assert.Same(source, Assert.IsType<ParsedOrigin>(node.Origin).Source);
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                AssertOrigins(child, source);
            }
        }
    }
}