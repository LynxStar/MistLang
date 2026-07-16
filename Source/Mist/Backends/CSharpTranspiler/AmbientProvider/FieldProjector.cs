using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR fields onto a Mist ConcreteType shell.
/// </summary>
internal static class FieldProjector
{
    /// <summary>
    /// Projects all public and protected instance and static fields (excluding enum backing
    /// fields and compiler-generated fields) onto the given ConcreteType.
    /// </summary>
    public static void Project(ConcreteType concreteType, Type clrType, AmbientTypeResolver resolver)
    {
        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var field in fields)
        {
            // Only project public and protected fields.
            if(!field.IsPublic && !field.IsFamily && !field.IsFamilyOrAssembly)
            {
                continue;
            }

            // Skip compiler-generated backing fields (e.g., <PropertyName>k__BackingField).
            if(field.Name.StartsWith('<'))
            {
                continue;
            }

            var resolvedType = resolver.Resolve(field.FieldType, concreteType.Scope);
            var modifiers = new MemberModifiers
            {
                Accessibility = ClrAccessibilityMap.FromField(field),
                IsStatic = field.IsStatic,
                IsReadonly = field.IsInitOnly || field.IsLiteral,
            };

            var projected = new Field(field.Name, resolvedType, OwnershipKind.Unrestricted, modifiers);
            AttributeProjector.Project(field, projected.AttributesList, concreteType.Scope, resolver);
            concreteType.FieldsList.Add(projected);
        }
    }
}