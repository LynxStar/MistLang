using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Infers type arguments for generic method calls by unifying declared parameter
/// types against resolved argument types. This is a pure static class with no
/// mutable state, designed to be testable in isolation.
/// </summary>
public static class TypeArgumentInferenceEngine
{
    /// <summary>
    /// Attempts to infer all type arguments for a generic method call.
    /// </summary>
    /// <param name="typeParameters">The method's type parameters (from Symbol.TypeParameters).</param>
    /// <param name="parameterTypes">The declared types of the method's parameters.</param>
    /// <param name="argumentTypes">The resolved types of the arguments at the call site.</param>
    /// <returns>An InferenceResult indicating success or failure with details.</returns>
    public static InferenceResult TryInfer(
        IReadOnlyList<IType> typeParameters,
        IReadOnlyList<IType> parameterTypes,
        IReadOnlyList<IType> argumentTypes)
    {
        var context = new InferenceContext(typeParameters);

        var pairCount = Math.Min(parameterTypes.Count, argumentTypes.Count);
        for(var i = 0; i < pairCount; i++)
        {
            var argumentType = argumentTypes[i];

            // Skip arguments whose resolved type is ErrorType, null, or UnknownType.
            if(argumentType is null or ErrorType or UnknownType)
            {
                continue;
            }

            Unify(parameterTypes[i], argumentType, context);
        }

        // Check the context for completeness.
        if(context.HasConflicts)
        {
            return new InferenceResult(
                Success: false,
                InferredTypeArguments: null,
                UninferrableTypeParameters: null,
                Conflicts: context.Conflicts);
        }

        var unbound = context.GetUnboundParameters();
        if(unbound.Count > 0)
        {
            return new InferenceResult(
                Success: false,
                InferredTypeArguments: null,
                UninferrableTypeParameters: unbound,
                Conflicts: null);
        }

        // All type parameters are bound — collect inferred types in declaration order.
        var inferredTypes = new IType[typeParameters.Count];
        for(var i = 0; i < typeParameters.Count; i++)
        {
            if(typeParameters[i] is TypeParameterType tpt)
            {
                inferredTypes[i] = context.GetBinding(tpt.Name)!;
            }
            else
            {
                inferredTypes[i] = typeParameters[i];
            }
        }

        return new InferenceResult(
            Success: true,
            InferredTypeArguments: inferredTypes,
            UninferrableTypeParameters: null,
            Conflicts: null);
    }

    /// <summary>
    /// Unifies a declared parameter type against an argument type, binding type parameters
    /// in the context as they are discovered.
    /// </summary>
    /// <param name="declaredParamType">The declared type of the method parameter.</param>
    /// <param name="argumentType">The resolved type of the argument at the call site.</param>
    /// <param name="context">The mutable inference context accumulating bindings.</param>
    private static void Unify(IType declaredParamType, IType argumentType, InferenceContext context)
    {
        // Case 1: The declared parameter type is a type parameter — bind it directly.
        if(declaredParamType is TypeParameterType tpt)
        {
            _ = context.TryBind(tpt.Name, argumentType);
            return;
        }

        // Case 2: Both are ConstructedTypes with the same GenericDefinition — recurse.
        if(declaredParamType is ConstructedType declaredConstructed
            && argumentType is ConstructedType argumentConstructed
            && ReferenceEquals(declaredConstructed.GenericDefinition, argumentConstructed.GenericDefinition))
        {
            var count = Math.Min(
                declaredConstructed.TypeArguments.Count,
                argumentConstructed.TypeArguments.Count);

            for(var i = 0; i < count; i++)
            {
                Unify(declaredConstructed.TypeArguments[i], argumentConstructed.TypeArguments[i], context);
            }

            return;
        }

        // Case 3: Both are ArrayTypes — recurse into element types.
        // This handles patterns like T[] matched against string[] to infer T = string.
        if(declaredParamType is ArrayType declaredArray && argumentType is ArrayType argumentArray)
        {
            Unify(declaredArray.ElementType, argumentArray.ElementType, context);
            return;
        }

        // Case 4: Declared parameter type is a UnionType (e.g., T[] | void from nullable T[]?).
        // Try to unify against each non-void constituent. This handles the common pattern
        // where CLR nullable reference types are projected as T | void unions.
        if(declaredParamType is UnionType declaredUnion)
        {
            foreach(var constituent in declaredUnion.Constituents)
            {
                if(constituent.IsVoid())
                {
                    continue;
                }

                Unify(constituent, argumentType, context);
            }

            return;
        }

        // Case 5: Argument type is a UnionType (e.g., List<WI10> | void from nullable projection).
        // Unwrap to non-void constituent before unification.
        if(argumentType is UnionType argumentUnion)
        {
            foreach(var constituent in argumentUnion.Constituents)
            {
                if(constituent.IsVoid())
                {
                    continue;
                }

                Unify(declaredParamType, constituent, context);
                return;
            }

            return;
        }

        // Case 6: Expression<T> unwrapping — when the declared param type is
        // Expression<Func<...>> (a CLR Expression tree type), unwrap to the inner
        // Func type and unify against the argument. This enables type inference from
        // lambdas passed to Expression<Func<TEntity, TResult>> parameters.
        if(declaredParamType is ConstructedType exprCt
            && exprCt.GenericDefinition is ClassType { DisplayName: "Expression" }
            && exprCt.TypeArguments.Count == 1)
        {
            Unify(exprCt.TypeArguments[0], argumentType, context);
            return;
        }

        // Case 7: Abstract type implementation unification — when the declared param type
        // is an abstract type (e.g., IEnumerable<TRelated>) and the argument type is a class
        // that implements that abstract type (e.g., List<WI10>), find the matching abstract type
        // implementation and unify type arguments.
        if(declaredParamType is ConstructedType declaredIfaceCt
            && declaredIfaceCt.GenericDefinition is AbstractType declaredIface
            && argumentType is ConstructedType argumentClassCt
            && argumentClassCt.GenericDefinition is ClassType argumentClassDef)
        {
            // Search the argument class's abstracts for one that matches the declared abstract type.
            foreach(var abs in argumentClassDef.Abstracts)
            {
                if(abs is ConstructedType absCt
                    && ReferenceEquals(absCt.GenericDefinition, declaredIface))
                {
                    // Found matching abstract type — substitute the class's type args
                    // into the abstract type's type args, then unify against the declared params.
                    var substitutedIfaceArgs = new IType[absCt.TypeArguments.Count];
                    for(var i = 0; i < absCt.TypeArguments.Count; i++)
                    {
                        var absArg = absCt.TypeArguments[i];
                        if(absArg is TypeParameterType absTpt
                            && argumentClassDef.TypeParameters.Count > 0)
                        {
                            // Map the class's type param to the concrete type arg.
                            for(var j = 0; j < argumentClassDef.TypeParameters.Count; j++)
                            {
                                if(argumentClassDef.TypeParameters[j] is TypeParameterType classTP
                                    && classTP.Name == absTpt.Name && classTP.Ordinal == absTpt.Ordinal)
                                {
                                    absArg = j < argumentClassCt.TypeArguments.Count
                                        ? argumentClassCt.TypeArguments[j]
                                        : absArg;
                                    break;
                                }
                            }
                        }

                        substitutedIfaceArgs[i] = absArg;
                    }

                    // Unify declared abstract type args against the resolved abstract type args.
                    var count = Math.Min(declaredIfaceCt.TypeArguments.Count, substitutedIfaceArgs.Length);
                    for(var i = 0; i < count; i++)
                    {
                        Unify(declaredIfaceCt.TypeArguments[i], substitutedIfaceArgs[i], context);
                    }

                    return;
                }
            }
        }

        // Case 8: ConstructedType with different GenericDefinitions but same display name.
        // This handles cases where the same CLR type is projected through different paths
        // (e.g., Func from stdlib vs Func from CLR projection).
        if(declaredParamType is ConstructedType declaredCt2
            && argumentType is ConstructedType argumentCt2
            && !ReferenceEquals(declaredCt2.GenericDefinition, argumentCt2.GenericDefinition)
            && declaredCt2.GenericDefinition.DisplayName == argumentCt2.GenericDefinition.DisplayName
            && declaredCt2.TypeArguments.Count == argumentCt2.TypeArguments.Count)
        {
            for(var i = 0; i < declaredCt2.TypeArguments.Count; i++)
            {
                Unify(declaredCt2.TypeArguments[i], argumentCt2.TypeArguments[i], context);
            }

            return;
        }

        // Case 9: No bindings produced from this pair — skip silently.
    }
}