using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class PropertyProjector
{
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
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

            if(Judge(prop, memberType, resolver, out var withheld) is { } projected)
            {
                memberType.PropertiesList.Add(projected);
            }
            else if(withheld is { } reason)
            {
                memberType.WithheldMembers.Add(reason.Snapshot(prop));
            }
        }
    }

    internal static Property? Judge(
        PropertyInfo prop, MemberType memberType, AmbientTypeResolver resolver, out Withholding? withheld)
    {
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

        var getter = CreateGetter(prop);
        var setter = CreateSetter(prop);

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

        var projected = new Property(prop.Name, resolvedType, modifiers)
        {
            DeclaringType = memberType,
            Getter = getter,
            Setter = setter,
            SlotKind = MethodProjector.SlotKindOf(prop.GetMethod ?? prop.SetMethod),
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

    private static PropertyAccessor? CreateGetter(PropertyInfo prop)
    {
        if(IsVisibleMethod(prop.GetMethod))
        {
            return new PropertyAccessor { Accessibility = ClrAccessibilityMap.FromMethod(prop.GetMethod!) };
        }

        return null;
    }

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

        return new PropertyAccessor { IsInit = isInit, Accessibility = ClrAccessibilityMap.FromMethod(setMethod) };
    }
}