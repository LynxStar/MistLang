using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Tests.Backend.Razor;

internal static class RazorParse
{
    public static (RazorSyntaxTree Tree, IReadOnlyList<Diagnostic> Diagnostics) Text(string contents)
    {
        var bag = new DiagnosticBag(PipelineStage.Backend);
        var tree = RazorParser.Parse(new SourceText("Components/Probe.razor", contents), bag);
        return (tree, bag.Admitted);
    }

    public static RazorSyntaxTree Clean(string contents)
    {
        var (tree, diagnostics) = Text(contents);
        Assert.True(diagnostics.Count == 0, string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.False(tree.HasErrors);
        return tree;
    }

    public static ImmutableArray<RazorNode> Content(string contents)
    {
        return Significant(Clean(contents).Content);
    }

    public static T Single<T>(string contents) where T : RazorNode
    {
        return Assert.IsType<T>(Assert.Single(Content(contents)));
    }

    public static ImmutableArray<RazorNode> Significant(ImmutableArray<RazorNode> nodes)
    {
        return [.. nodes.Where(node => node is not MarkupText { Text: var value } || !string.IsNullOrWhiteSpace(value))];
    }

    public static Diagnostic Error(string contents)
    {
        var (tree, diagnostics) = Text(contents);
        Assert.True(tree.HasErrors);
        return Assert.Single(diagnostics);
    }

    public static (int Offset, int Length) Where(Diagnostic diagnostic)
    {
        var range = Assert.IsType<SourceRange>(diagnostic.Location);
        return (range.Offset, range.Length);
    }

    public static (int Line, int Column) LineAndColumn(Diagnostic diagnostic)
    {
        var start = diagnostic.Position().Start;
        return (start.Line, start.Column);
    }

    public static string Fixture(string fileName)
    {
        using var stream = typeof(RazorParse).Assembly.GetManifestResourceStream($"RazorFixture.{fileName}")
            ?? throw new InvalidOperationException($"the fixture '{fileName}' is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IEnumerable<CodeRegion> CodeRegions(RazorSyntaxTree tree)
    {
        return tree.Directives.Select(directive => directive.Argument).OfType<CodeRegion>()
            .Concat(tree.CodeBlocks.Select(block => block.Body))
            .Concat(tree.Content.SelectMany(CodeRegions));
    }

    private static IEnumerable<CodeRegion> CodeRegions(RazorNode node)
    {
        return node switch
        {
            CodeRegion region => [region],
            ImplicitExpression implicitExpression => [implicitExpression.Code],
            ExplicitExpression explicitExpression => [explicitExpression.Code],
            StatementBlock block => block.Body.SelectMany(CodeRegions),
            ControlBlock control => control.Arms.SelectMany(arm => (arm.Header is { } header ? [header] : Enumerable.Empty<CodeRegion>()).Concat(arm.Body.SelectMany(CodeRegions))),
            MarkupElement element => element.Attributes.SelectMany(CodeRegions).Concat(element.Children.SelectMany(CodeRegions)),
            TextTag tag => tag.Children.SelectMany(CodeRegions),
            TextLine line => line.Content.SelectMany(CodeRegions),
            _ => [],
        };
    }

    private static IEnumerable<CodeRegion> CodeRegions(MarkupAttribute attribute)
    {
        return attribute switch
        {
            DirectiveAttribute { Value: { } value } => [value],
            HtmlAttribute { Value: { } value } => value.Parts.SelectMany(CodeRegions),
            _ => [],
        };
    }
}