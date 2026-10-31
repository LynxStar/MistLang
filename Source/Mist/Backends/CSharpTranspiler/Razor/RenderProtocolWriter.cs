using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record RenderScope(
    ComponentCatalog Catalog,
    ComponentScope Components,
    ImmutableArray<string> Usings,
    IReadOnlyDictionary<string, string> MemberTypes,
    RazorReporter Reporter);

internal sealed class RenderProtocolWriter
{
    private const string RootBuilder = "__builder";

    private readonly MistWriter writer;
    private readonly StaticMarkup statics;
    private readonly ElementLowering elements;
    private readonly ComponentLowering components;
    private int sequence;
    private int fragments;
    private int depth = 2;

    public RenderProtocolWriter(MistWriter writer, SourceText file, RenderScope scope)
    {
        this.writer = writer;
        File = file;
        Scope = scope;
        statics = new StaticMarkup(file.Contents, element => Resolve(element).Kind != ResolutionKind.Element);
        elements = new ElementLowering(this);
        components = new ComponentLowering(this);
    }

    public SourceText File { get; }

    public RenderScope Scope { get; }

    public string Builder { get; private set; } = RootBuilder;

    public void WriteRenderMethod(RazorSyntaxTree tree, bool separate)
    {
        var body = tree.Content.IsEmpty ? default : SpanOf(tree.Content);
        writer.Synthesized($"{(separate ? "\n" : "")}    protected void BuildRenderTree(RenderTreeBuilder {RootBuilder})\n    {{\n", File, body);
        WriteContent(MarkupWhitespace.Content(tree.Content));
        writer.Synthesized("    }\n", File, body);
    }

    public int NextSequence()
    {
        return sequence++;
    }

    public SourcePiece Code(CodeRegion region)
    {
        return new SourcePiece(File, region.Span);
    }

    public SourcePiece Code(SourceSpan span)
    {
        return new SourcePiece(File, span);
    }

    public void Line(SourceSpan construct, params ReadOnlySpan<MistPiece> pieces)
    {
        writer.Synthesized(new string(' ', depth * 4), File, construct);
        writer.Write(File, construct, pieces);
        writer.Synthesized("\n", File, construct);
    }

    public void Call(SourceSpan construct, string method, params ReadOnlySpan<MistPiece> arguments)
    {
        writer.Synthesized($"{new string(' ', depth * 4)}{Builder}.{method}(", File, construct);
        writer.Write(File, construct, arguments);
        writer.Synthesized(");\n", File, construct);
    }

    public void Report(MistDiagnostic descriptor, SourceSpan span, params ReadOnlySpan<SlotValue> arguments)
    {
        Scope.Reporter.Report(File, descriptor, span, arguments);
    }

    public ComponentResolution Resolve(MarkupElement element)
    {
        return Scope.Catalog.Resolve(element.Name, Scope.Components);
    }

    public string TypeName(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot > 0 && Scope.Usings.Contains(fullName[..dot], StringComparer.Ordinal) ? fullName[(dot + 1)..] : fullName;
    }

    public void WriteContent(ImmutableArray<RazorNode> nodes)
    {
        foreach(var run in statics.Runs(nodes))
        {
            if(run.IsStatic)
            {
                WriteStatic(run.Nodes);
                continue;
            }

            WriteNode(run.Nodes[0]);
        }
    }

    public void WriteFragment(SourceSpan construct, string parameter, string? valueType, MistPiece context, ImmutableArray<RazorNode> content)
    {
        var fragmentBuilder = $"__b{++fragments}";
        var cast = valueType is null ? "(RenderFragment)((" : $"(RenderFragment<{valueType}>)(({valueType} ";
        writer.Synthesized($"{new string(' ', depth * 4)}{Builder}.AddComponentParameter({NextSequence()}, {MistLiteral.String(parameter)}, {cast}", File, construct);
        if(valueType is not null)
        {
            writer.Write(File, construct, context, ") => (");
        }

        writer.Synthesized($"RenderTreeBuilder {fragmentBuilder}) =>\n", File, construct);
        Line(construct, "{");
        var outer = Builder;
        Builder = fragmentBuilder;
        depth++;
        WriteContent(content);
        depth--;
        Builder = outer;
        Line(construct, "}));");
    }

    public static SourceSpan SpanOf(IReadOnlyList<RazorNode> nodes)
    {
        return new SourceSpan(nodes[0].Span.Start, nodes[^1].Span.End - nodes[0].Span.Start);
    }

    private void WriteStatic(ImmutableArray<RazorNode> nodes)
    {
        if(nodes.All(node => node is MarkupText))
        {
            foreach(var text in nodes.Cast<MarkupText>())
            {
                var plain = StaticMarkup.TryPlainText(text.Text, out var decoded);
                Call(text.Span, plain ? "AddContent" : "AddMarkupContent", $"{NextSequence()}, {MistLiteral.String(plain ? decoded : text.Text)}");
            }

            return;
        }

        Call(SpanOf(nodes), "AddMarkupContent", $"{NextSequence()}, {MistLiteral.String(statics.Render(nodes))}");
    }

    private void WriteNode(RazorNode node)
    {
        switch(node)
        {
            case ImplicitExpression { Code: var code }:
                Call(node.Span, "AddContent", $"{NextSequence()}, ", Code(code));
                break;
            case ExplicitExpression { Code: var code }:
                Call(node.Span, "AddContent", $"{NextSequence()}, ", Code(code));
                break;
            case CodeRegion region:
                Line(region.Span, Code(region));
                break;
            case StatementBlock block:
                WriteContent(MarkupWhitespace.CodeBody(block.Body, File.Contents));
                break;
            case ControlBlock control:
                WriteControl(control);
                break;
            case MarkupElement element:
                WriteElement(element);
                break;
        }
    }

    private void WriteControl(ControlBlock control)
    {
        foreach(var arm in control.Arms)
        {
            var keyword = new SourceSpan(arm.Span.Start + (arm.Span.Start == control.Span.Start ? 1 : 0), arm.Keyword.Length);
            if(arm.Header is { } header)
            {
                Line(arm.Span, ArmKeyword(arm, keyword), " ", Code(header));
            }
            else
            {
                Line(arm.Span, ArmKeyword(arm, keyword));
            }

            Line(arm.Span, "{");
            depth++;
            WriteContent(MarkupWhitespace.CodeBody(arm.Body, File.Contents));
            depth--;
            Line(arm.Span, "}");
        }
    }

    private MistPiece ArmKeyword(ControlArm arm, SourceSpan keyword)
    {
        return File.Contents.AsSpan(keyword.Start, keyword.Length).SequenceEqual(arm.Keyword) ? Code(keyword) : arm.Keyword;
    }

    private void WriteElement(MarkupElement element)
    {
        var resolution = Resolve(element);
        switch(resolution.Kind)
        {
            case ResolutionKind.Component:
                components.Write(element, resolution.Component!);
                return;
            case ResolutionKind.Unknown:
                Report(RazorDiagnostics.UnknownComponent, element.NameSpan, new NameValue(element.Name));
                break;
            case ResolutionKind.Ambiguous:
                Report(RazorDiagnostics.AmbiguousComponentName, element.NameSpan, new NameValue(element.Name),
                    new SequenceValue(SlotKind.Type, [.. resolution.Candidates.Select(candidate => (SlotValue)new TypeValue(candidate.FullName, candidate.FullName))]));
                break;
        }

        elements.Write(element);
    }
}