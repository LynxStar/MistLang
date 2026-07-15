using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR indexers onto a Mist MemberType shell.
/// In CLR, indexers are properties with parameters (typically named "Item").
/// </summary>
internal static class IndexerProjector
{
    /// <summary>
    /// Projects all public indexers (properties with index parameters) onto the given MemberType.
    /// Skips indexers with pointer or unresolvable parameter types.
    /// </summary>
    public static void Project(MemberType memberType, Type clrType, AmbientTypeResolver resolver)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach(var prop in properties)
        {
            var indexParams = prop.GetIndexParameters();
            if(indexParams.Length == 0)
            {
                continue;
            }

            var resolvedType = resolver.Resolve(prop.PropertyType, memberType.Scope);
            if(resolvedType is ErrorType or NotProjectedType)
            {
                continue;
            }

            var parameters = ParameterProjector.Project(indexParams, memberType.Scope, resolver);
            if(parameters is null)
            {
                continue;
            }

            var getter = prop.GetMethod is { IsPublic: true } ? new PropertyAccessor() : null;
            var setter = prop.SetMethod is { IsPublic: true } ? new PropertyAccessor() : null;

            var modifiers = new MemberModifiers
            {
                IsPublic = true,
                IsStatic = (prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic) == true,
            };

            var projected = new Indexer(resolvedType, parameters, modifiers)
            {
                Getter = getter,
                Setter = setter,
            };

            AttributeProjector.Project(prop, projected.AttributesList, memberType.Scope, resolver);
            memberType.IndexersList.Add(projected);
        }
    }
}