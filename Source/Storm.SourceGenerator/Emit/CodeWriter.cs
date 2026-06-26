using System;
using System.Text;

namespace Storm.SourceGenerator.Emit
{
    /// <summary>
    /// A deterministic C# code-writing utility that manages indentation levels
    /// and produces consistent, reproducible output with no timestamps or
    /// non-deterministic content.
    /// </summary>
    public sealed class CodeWriter
    {
        private StringBuilder _sb = new StringBuilder();
        private int _indentLevel;
        private const string IndentUnit = "    "; // 4 spaces

        /// <summary>
        /// Increases the current indentation level by one.
        /// </summary>
        public void Indent()
        {
            _indentLevel++;
        }

        /// <summary>
        /// Decreases the current indentation level by one.
        /// </summary>
        public void Dedent()
        {
            if(_indentLevel > 0)
            {
                _indentLevel--;
            }
        }

        /// <summary>
        /// Writes a line of code at the current indentation level.
        /// </summary>
        public void WriteLine(string line)
        {
            if(string.IsNullOrEmpty(line))
            {
                _sb.AppendLine();
                return;
            }

            for(var i = 0; i < _indentLevel; i++)
            {
                _sb.Append(IndentUnit);
            }
            _sb.AppendLine(line);
        }

        /// <summary>
        /// Writes an empty line (no indentation).
        /// </summary>
        public void WriteEmptyLine()
        {
            _sb.AppendLine();
        }

        /// <summary>
        /// Writes a block with a header line, opening brace, body, and closing brace.
        /// The body is indented one level deeper than the header.
        /// </summary>
        /// <param name="header">The block header (e.g., "public class Foo").</param>
        /// <param name="body">An action that writes the block body.</param>
        public void WriteBlock(string header, Action body)
        {
            WriteLine(header);
            WriteLine("{");
            Indent();
            body();
            Dedent();
            WriteLine("}");
        }

        /// <summary>
        /// Returns the accumulated code as a string.
        /// </summary>
        public override string ToString()
        {
            return _sb.ToString();
        }
    }
}