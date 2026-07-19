using System.Text;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Low-level text writer for emitting formatted C# source code.
/// Tracks indentation level and provides line-oriented output methods.
/// </summary>
internal sealed class CSharpWriter
{
    private readonly StringBuilder sb = new();
    private int indentLevel;
    private const string IndentUnit = "    ";

    /// <summary>
    /// Increases the indentation level by one.
    /// </summary>
    public void Indent()
    {
        indentLevel++;
    }

    /// <summary>
    /// Decreases the indentation level by one.
    /// </summary>
    public void Dedent()
    {
        if(indentLevel > 0)
        {
            indentLevel--;
        }
    }

    /// <summary>
    /// Writes a line with the current indentation, followed by a newline.
    /// </summary>
    public void WriteLine(string text)
    {
        WriteIndent();
        _ = sb.AppendLine(text);
    }

    /// <summary>
    /// Writes an empty line (no indentation).
    /// </summary>
    public void WriteBlankLine()
    {
        _ = sb.AppendLine();
    }

    /// <summary>
    /// Writes text at the current indentation without a trailing newline.
    /// </summary>
    public void Write(string text)
    {
        WriteIndent();
        _ = sb.Append(text);
    }

    /// <summary>
    /// Appends text without indentation or newline. Used for building a line
    /// incrementally after a <see cref="Write"/> call.
    /// </summary>
    public void Append(string text)
    {
        _ = sb.Append(text);
    }

    /// <summary>
    /// Appends a newline to terminate the current line.
    /// </summary>
    public void AppendLine()
    {
        _ = sb.AppendLine();
    }

    /// <summary>
    /// Writes an opening brace on its own line and increases indentation.
    /// </summary>
    public void OpenBrace()
    {
        WriteLine("{");
        Indent();
    }

    /// <summary>
    /// Decreases indentation and writes a closing brace on its own line.
    /// </summary>
    public void CloseBrace()
    {
        Dedent();
        WriteLine("}");
    }

    /// <summary>
    /// Returns the accumulated source text.
    /// </summary>
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