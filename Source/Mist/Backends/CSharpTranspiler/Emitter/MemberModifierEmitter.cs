using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Maps a member's modifiers to their C# source form, with a trailing space when any
/// are present so call sites can concatenate directly.
/// </summary>
internal static class MemberModifierEmitter
{
    public static string Emit(MemberModifiers modifiers)
    {
        var parts = new List<string>();

        var accessibility = modifiers.Accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            _ => null,
        };

        if(accessibility is not null)
        {
            parts.Add(accessibility);
        }

        if(modifiers.IsStatic)
        {
            parts.Add("static");
        }

        if(modifiers.IsReadonly)
        {
            parts.Add("readonly");
        }

        return parts.Count > 0 ? string.Join(" ", parts) + " " : "";
    }
}