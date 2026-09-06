using System.Text;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Names the static class an extension block emits as: <c>{Receiver}Extensions_{file}_{index}</c>.
/// <para>
/// SANITISATION IS NOT COSMETIC. A receiver's model name carries whatever the type system calls it,
/// and several of those are not C# identifiers: an array receiver names <c>Array&lt;Int32, unknown&gt;</c>
/// and a generic one <c>Box&lt;T&gt;</c>. Emitted verbatim, C# re-parses the angle brackets as a
/// generic class declaration -- <c>static class Box&lt;T&gt;Extensions_Lib_1</c> collides with the
/// user's own <c>Box&lt;T&gt;</c> and reports errors against types they did write, and the array form
/// cascades into unbalanced braces. So the name is reduced to identifier characters here rather than
/// trusted to be one.
/// </para>
/// <para>
/// Assigned once, when the block is built, and stored on it. A static extension member is called by
/// carrier name from any file, so a name derived at emission from the EMITTING unit would be
/// unreachable from the call site that needs it.
/// </para>
/// </summary>
internal static class CarrierNames
{
    public static string For(IMistType receiver, string? sourcePath, int indexInUnit)
    {
        var stem = Sanitize(Path.GetFileNameWithoutExtension(sourcePath ?? "") ?? "");

        return $"{Sanitize(MistType.StripArity(receiver.Name))}Extensions_{stem}_{indexInUnit}";
    }

    /// <summary>
    /// Identifier characters only, and never a leading digit. Everything a type name can carry that
    /// C# cannot -- angle brackets, commas, spaces, square brackets, dots -- is dropped rather than
    /// replaced, so <c>Array&lt;Int32, unknown&gt;</c> reduces to <c>ArrayInt32unknown</c> and
    /// <c>Box&lt;T&gt;</c> to <c>BoxT</c>: still unique per receiver spelling, and legal.
    /// </summary>
    private static string Sanitize(string name)
    {
        var sanitized = new StringBuilder(name.Length);

        foreach(var character in name)
        {
            if(char.IsLetterOrDigit(character) || character == '_')
            {
                _ = sanitized.Append(character);
            }
        }

        if(sanitized.Length == 0)
        {
            return "X";
        }

        return char.IsDigit(sanitized[0]) ? $"_{sanitized}" : sanitized.ToString();
    }
}