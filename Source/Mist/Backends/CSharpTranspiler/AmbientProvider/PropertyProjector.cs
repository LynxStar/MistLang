using System.Reflection;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR properties onto a Mist MemberType shell.
/// </summary>
internal static class PropertyProjector
{
    /// <summary>
    /// Projects all public and protected instance and static properties onto the given MemberType.
    /// </summary>
    public static void Project(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var prop in properties)
        {
            // Skip indexers — they're projected by IndexerProjector.
            if(prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            // Only project if at least one accessor is public or protected.
            if(!HasVisibleAccessor(prop))
            {
                continue;
            }

            var resolvedType = resolver.ResolveInbound(prop.PropertyType, memberType.Scope);
            var getter = CreateGetter(prop);
            var setter = CreateSetter(prop);

            var accessibility = ClrAccessibilityMap.FromProperty(prop);
            var modifiers = new MemberModifiers
            {
                Accessibility = accessibility,
                IsStatic = (prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic) == true,
            };

            var projected = new Property(prop.Name, resolvedType, modifiers)
            {
                Getter = getter,
                Setter = setter,
            };

            AttributeProjector.Project(prop, projected.AttributesList, memberType.Scope, resolver);
            memberType.PropertiesList.Add(projected);
        }
    }

    /// <summary>
    /// Determines whether a property has at least one public or protected accessor.
    /// </summary>
    private static bool HasVisibleAccessor(PropertyInfo prop)
    {
        return IsVisibleMethod(prop.GetMethod) || IsVisibleMethod(prop.SetMethod);
    }

    /// <summary>
    /// Returns whether a method is public or protected (family/familyOrAssembly).
    /// </summary>
    private static bool IsVisibleMethod(MethodInfo? method)
    {
        if(method is null)
        {
            return false;
        }

        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

    /// <summary>
    /// Creates a getter accessor if the property has a public or protected get method.
    /// </summary>
    private static PropertyAccessor? CreateGetter(PropertyInfo prop)
    {
        if(IsVisibleMethod(prop.GetMethod))
        {
            return new PropertyAccessor();
        }

        return null;
    }

    /// <summary>
    /// Creates a setter accessor if the property has a public or protected set or init method.
    /// Detects init-only setters via the IsExternalInit convention (return type has
    /// required modifier IsExternalInit).
    /// </summary>
    private static PropertyAccessor? CreateSetter(PropertyInfo prop)
    {
        var setMethod = prop.SetMethod;
        if(!IsVisibleMethod(setMethod))
        {
            return null;
        }

        var isInit = setMethod!.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(t => t.FullName == "System.Runtime.CompilerServices.IsExternalInit");

        return new PropertyAccessor { IsInit = isInit };
    }
}