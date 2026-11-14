using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class FieldProjector
{
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
    public static void Project(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var field in fields)
        {
            if(!field.IsPublic && !field.IsFamily && !field.IsFamilyOrAssembly)
            {
                continue;
            }

            // Skip compiler-generated backing fields (e.g., <PropertyName>k__BackingField).
            if(field.Name.StartsWith('<'))
            {
                continue;
            }

            var resolvedType = resolver.ResolveInbound(
                field.FieldType,
                concreteType.Scope,
                ClrNullability.ResultIsNonNull(field, field),
                TupleElementNames.Of(field, field.FieldType));
            if(resolvedType is ErrorType or NotProjectedType)
            {
                continue;
            }

            var modifiers = new MemberModifiers
            {
                Accessibility = ClrAccessibilityMap.FromField(field),
                IsStatic = field.IsStatic,
                IsReadonly = field.IsInitOnly || field.IsLiteral,
                IsClrProjected = true,
                ClrResultIsNonNull = ClrNullability.ResultIsNonNull(field, field),
            };

            var projected = new Field(field.Name, resolvedType, OwnershipKind.Unrestricted, modifiers)
            {
                DeclaringType = concreteType,
                Initializer = Constant(field, concreteType.Scope, resolver),
            };
            AttributeProjector.Project(field, projected.AttributesList, concreteType.Scope, resolver);
            concreteType.FieldsList.Add(projected);
        }

        if(concreteType is StructType)
        {
            StorageProjector.Project(concreteType, clrType, resolver);
        }
    }

    /// <summary>
    /// The raw metadata value, boxed as the field's own primitive as <see cref="EnumProjector"/> boxes an
    /// enum member's. An enum-typed literal is left without one: its raw value is the underlying integer,
    /// which a literal of the enum's type cannot honestly hold.
    /// </summary>
    private static LiteralExpression? Constant(FieldInfo field, Scope scope, AmbientTypeResolver resolver)
    {
        if(!field.IsLiteral || field.FieldType.IsEnum || field.GetRawConstantValue() is not { } value)
        {
            return null;
        }

        return new LiteralExpression(null!, value, resolver.Resolve(field.FieldType, scope));
    }
}