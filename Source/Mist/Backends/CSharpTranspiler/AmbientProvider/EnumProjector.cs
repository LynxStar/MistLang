using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class EnumProjector
{
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
    public static void Project(EnumType enumType, Type clrType, AmbientTypeResolver resolver)
    {
        var underlyingClrType = clrType.GetEnumUnderlyingType();
        var underlying = resolver.Resolve(underlyingClrType, enumType.Scope);
        enumType.UnderlyingType = underlying;

        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach(var field in fields)
        {
            // The raw metadata constant, already boxed as the base's own primitive as IntegralConstant expects
            // of every integral constant: GetValue would box an enum instance, and a MetadataLoadContext refuses it.
            var value = new LiteralExpression(null!, field.GetRawConstantValue(), underlying);
            var member = new EnumMember(field.Name, isDefault: false, value);
            enumType.MembersList.Add(member);
        }

        AttributeProjector.Project(clrType, enumType.AttributesList, enumType.Scope, resolver);
    }
}