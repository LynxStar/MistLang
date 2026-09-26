using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Parsing;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Parsing;

public sealed class PropertyPatternParsingTests
{
    [Theory]
    [InlineData("Dog { Breed: var breed } dog", "Dog", "dog", "Breed", "breed")]
    [InlineData("Creatures.Dog { Breed: var breed } dog", "Creatures.Dog", "dog", "Breed", "breed")]
    [InlineData("DogAlias { Breed: var breed } dog", "DogAlias", "dog", "Breed", "breed")]
    [InlineData("Dog { Breed: var breed }", "Dog", null, "Breed", "breed")]
    [InlineData("Dog { Breed: var breed } _", "Dog", "_", "Breed", "breed")]
    [InlineData("{ Name: var name } whole", null, "whole", "Name", "name")]
    [InlineData("{ Name: var name }", null, null, "Name", "name")]
    [InlineData("{ Name: var name } _", null, "_", "Name", "name")]
    public void SourceToAst_PropertyPrefixAndWholeBindingRoundTrip(
        string pattern,
        string? typeName,
        string? bindingName,
        string propertyName,
        string subpatternBinding)
    {
        var source = $"type DogAlias = Creatures.Dog; class Probe {{ void Run(object value) {{ if(value is {pattern}) {{ }} }} }}";
        var first = Parse(source);
        AssertPropertyPattern(first, typeName, bindingName, propertyName, subpatternBinding);

        var printed = MistPrettyPrinter.Print(first);
        Assert.Contains($"value is {pattern}", printed, StringComparison.Ordinal);

        var second = Parse(printed);
        AssertPropertyPattern(second, typeName, bindingName, propertyName, subpatternBinding);
        Assert.Equal(printed, MistPrettyPrinter.Print(second));
    }

    [Theory]
    [InlineData("Dog { } dog", "Dog", "dog")]
    [InlineData("{ } whole", null, "whole")]
    public void SourceToAst_EmptyPropertyPatternRetainsItsPrefixAndBinding(
        string pattern,
        string? typeName,
        string bindingName)
    {
        var source = $"class Probe {{ void Run(object value) {{ if(value is {pattern}) {{ }} }} }}";
        var first = Parse(source);
        var propertyPattern = Assert.Single(Descendants(first).OfType<PropertyPatternPrimaryPatternNode>());

        Assert.Equal(typeName, QualifiedName(propertyPattern.TypeName));
        Assert.Equal(bindingName, propertyPattern.Name?.Lexeme);
        Assert.Null(propertyPattern.Subpatterns);

        var printed = MistPrettyPrinter.Print(first);
        Assert.Contains($"value is {pattern}", printed, StringComparison.Ordinal);
        Assert.Equal(printed, MistPrettyPrinter.Print(Parse(printed)));
    }

    [Theory]
    [InlineData("if(value is Dog { Breed: var breed } dog && dog.Breed == breed) { }")]
    [InlineData("var result = value switch { Dog { Breed: var breed } dog when dog.Breed == breed => dog, _ => value };")]
    [InlineData("if(value is { Name: var name } whole && whole.Name == name) { }")]
    [InlineData("var result = value switch { { Name: var name } whole when whole.Name == name => whole, _ => value };")]
    public void SourceToAst_WholeBindingStopsBeforeConditionConjunctAndSwitchGuard(string statement)
    {
        var source = $"class Probe {{ void Run(object value) {{ {statement} }} }}";
        var first = Parse(source);
        var propertyPattern = Assert.Single(Descendants(first).OfType<PropertyPatternPrimaryPatternNode>());

        Assert.Contains(propertyPattern.Name?.Lexeme, new[] { "dog", "whole" });
        var printed = MistPrettyPrinter.Print(first);
        Assert.Equal(printed, MistPrettyPrinter.Print(Parse(printed)));
    }

    [Theory]
    [InlineData("Dog", typeof(BareTypePatternPrimaryPatternNode))]
    [InlineData("Dog dog", typeof(TypePatternPrimaryPatternNode))]
    public void SourceToAst_PropertyPatternOrderingPreservesPlainTypePatterns(string pattern, Type expectedType)
    {
        var source = $"class Probe {{ void Run(object value) {{ if(value is {pattern}) {{ }} }} }}";
        var primary = Assert.Single(Descendants(Parse(source)).OfType<PrimaryPatternNode>());

        Assert.Equal(expectedType, primary.GetType());
    }

    private static void AssertPropertyPattern(
        CompilationUnitNode root,
        string? typeName,
        string? bindingName,
        string propertyName,
        string subpatternBinding)
    {
        var propertyPattern = Assert.Single(Descendants(root).OfType<PropertyPatternPrimaryPatternNode>());
        Assert.Equal(typeName, QualifiedName(propertyPattern.TypeName));
        Assert.Equal(bindingName, propertyPattern.Name?.Lexeme);

        var subpattern = Assert.IsType<PropertySubpatternListNode>(propertyPattern.Subpatterns).PropertySubpattern;
        Assert.Equal(propertyName, subpattern.Name.Lexeme);
        var binding = Assert.Single(Descendants(subpattern).OfType<VarPatternPrimaryPatternNode>());
        Assert.Equal(subpatternBinding, binding.Name.Lexeme);
    }

    private static string? QualifiedName(QualifiedIdentifierNode? name)
    {
        return name is null
            ? null
            : string.Join(".", new[] { name.IDENTIFIER.Lexeme }.Concat(name.IDENTIFIERs.Select(token => token.Lexeme)));
    }

    private static CompilationUnitNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new MistLexer().Lex(new SourceText("property-pattern.mist", source), diagnostics);
        var result = new MistParser().Parse(tokens, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
    {
        foreach(var child in node.Children().Where(child => child is not null))
        {
            yield return child;

            foreach(var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}