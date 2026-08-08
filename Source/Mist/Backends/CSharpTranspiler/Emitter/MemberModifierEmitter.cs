using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Maps a member's modifiers to their C# source form, with a trailing space when any
/// are present so call sites can concatenate directly.
/// <para>
/// <paramref name="emitReadonly"/> is the CALLER'S to decide and is not read off
/// <paramref name="modifiers"/>, because whether C# accepts <c>readonly</c> depends on the position
/// rather than the declaration: legal on any field, on a struct's methods and properties, and on
/// nothing in a class but a field. A member also inherits it from a readonly struct without declaring
/// it. Deciding here would need the member kind and the declaring type, neither of which is in a
/// <see cref="MemberModifiers"/>.
/// </para>
/// </summary>
internal static class MemberModifierEmitter
{
    public static string Emit(MemberModifiers modifiers, bool emitReadonly)
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

        if(emitReadonly)
        {
            parts.Add("readonly");
        }

        return parts.Count > 0 ? string.Join(" ", parts) + " " : "";
    }
}