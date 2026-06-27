using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Determines method-call visibility for receivers held in owned bindings
/// (Requirement 2, Requirement 3).
/// <list type="bullet">
///   <item>Owned type receiver → all methods visible (cooperation contract verified).</item>
///   <item>Non-owned type receiver → readonly methods only; mutable methods hidden.</item>
///   <item>Value-type receiver → all methods visible (the struct is copied, so a
///         mutable method cannot break ownership invariants of the original).</item>
///   <item>Receivers NOT held in owned bindings are unaffected (full access).</item>
/// </list>
/// This component is pure: it returns a verdict only. The owning analyzer is
/// responsible for emitting any diagnostic (e.g. <c>MIST_OWN_MUTABLE_HIDDEN</c>)
/// when the verdict is <see cref="MethodVisibility.HiddenMutable"/>.
/// </summary>
public sealed class MethodTierVisibilityFilter
{
    /// <summary>
    /// Resolves whether <paramref name="methodSymbol"/> may be called on a receiver
    /// of <paramref name="receiverType"/>.
    /// </summary>
    /// <param name="methodSymbol">The method being called.</param>
    /// <param name="receiverType">The static type of the receiver.</param>
    /// <param name="receiverIsOwnedBinding">
    /// Whether the receiver is held in an owned binding. When <c>false</c>, the call
    /// is unaffected by ownership tiering and is always <see cref="MethodVisibility.Allowed"/>.
    /// </param>
    /// <param name="model">The semantic model (reserved for future resolution needs).</param>
    /// <returns>
    /// <see cref="MethodVisibility.Allowed"/> if the call is permitted, or
    /// <see cref="MethodVisibility.HiddenMutable"/> if the method is a mutable method
    /// hidden because a non-owned type is held in an owned binding (Req 2 AC 2/4).
    /// </returns>
    public static MethodVisibility Resolve(
        ISymbol methodSymbol,
        IType receiverType,
        bool receiverIsOwnedBinding)
    {
        // A receiver that is not an owned binding behaves exactly as today —
        // full method access (Req 2/3 decision table, last row).
        if(!receiverIsOwnedBinding)
        {
            return MethodVisibility.Allowed;
        }

        // Value-type receivers are copied on access, so a mutable method on the copy
        // cannot break the original's ownership invariants. All methods are visible
        // (consistent with Req 4 AC 3 value-copy reasoning).
        if(IsValueType(receiverType))
        {
            return MethodVisibility.Allowed;
        }

        // Owned types grant full method access — the cooperation contract guarantees
        // internal mutations maintain ownership invariants (Req 3 AC 1).
        if(IsOwnedType(receiverType))
        {
            return MethodVisibility.Allowed;
        }

        // Non-owned reference type in an owned binding: readonly methods are visible
        // (Req 2 AC 1); mutable methods are hidden (Req 2 AC 2/4).
        return IsReadonlyMethod(methodSymbol)
            ? MethodVisibility.Allowed
            : MethodVisibility.HiddenMutable;
    }

    /// <summary>
    /// Classifies a method as readonly vs mutable by reusing the readonly status the
    /// <see cref="ReadonlyInferenceEngine"/> computed on the symbol. No new readonly
    /// machinery is introduced here.
    /// </summary>
    private static bool IsReadonlyMethod(ISymbol methodSymbol)
    {
        // ReadonlyStatus lives on the concrete Symbol. If the symbol is not a Symbol
        // (or is unclassified), conservatively treat it as mutable so that mutable
        // methods are never accidentally exposed on non-owned owned-bound receivers.
        if(methodSymbol is not Symbol symbol)
        {
            return false;
        }

        return symbol.ReadonlyStatus
            is ReadonlyStatus.ExplicitReadonly
            or ReadonlyStatus.InferredReadonly
            or ReadonlyStatus.ImplicitReadonly;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type is a value type (struct or enum).
    /// </summary>
    private static bool IsValueType(IType type)
    {
        return type.Kind is TypeKind.Struct or TypeKind.Enum;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type is declared as an owned type. For
    /// constructed generic types, the generic definition is consulted.
    /// </summary>
    private static bool IsOwnedType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false,
        };
    }
}

/// <summary>
/// The visibility verdict for a method call on an owned-bound receiver.
/// </summary>
public enum MethodVisibility
{
    /// <summary>The method call is permitted.</summary>
    Allowed,

    /// <summary>
    /// The method is a mutable method hidden because a non-owned type is held in an
    /// owned binding (Req 2 AC 2/4). The analyzer should emit
    /// <c>MIST_OWN_MUTABLE_HIDDEN</c>.
    /// </summary>
    HiddenMutable,
}