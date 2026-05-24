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
        for (var i = 0; i < pairCount; i++)
        {
            var argumentType = argumentTypes[i];

            // Skip arguments whose resolved type is ErrorType, null, or UnknownType.
            if (argumentType is null or ErrorType or UnknownType)
                continue;

            Unify(parameterTypes[i], argumentType, context);
        }

        // Check the context for completeness.
        if (context.HasConflicts)
        {
            return new InferenceResult(
                Success: false,
                InferredTypeArguments: null,
                UninferrableTypeParameters: null,
                Conflicts: context.Conflicts);
        }

        var unbound = context.GetUnboundParameters();
        if (unbound.Count > 0)
        {
            return new InferenceResult(
                Success: false,
                InferredTypeArguments: null,
                UninferrableTypeParameters: unbound,
                Conflicts: null);
        }

        // All type parameters are bound — collect inferred types in declaration order.
        var inferredTypes = new IType[typeParameters.Count];
        for (var i = 0; i < typeParameters.Count; i++)
        {
            if (typeParameters[i] is TypeParameterType tpt)
                inferredTypes[i] = context.GetBinding(tpt.Name)!;
            else
                inferredTypes[i] = typeParameters[i];
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
        if (declaredParamType is TypeParameterType tpt)
        {
            context.TryBind(tpt.Name, argumentType);
            return;
        }

        // Case 2: Both are ConstructedTypes with the same GenericDefinition — recurse.
        if (declaredParamType is ConstructedType declaredConstructed
            && argumentType is ConstructedType argumentConstructed
            && ReferenceEquals(declaredConstructed.GenericDefinition, argumentConstructed.GenericDefinition))
        {
            var count = Math.Min(
                declaredConstructed.TypeArguments.Count,
                argumentConstructed.TypeArguments.Count);

            for (var i = 0; i < count; i++)
            {
                Unify(declaredConstructed.TypeArguments[i], argumentConstructed.TypeArguments[i], context);
            }

            return;
        }

        // Case 3: No bindings produced from this pair — skip silently.
    }
}
