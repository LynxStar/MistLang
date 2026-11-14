using System.Text;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Names the static class an extension block emits as: <c>{Receiver}Extensions_{file}_{index}</c>.
/// <para>
/// Sanitising is not cosmetic. An array or generic receiver's model name carries angle brackets and
/// commas, which C# re-parses as a generic class declaration colliding with the user's own type, so
/// the name is reduced to identifier characters rather than trusted to be one.
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
    /// Characters C# cannot carry are dropped rather than replaced, which keeps the name unique per
    /// receiver spelling.
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