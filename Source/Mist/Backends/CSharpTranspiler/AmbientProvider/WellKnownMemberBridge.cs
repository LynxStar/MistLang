using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Adds the CLR members of a bridged well-known type to the Mist declaration that stands in for it,
/// so that <c>"x".ToUpper()</c> resolves without Standard having to redeclare the BCL.
/// <para>
/// PROJECTED INTO A SCRATCH TYPE AND THEN MERGED, rather than projected onto the target directly.
/// The projectors append without checking what is already there, and the two collisions that
/// produces are not alike: a duplicate METHOD is deduped by signature at lookup and silently drops
/// whichever came second, while a duplicate PROPERTY has no dedupe at all and turns every read of
/// it into MIST_PM_AMBIGUOUS_MEMBER. Standard.String declares Length, so projecting onto it
/// directly would break the one member of it that works today.
/// </para>
/// <para>
/// Only methods and properties. Base types, interfaces, operators, constructors, cast operators and
/// type parameters are all deliberately absent: the Mist declaration is the authority on what the
/// type IS, and this only widens what can be CALLED on it. BaseTypeProjector in particular assigns
/// rather than appends, so running it here would replace a hand-written base with System.Object.
/// </para>
/// </summary>
internal static class WellKnownMemberBridge
{
    public static void Bridge(MemberType target, Type clrType, AmbientTypeResolver resolver)
    {
        if(TypeProjector.CreateShell(clrType, target.NamespaceName) is not MemberType scratch)
        {
            return;
        }

        MethodProjector.Project(scratch, clrType, resolver);
        PropertyProjector.Project(scratch, clrType, resolver);

        var declaredProperties = new HashSet<string>(
            target.PropertiesList.Select(property => property.Name), StringComparer.Ordinal);

        foreach(var property in scratch.PropertiesList)
        {
            if(declaredProperties.Add(property.Name))
            {
                target.PropertiesList.Add(property);
            }
        }

        var declaredMethods = new HashSet<string>(
            target.MethodsList.Select(SignatureKey), StringComparer.Ordinal);

        foreach(var method in scratch.MethodsList)
        {
            if(declaredMethods.Add(SignatureKey(method)))
            {
                target.MethodsList.Add(method);
            }
        }
    }

    /// <summary>
    /// Name, arity and parameter types, matching what overload lookup keys on. The return type is
    /// excluded for the same reason it is there: C# does not overload on it, so two members that
    /// differ only in return type are one member as far as a call site is concerned -- and the Mist
    /// declaration is the one to keep, since <c>owned String Clone()</c> and the CLR's
    /// <c>object Clone()</c> collide exactly this way.
    /// </summary>
    private static string SignatureKey(Method method)
    {
        var parameters = string.Join(
            ",", method.ParametersList.Select(parameter => parameter.Type.FullyQualifiedName));

        return $"{method.Name}`{method.TypeParametersList.Count}({parameters})";
    }
}