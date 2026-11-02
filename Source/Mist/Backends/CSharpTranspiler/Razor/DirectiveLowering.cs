using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record DirectiveArgument(SourcePiece Value, SourceSpan Directive)
{
    public SourceText Source => Value.Source;
}

internal sealed record InjectDirective(SourcePiece Type, SourcePiece Name, SourceSpan Directive);

internal sealed record ComponentDirectives(
    ImmutableArray<DirectiveArgument> Usings,
    DirectiveArgument? Namespace,
    ImmutableArray<DirectiveArgument> Routes,
    DirectiveArgument? Layout,
    DirectiveArgument? Inherits,
    ImmutableArray<DirectiveArgument> Implements,
    ImmutableArray<InjectDirective> Injects,
    ImmutableArray<DirectiveArgument> Attributes,
    DirectiveArgument? RenderMode)
{
    public static readonly ComponentDirectives Empty = new([], null, [], null, null, [], [], [], null);

    public ComponentDirectives Then(ComponentDirectives inner)
    {
        return new ComponentDirectives(
            Usings.AddRange(inner.Usings), inner.Namespace ?? Namespace, Routes.AddRange(inner.Routes), inner.Layout ?? Layout,
            inner.Inherits ?? Inherits, Implements.AddRange(inner.Implements), Injects.AddRange(inner.Injects),
            Attributes.AddRange(inner.Attributes), inner.RenderMode ?? RenderMode);
    }
}

internal static class DirectiveLowering
{
    public const string RenderModeClass = "__RenderModeAttribute";

    public static readonly ImmutableArray<string> ImplicitUsings =
        ["Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Rendering"];

    public static ComponentDirectives Collect(RazorSyntaxTree tree, bool imports, RazorReporter reporter)
    {
        var directives = ComponentDirectives.Empty;
        foreach(var directive in tree.Directives)
        {
            if(directive.Name is "typeparam" or "preservewhitespace")
            {
                reporter.Report(tree.Text, RazorDiagnostics.DirectiveNotSupportedYet, directive.KeywordSpan, new NameValue(directive.Name));
                continue;
            }

            if(directive.Argument is not { } region || (imports && directive.Name is "page" or "namespace"))
            {
                continue;
            }

            var argument = new DirectiveArgument(new SourcePiece(tree.Text, region.Span), directive.Span);
            directives = directive.Name switch
            {
                "using" => Using(directives, argument, reporter),
                "namespace" => directives with { Namespace = argument },
                "page" => directives with { Routes = directives.Routes.Add(argument) },
                "layout" => directives with { Layout = argument },
                "inherits" => directives with { Inherits = argument },
                "implements" => directives with { Implements = directives.Implements.Add(argument) },
                "inject" => directives with { Injects = directives.Injects.Add(Inject(argument)) },
                "attribute" => directives with { Attributes = directives.Attributes.Add(argument) },
                "rendermode" => directives with { RenderMode = argument },
                _ => directives,
            };
        }

        return directives;
    }

    public static ImmutableArray<string> Namespaces(ComponentDirectives directives)
    {
        return [.. ImplicitUsings.Concat(directives.Usings.Select(argument => argument.Value.Text)).Distinct(StringComparer.Ordinal)];
    }

    public static void WriteUsings(MistWriter writer, ComponentDirectives directives, SourceText file)
    {
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach(var name in ImplicitUsings.Where(written.Add))
        {
            writer.Synthesized($"using {name};\n", file, default);
        }

        foreach(var argument in directives.Usings.Where(argument => written.Add(argument.Value.Text)))
        {
            writer.Write(argument.Source, argument.Directive, "using ", argument.Value, ";\n");
        }

        if(directives.Namespace is { } declared)
        {
            writer.Write(declared.Source, declared.Directive, "\nnamespace ", declared.Value, ";\n");
        }
    }

    public static void WriteClassAttributes(MistWriter writer, ComponentDirectives directives, string className)
    {
        foreach(var route in directives.Routes)
        {
            writer.Write(route.Source, route.Directive, "[Route(", route.Value, ")]\n");
        }

        if(directives.Layout is { } layout)
        {
            writer.Write(layout.Source, layout.Directive, "[Layout(", layout.Value, ")]\n");
        }

        foreach(var attribute in directives.Attributes)
        {
            writer.Write(attribute.Source, attribute.Directive, attribute.Value, "\n");
        }

        if(directives.RenderMode is { } mode)
        {
            writer.Write(mode.Source, mode.Directive, $"[{className}.{RenderModeClass}]\n");
        }
    }

    public static void WriteBaseList(MistWriter writer, ComponentDirectives directives, SourceText file)
    {
        if(directives.Inherits is { } inherits)
        {
            writer.Write(inherits.Source, inherits.Directive, " : ", inherits.Value);
        }
        else
        {
            writer.Synthesized(" : ComponentBase", file, default);
        }

        foreach(var contract in directives.Implements)
        {
            writer.Write(contract.Source, contract.Directive, ", ", contract.Value);
        }
    }

    public static void WriteMembers(MistWriter writer, ComponentDirectives directives, string className, SourceText file)
    {
        foreach(var inject in directives.Injects)
        {
            writer.Write(inject.Type.Source, inject.Directive, "    private readonly ", inject.Type, " ", inject.Name, ";\n");
        }

        if(!directives.Injects.IsEmpty)
        {
            writer.Synthesized($"\n    public {className}(", file, default);
            for(var index = 0; index < directives.Injects.Length; index++)
            {
                var inject = directives.Injects[index];
                writer.Write(inject.Type.Source, inject.Directive, index == 0 ? "" : ", ", inject.Type, " ", inject.Name);
            }

            writer.Synthesized(")\n    {\n", file, default);
            foreach(var inject in directives.Injects)
            {
                writer.Write(inject.Type.Source, inject.Directive, "        this.", inject.Name, " = ", inject.Name, ";\n");
            }

            writer.Synthesized("    }\n", file, default);
        }

        if(directives.RenderMode is { } mode)
        {
            writer.Write(mode.Source, mode.Directive,
                $"{(directives.Injects.IsEmpty ? "" : "\n")}    private class {RenderModeClass} : RenderModeAttribute\n    {{\n        public IComponentRenderMode Mode => ",
                mode.Value, ";\n    }\n");
        }
    }

    private static ComponentDirectives Using(ComponentDirectives directives, DirectiveArgument argument, RazorReporter reporter)
    {
        var text = argument.Value.Text;
        var span = argument.Value.Span;
        var length = text.TrimEnd().TrimEnd(';').TrimEnd().Length;
        if(text.StartsWith("static ", StringComparison.Ordinal) || text.Contains('='))
        {
            reporter.Report(argument.Source, RazorDiagnostics.UsingFormNotSupported, argument.Directive, new TextValue(text));
            return directives;
        }

        var value = new SourcePiece(argument.Source, new SourceSpan(span.Start, length));
        return directives with { Usings = directives.Usings.Add(argument with { Value = value }) };
    }

    private static InjectDirective Inject(DirectiveArgument argument)
    {
        var text = argument.Value.Text.TrimEnd(';').TrimEnd();
        var start = argument.Value.Span.Start;
        var split = text.LastIndexOfAny([' ', '\t']);
        var typeEnd = split < 0 ? text.Length : text[..split].TrimEnd().Length;
        var nameStart = split < 0 ? text.Length : split + 1;
        return new InjectDirective(
            new SourcePiece(argument.Source, new SourceSpan(start, typeEnd)),
            new SourcePiece(argument.Source, new SourceSpan(start + nameStart, text.Length - nameStart)),
            argument.Directive);
    }
}