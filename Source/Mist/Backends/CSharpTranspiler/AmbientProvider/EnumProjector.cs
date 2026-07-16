using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR enum metadata (underlying type and members) onto a Mist EnumType shell.
/// </summary>
internal static class EnumProjector
{
    /// <summary>
    /// Projects the underlying type and all public members of a CLR enum
    /// onto the given Mist EnumType shell.
    /// </summary>
    public static void Project(EnumType enumType, Type clrType, AmbientTypeResolver resolver)
    {
        // Project underlying type (int, byte, short, etc.).
        var underlyingClrType = clrType.GetEnumUnderlyingType();
        enumType.UnderlyingType = resolver.Resolve(underlyingClrType, enumType.Scope);

        // Project members (named constants).
        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach(var field in fields)
        {
            var member = new EnumMember(field.Name, isDefault: false, value: null);
            enumType.MembersList.Add(member);
        }

        // Project type-level attributes.
        AttributeProjector.Project(clrType, enumType.AttributesList, enumType.Scope, resolver);
    }
}
