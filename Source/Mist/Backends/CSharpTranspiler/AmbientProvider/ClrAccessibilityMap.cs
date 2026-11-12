using System.Reflection;
using Mist.ProgramModel.Model.Enums;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Never produces <see cref="Accessibility.Internal"/>: an external assembly's internal member must not
/// be widened into the consumer's compilation.
/// </summary>
internal static class ClrAccessibilityMap
{
    public static Accessibility FromField(FieldInfo field)
    {
        if(field.IsPublic)
        {
            return Accessibility.Public;
        }

        if(field.IsFamily || field.IsFamilyOrAssembly)
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    public static Accessibility FromMethod(MethodBase method)
    {
        if(method.IsPublic)
        {
            return Accessibility.Public;
        }

        if(method.IsFamily || method.IsFamilyOrAssembly)
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    public static Accessibility FromNestedType(Type nestedType)
    {
        if(nestedType.IsNestedPublic)
        {
            return Accessibility.Public;
        }

        if(nestedType.IsNestedFamily || nestedType.IsNestedFamORAssem)
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    public static Accessibility FromProperty(PropertyInfo property)
    {
        var getMethod = property.GetMethod;
        var setMethod = property.SetMethod;

        if(getMethod is { IsPublic: true } || setMethod is { IsPublic: true })
        {
            return Accessibility.Public;
        }

        if(IsProtected(getMethod) || IsProtected(setMethod))
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    private static bool IsProtected(MethodInfo? method)
    {
        if(method is null)
        {
            return false;
        }

        return method.IsFamily || method.IsFamilyOrAssembly;
    }
}