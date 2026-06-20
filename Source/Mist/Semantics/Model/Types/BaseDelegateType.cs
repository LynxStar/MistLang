using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// The Mist type representing <c>System.Delegate</c>, the root base type of all CLR delegates.
/// </summary>
/// <remarks>
/// <para>
/// <c>BaseDelegateType</c> is a singleton that models the <c>System.Delegate</c> top type in
/// the delegate hierarchy. Any <see cref="DelegateType"/> value and any <c>func&lt;...&gt;</c>
/// value is assignable to <c>BaseDelegateType</c>, but <c>BaseDelegateType</c> itself cannot
/// be narrowed back to a specific delegate or function type without an explicit cast.
/// </para>
/// <para>
/// <c>System.MulticastDelegate</c> is NOT modeled as a separate type — the hierarchy is
/// flattened to <c>DelegateType → BaseDelegateType</c>.
/// </para>
/// <para>
/// <c>BaseDelegateType</c> is not directly invocable. Attempting to invoke a value of this
/// type produces an error diagnostic suggesting a cast to a concrete delegate or func type.
/// </para>
/// </remarks>
public sealed class BaseDelegateType : IType
{
    public static readonly BaseDelegateType Instance = new();

    public string DisplayName => "Delegate";
    public TypeKind Kind => TypeKind.Class;

    /// <summary>
    /// The member scope containing <c>System.Delegate</c>'s own members
    /// (e.g., <c>GetType()</c>, <c>DynamicInvoke(object?[])</c>, <c>Method</c>, <c>Target</c>).
    /// </summary>
    /// <remarks>
    /// This property is populated during type projection when the <c>System.Delegate</c> type
    /// is encountered in a referenced assembly. It is <c>null!</c> until projection completes.
    /// </remarks>
    public IScope MemberScope { get; internal set; } = null!;

    private BaseDelegateType() { }
}
