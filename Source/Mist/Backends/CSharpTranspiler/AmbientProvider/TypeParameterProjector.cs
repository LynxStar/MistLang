using System.Reflection;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Projects CLR generic type parameters and their constraints onto a Mist MemberType.
/// Uses an <see cref="AmbientTypeResolver"/> to look up constraint types.
/// </summary>
internal static class TypeParameterProjector
{
    /// <summary>
    /// Projects CLR generic type parameters onto the projected type.
    /// Creates TypeParameter instances, adds them to the type's TypeParametersList,
    /// and registers them in the type's scope for name resolution.
    /// </summary>
    public static void Project(MemberType projectedType, Type clrType, AmbientTypeResolver resolver)
    {
        if(!clrType.IsGenericTypeDefinition)
        {
            return;
        }

        var clrTypeParams = clrType.GetGenericArguments();

        // Pass 1: Create all type parameters and register them in scope.
        // This must happen before constraint resolution so that cross-parameter
        // constraints (where T : U) can find their targets.
        for(var i = 0; i < clrTypeParams.Length; i++)
        {
            var clrParam = clrTypeParams[i];
            var typeParam = new TypeParameter(clrParam.Name, i, projectedType.Scope, projectedType);
            projectedType.TypeParametersList.Add(typeParam);
            projectedType.Scope.AddLocalType(clrParam.Name, typeParam);
        }

        // Pass 2: Resolve constraints now that all type parameters are in scope.
        for(var i = 0; i < clrTypeParams.Length; i++)
        {
            ProjectConstraints(projectedType.TypeParametersList[i], clrTypeParams[i], projectedType.Scope, resolver);
        }
    }

    /// <summary>
    /// Maps CLR generic parameter constraints to Mist TypeParameterConstraint instances.
    /// </summary>
    private static void ProjectConstraints(TypeParameter typeParam, Type clrParam, Scope scope, AmbientTypeResolver resolver)
    {
        var attributes = clrParam.GenericParameterAttributes;
        var specialConstraints = attributes & GenericParameterAttributes.SpecialConstraintMask;

        // struct constraint (NotNullableValueTypeConstraint)
        if((specialConstraints & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
        {
            typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
        }

        // class constraint (ReferenceTypeConstraint)
        if((specialConstraints & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
        {
            typeParam.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
        }

        // new() constraint (DefaultConstructorConstraint)
        if((specialConstraints & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
        {
            typeParam.ConstraintsList.Add(new ConstructorConstraint([]));
        }

        // Base type and interface constraints from GetGenericParameterConstraints().
        var constraintTypes = clrParam.GetGenericParameterConstraints();
        foreach(var constraintType in constraintTypes)
        {
            // System.ValueType appears as a constraint for struct-constrained params — skip it.
            if(constraintType.FullName == "System.ValueType")
            {
                continue;
            }

            var resolved = ResolveConstraintType(constraintType, scope, resolver);
            if(resolved is not null)
            {
                typeParam.ConstraintsList.Add(new TypeConstraint(resolved));
            }
        }
    }

    /// <summary>
    /// Resolves a CLR constraint type through the ambient resolver.
    /// Returns null for sentinel types (ErrorType, NotProjectedType) that
    /// should not become constraints.
    /// </summary>
    private static MistType? ResolveConstraintType(Type constraintType, Scope scope, AmbientTypeResolver resolver)
    {
        var resolved = resolver.Resolve(constraintType, scope);
        if(resolved is ErrorType or NotProjectedType)
        {
            return null;
        }

        return resolved;
    }
}