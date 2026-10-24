using System.Reflection;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR indexers onto a Mist MemberType shell.
/// In CLR, indexers are properties with parameters (typically named "Item").
/// </summary>
internal static class IndexerProjector
{
    /// <summary>
    /// Projects all public and protected indexers (properties with index parameters)
    /// onto the given MemberType.
    /// Skips indexers with pointer or unresolvable parameter types.
    /// </summary>
    public static void Project(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var prop in properties)
        {
            if(prop.GetIndexParameters().Length == 0)
            {
                continue;
            }

            if(Judge(prop, memberType, resolver, out _) is { } projected)
            {
                memberType.IndexersList.Add(projected);
            }
        }
    }

    internal static Indexer? Judge(
        PropertyInfo prop, MemberType memberType, AmbientTypeResolver resolver, out Withholding? withheld)
    {
        // Only project if at least one accessor is public or protected.
        if(!HasVisibleAccessor(prop))
        {
            withheld = new Withholding(WithholdingReason.NonPublic);
            return null;
        }

        var resolvedType = resolver.ResolveInbound(
            prop.PropertyType,
            memberType.Scope,
            ClrNullability.ResultIsNonNull(prop, prop),
            TupleElementNames.Of(prop, prop.PropertyType));
        if(resolvedType is ErrorType or NotProjectedType)
        {
            withheld = new Withholding(WithholdingReason.UnresolvableType, prop.PropertyType);
            return null;
        }

        var parameters = ParameterProjector.Project(prop.GetIndexParameters(), memberType.Scope, resolver, out var unresolvable);
        if(parameters is null)
        {
            withheld = new Withholding(WithholdingReason.UnresolvableType, unresolvable);
            return null;
        }

        var getter = IsVisibleMethod(prop.GetMethod) ? new PropertyAccessor { Accessibility = ClrAccessibilityMap.FromMethod(prop.GetMethod!) } : null;
        var setter = IsVisibleMethod(prop.SetMethod) ? new PropertyAccessor { Accessibility = ClrAccessibilityMap.FromMethod(prop.SetMethod!) } : null;

        var accessibility = ClrAccessibilityMap.FromProperty(prop);
        var modifiers = new MemberModifiers
        {
            Accessibility = accessibility,
            IsStatic = (prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic) == true,
            IsRef = prop.PropertyType.IsByRef,
            ReturnsReadonlyRef = ParameterProjector.IsReadonlyRef(prop.GetMethod?.ReturnParameter),
            IsClrProjected = true,
            ClrResultIsNonNull = ClrNullability.ResultIsNonNull(prop, prop),
        };

        var projected = new Indexer(resolvedType, parameters, modifiers)
        {
            DeclaringType = memberType,
            Getter = getter,
            Setter = setter,
        };

        AttributeProjector.Project(prop, projected.AttributesList, memberType.Scope, resolver);
        withheld = null;
        return projected;
    }

    private static bool HasVisibleAccessor(PropertyInfo prop)
    {
        return IsVisibleMethod(prop.GetMethod) || IsVisibleMethod(prop.SetMethod);
    }

    private static bool IsVisibleMethod(MethodInfo? method)
    {
        if(method is null)
        {
            return false;
        }

        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

}
