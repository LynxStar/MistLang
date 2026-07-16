using System.Reflection;
using Mist.ProgramModel.Model;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Maps CLR accessibility onto the four-level Mist <see cref="Accessibility"/> enum
/// without broadening. The mapping never produces <see cref="Accessibility.Internal"/>
/// because an external assembly's internal member must not be widened into the
/// consumer's compilation.
/// </summary>
internal static class ClrAccessibilityMap
{
    /// <summary>
    /// Maps a CLR field's accessibility to the Mist <see cref="Accessibility"/> enum.
    /// <list type="table">
    ///   <item><term>IsPublic</term><description><see cref="Accessibility.Public"/></description></item>
    ///   <item><term>IsFamily (protected)</term><description><see cref="Accessibility.Protected"/></description></item>
    ///   <item><term>IsFamilyOrAssembly (protected internal)</term><description><see cref="Accessibility.Protected"/></description></item>
    ///   <item><term>IsPrivate</term><description><see cref="Accessibility.Private"/></description></item>
    ///   <item><term>IsAssembly (internal)</term><description><see cref="Accessibility.Private"/></description></item>
    ///   <item><term>IsFamilyAndAssembly (private protected)</term><description><see cref="Accessibility.Private"/></description></item>
    /// </list>
    /// </summary>
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

    /// <summary>
    /// Maps a CLR method's accessibility to the Mist <see cref="Accessibility"/> enum.
    /// Applies to regular methods, constructors, property/indexer accessors, and operators.
    /// </summary>
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

    /// <summary>
    /// Maps a CLR nested type's accessibility to the Mist <see cref="Accessibility"/> enum.
    /// </summary>
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

    /// <summary>
    /// Determines the overall accessibility of a CLR property from its most-visible accessor.
    /// A property's effective accessibility is that of its most accessible accessor.
    /// </summary>
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