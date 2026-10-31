using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal static class ComponentClassWriter
{
    public static SourceText Write(RazorSyntaxTree tree, ComponentDirectives directives, string className, RenderScope scope)
    {
        var writer = new MistWriter();
        var file = tree.Text;
        DirectiveLowering.WriteUsings(writer, directives, file);
        writer.Synthesized("\n", file, default);
        DirectiveLowering.WriteClassAttributes(writer, directives, className);
        writer.Synthesized($"public class {className}", file, default);
        DirectiveLowering.WriteBaseList(writer, directives, file);
        writer.Synthesized("\n{\n", file, default);
        DirectiveLowering.WriteMembers(writer, directives, className, file);
        var separate = !directives.Injects.IsEmpty || directives.RenderMode is not null;
        foreach(var block in tree.CodeBlocks.Where(block => block.Body.Text.Length > 0))
        {
            writer.Synthesized(separate ? "\n    " : "    ", file, block.Span);
            writer.Verbatim(file, block.Body.Span);
            writer.Synthesized("\n", file, block.Span);
            separate = true;
        }

        new RenderProtocolWriter(writer, file, scope).WriteRenderMethod(tree, separate);
        writer.Synthesized("}\n", file, default);
        return writer.ToSourceText(file.Path);
    }
}