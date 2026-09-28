using System.Reflection;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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
        var underlying = resolver.Resolve(underlyingClrType, enumType.Scope);
        enumType.UnderlyingType = underlying;

        // Project members (named constants).
        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach(var field in fields)
        {
            // The raw metadata constant, already boxed as the base's own primitive as IntegralConstant expects
            // of every integral constant: GetValue would box an enum instance, and a MetadataLoadContext refuses it.
            var value = new LiteralExpression(null!, field.GetRawConstantValue(), underlying);
            var member = new EnumMember(field.Name, isDefault: false, value);
            enumType.MembersList.Add(member);
        }

        // Project type-level attributes.
        AttributeProjector.Project(clrType, enumType.AttributesList, enumType.Scope, resolver);
    }
}