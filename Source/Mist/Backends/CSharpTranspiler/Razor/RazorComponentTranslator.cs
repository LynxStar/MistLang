using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.ProgramModel.Construction.Scaffolding;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed class RazorReporter(DiagnosticBag diagnostics)
{
    public bool Reported { get; private set; }

    public void Report(SourceText source, MistDiagnostic descriptor, SourceSpan span, params ReadOnlySpan<SlotValue> arguments)
    {
        Reported = true;
        diagnostics.ReportTyped(descriptor, new SourceRange(new SourceIdentity(source), span.Start, span.Length), arguments);
    }
}

internal sealed record PreparedComponent(
    ClaimedInput Input,
    RazorSyntaxTree Tree,
    ComponentDirectives Directives,
    string ClassName,
    ComponentSource Source,
    ImmutableArray<string> Usings,
    ImmutableArray<CodeMember> Members,
    RazorReporter Reporter);

internal sealed class RazorComponentTranslator : ISourceTranslator
{
    public ImmutableArray<string> Extensions => [".razor"];

    public ImmutableArray<TranslatedUnit> Translate(TranslationContext context)
    {
        if(context.Inputs.IsEmpty)
        {
            return [];
        }

        var files = context.Inputs.Select(input => (Input: input, Tree: RazorParser.Parse(input.Text, context.Diagnostics))).ToImmutableArray();
        var imports = files.Where(file => ImportsResolution.IsImports(file.Input.Text.Path))
            .GroupBy(file => ImportsResolution.Folder(file.Input.Text.Path), StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => DirectiveLowering.Collect(group.First().Tree, imports: true, new RazorReporter(context.Diagnostics)), StringComparer.Ordinal);
        var components = files.Where(file => !ImportsResolution.IsImports(file.Input.Text.Path))
            .Select(file => Prepare(file.Input, file.Tree, imports, context))
            .ToImmutableArray();
        var catalog = CatalogBuilder.Build(context, [.. components.Select(component => component.Source)]);
        return [.. components.Select(component => Translate(component, catalog)).OfType<TranslatedUnit>()];
    }

    internal static PreparedComponent Prepare(
        ClaimedInput input, RazorSyntaxTree tree, IReadOnlyDictionary<string, ComponentDirectives> imports, TranslationContext context)
    {
        var reporter = new RazorReporter(context.Diagnostics);
        var path = input.Text.Path;
        var directives = ImportsResolution.For(path, input.Project.ProjectFolder, imports)
            .Aggregate(ComponentDirectives.Empty, (outer, inner) => outer.Then(inner))
            .Then(DirectiveLowering.Collect(tree, imports: false, reporter));
        var className = Path.GetFileNameWithoutExtension(path);
        var namespaceName = directives.Namespace?.Value.Text
            ?? NamespaceBuilder.ComputeInferredNamespace(path, input.Project.Name, input.Project.ProjectFolder);
        var usings = DirectiveLowering.Namespaces(directives);
        var globalUsings = input.Project.GlobalUsings.IsDefault ? [] : input.Project.GlobalUsings;
        var scope = new ComponentScope([.. Enclosing(namespaceName).Concat(usings).Concat(globalUsings).Distinct(StringComparer.Ordinal)]);
        var members = tree.CodeBlocks
            .SelectMany(block => CodeBlockMembers.Read(block, context.Lexer, context.Parser, context.Diagnostics.Operation))
            .ToImmutableArray();
        Refuse(tree.Text, members, directives, reporter);
        var fullName = string.IsNullOrEmpty(namespaceName) ? className : $"{namespaceName}.{className}";
        var source = new ComponentSource(fullName, directives.Inherits?.Value.Text, scope, tree.CodeBlocks);
        return new PreparedComponent(input, tree, directives, className, source, usings, members, reporter);
    }

    internal static TranslatedUnit? Translate(PreparedComponent component, ComponentCatalog catalog)
    {
        if(component.Tree.HasErrors)
        {
            return null;
        }

        var memberTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var member in component.Members.Where(member => member is { Kind: MemberKind.Field or MemberKind.Property, TypeText: not null }))
        {
            _ = memberTypes.TryAdd(member.Name, member.TypeText!);
        }

        var scope = new RenderScope(catalog, component.Source.Scope, component.Usings, memberTypes, component.Reporter);
        var text = ComponentClassWriter.Write(component.Tree, component.Directives, component.ClassName, scope);
        return component.Reporter.Reported ? null : new TranslatedUnit(text, component.Input.Project);
    }

    private static void Refuse(SourceText file, ImmutableArray<CodeMember> members, ComponentDirectives directives, RazorReporter reporter)
    {
        foreach(var member in members)
        {
            if(member.Kind is MemberKind.Type or MemberKind.Namespace)
            {
                reporter.Report(file, RazorDiagnostics.NestedTypeInCodeBlock, member.NameSpan, new NameValue(member.Name));
            }
            else if(member.Name.StartsWith("__", StringComparison.Ordinal))
            {
                reporter.Report(file, RazorDiagnostics.ReservedIdentifier, member.NameSpan, new NameValue(member.Name));
            }

            if(member.Kind == MemberKind.Constructor && !directives.Injects.IsEmpty)
            {
                reporter.Report(file, RazorDiagnostics.ConstructorBesideInject, member.NameSpan);
            }
        }
    }

    private static IEnumerable<string> Enclosing(string namespaceName)
    {
        for(var name = namespaceName; !string.IsNullOrEmpty(name); name = name.Contains('.') ? name[..name.LastIndexOf('.')] : "")
        {
            yield return name;
        }
    }
}