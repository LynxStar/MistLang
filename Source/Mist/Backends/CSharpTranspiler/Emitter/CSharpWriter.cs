using System.Text;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal sealed class CSharpWriter
{
    private readonly StringBuilder sb = new();
    private int indentLevel;
    private const string IndentUnit = "    ";

    public void Indent()
    {
        indentLevel++;
    }

    public void Dedent()
    {
        if(indentLevel > 0)
        {
            indentLevel--;
        }
    }

    public void WriteLine(string text)
    {
        WriteIndent();
        _ = sb.AppendLine(text);
    }

    public void WriteBlankLine()
    {
        _ = sb.AppendLine();
    }

    public void Write(string text)
    {
        WriteIndent();
        _ = sb.Append(text);
    }

    public void Append(string text)
    {
        _ = sb.Append(text);
    }

    public void AppendLine()
    {
        _ = sb.AppendLine();
    }

    public BlockScope Block()
    {
        WriteLine("{");
        Indent();
        return new BlockScope(this);
    }

    internal readonly struct BlockScope(CSharpWriter writer) : IDisposable
    {
        public void Dispose()
        {
            writer.Dedent();
            writer.WriteLine("}");
        }
    }

    public override string ToString()
    {
        return sb.ToString();
    }

    private void WriteIndent()
    {
        for(var i = 0; i < indentLevel; i++)
        {
            _ = sb.Append(IndentUnit);
        }
    }
}