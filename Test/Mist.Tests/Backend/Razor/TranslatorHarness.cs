using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Razor;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend.Razor;

internal sealed record Translation(RazorSyntaxTree Tree, SourceText? Unit, IReadOnlyList<Diagnostic> Diagnostics)
{
    public SourceText Text => Unit ?? throw new InvalidOperationException(
        "no unit: " + string.Join(Environment.NewLine, Diagnostics.Select(diagnostic => diagnostic.Message)));

    public string[] Calls()
    {
        var lines = Text.Contents.Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains("void BuildRenderTree(", StringComparison.Ordinal)) + 2;
        var end = Array.FindLastIndex(lines, line => line == "    }");
        return [.. lines[start..end].Select(line => line.Length >= 8 ? line[8..] : line.Trim())];
    }

    public string Codes()
    {
        return string.Join(" ", Diagnostics.Select(diagnostic => diagnostic.Descriptor.Number.ToString()));
    }

    public SourceRange Map(string generated, int skip = 0)
    {
        var offset = Text.Contents.IndexOf(generated, StringComparison.Ordinal);
        for(var index = 0; index < skip; index++)
        {
            offset = Text.Contents.IndexOf(generated, offset + 1, StringComparison.Ordinal);
        }

        Assert.True(offset >= 0, $"'{generated}' is not in the translated text");
        return Text.Origin!.Map(offset, generated.Length);
    }

    public string Original(SourceRange range)
    {
        return Tree.Text.Contents.Substring(range.Offset, range.Length);
    }
}

internal static class TranslatorHarness
{
    public const string ProjectFolder = "/app";
    public const string ComponentPath = "/app/Components/Probe.razor";
    public const string MouseEventArgs = "Microsoft.AspNetCore.Components.Web.MouseEventArgs";

    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly MistProject Project = new("App", ProjectFolder, ProjectFolder + "/mist.toml");

    public static ComponentCatalog Catalog(params ComponentFacts[] components)
    {
        return new ComponentCatalog(components, _ => null,
            [new EventFacts("onclick", MouseEventArgs), new EventFacts("onchange", "Microsoft.AspNetCore.Components.ChangeEventArgs")]);
    }

    public static ComponentFacts Component(string fullName, params (string Name, string Type)[] parameters)
    {
        return new ComponentFacts(fullName, [.. parameters.Select(parameter => ParameterFacts.Declared(parameter.Name, parameter.Type))], 0, IsMist: true);
    }

    public static Translation Run(RazorSyntaxTree tree, ComponentCatalog? catalog = null, MistProject? project = null, params RazorSyntaxTree[] imports)
    {
        project ??= Project;
        var bag = new DiagnosticBag(PipelineStage.Backend);
        var context = new TranslationContext([], [], new NoAmbientTypes(), Lexer, Parser, new ProjectFacts(project.Name, project.ProjectFolder, []), bag);
        var reporter = new RazorReporter(bag);
        var importDirectives = imports.ToDictionary(
            import => ImportsResolution.Folder(import.Text.Path), import => DirectiveLowering.Collect(import, imports: true, reporter), StringComparer.Ordinal);
        var prepared = RazorComponentTranslator.Prepare(new ClaimedInput(tree.Text, project), tree, importDirectives, context);
        var unit = RazorComponentTranslator.Translate(prepared, catalog ?? Catalog());
        return new Translation(tree, unit?.Text, bag.Admitted);
    }

    public static RazorSyntaxTree Parse(string path, string contents)
    {
        var bag = new DiagnosticBag(PipelineStage.Backend);
        var tree = RazorParser.Parse(new SourceText(path, contents), bag);
        Assert.Empty(bag.Admitted);
        return tree;
    }

    public static Translation Content(params Hand[] nodes)
    {
        return Run(new HandTree().Content(nodes).Tree());
    }

    public static Translation Content(ComponentCatalog catalog, params Hand[] nodes)
    {
        return Run(new HandTree().Content(nodes).Tree(), catalog);
    }

    private sealed class NoAmbientTypes : IAmbientTypeSource
    {
        public Type? FindClrType(string fullName)
        {
            return null;
        }

        public Type? FindClrType(string fullName, bool ignoreArity)
        {
            return null;
        }

        public void BuildAndRegister(ProgramModelClass programModel)
        {
        }

        public void Dispose()
        {
        }
    }
}