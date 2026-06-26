using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents a named CLR delegate type (e.g., <c>RequestDelegate</c>, <c>Predicate&lt;T&gt;</c>)
/// that preserves its CLR identity through projection, type checking, and emission.
/// Carries both the nominal CLR name and the equivalent <c>func&lt;...&gt;</c> signature
/// for structural comparison.
/// </summary>
public sealed class DelegateType : IType, IEquatable<DelegateType>
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Class;

    /// <summary>
    /// The fully qualified CLR type name (e.g., "System.Predicate", "Microsoft.AspNetCore.Http.RequestDelegate").
    /// Used by the emitter to produce the correct C# type name.
    /// </summary>
    public string ClrFullName { get; }

    /// <summary>
    /// The CLR namespace (e.g., "System", "Microsoft.AspNetCore.Http").
    /// </summary>
    public string ClrNamespace { get; }

    /// <summary>
    /// The simple CLR type name without namespace or generic arity suffix
    /// (e.g., "Predicate", "RequestDelegate").
    /// </summary>
    public string ClrSimpleName { get; }

    /// <summary>
    /// The equivalent func&lt;...&gt; type as a ConstructedType wrapping Standard.Func.
    /// The semantic model uses this for structural signature comparison.
    /// </summary>
    public ConstructedType FuncSignature { get; }

    /// <summary>
    /// Generic type parameters for open generic delegates (e.g., Predicate&lt;T&gt; has one TypeParameterType).
    /// Empty for non-generic delegates (e.g., RequestDelegate).
    /// </summary>
    public IReadOnlyList<IType> TypeParameters { get; }

    /// <summary>
    /// Concrete type arguments when this is a constructed generic delegate (e.g., Predicate&lt;int&gt;).
    /// Empty for non-generic delegates and open generic definitions.
    /// </summary>
    public IReadOnlyList<IType> TypeArguments { get; }

    /// <summary>
    /// The member scope containing members inherited from System.Delegate
    /// (GetType(), DynamicInvoke(), Method, Target, etc.).
    /// </summary>
    public IScope MemberScope { get; }

    public DelegateType(
        string clrFullName,
        string clrNamespace,
        string clrSimpleName,
        ConstructedType funcSignature,
        IReadOnlyList<IType> typeParameters,
        IReadOnlyList<IType> typeArguments,
        IScope memberScope)
    {
        ClrFullName = clrFullName;
        ClrNamespace = clrNamespace;
        ClrSimpleName = clrSimpleName;
        FuncSignature = funcSignature;
        TypeParameters = typeParameters;
        TypeArguments = typeArguments;
        MemberScope = memberScope;

        // Build display name: "Predicate<int>" or "RequestDelegate"
        DisplayName = typeArguments.Count > 0
            ? $"{clrSimpleName}<{string.Join(", ", typeArguments.Select(t => t.DisplayName))}>"
            : clrSimpleName;
    }

    /// <summary>
    /// Equality is based on CLR full name + type arguments match.
    /// Two DelegateType instances are equal when they represent the same nominal delegate
    /// with the same concrete type arguments.
    /// </summary>
    public bool Equals(DelegateType? other)
    {
        if(other is null)
        {
            return false;
        }

        if(ReferenceEquals(this, other))
        {
            return true;
        }

        if(ClrFullName != other.ClrFullName)
        {
            return false;
        }

        if(TypeArguments.Count != other.TypeArguments.Count)
        {
            return false;
        }

        for(var i = 0; i < TypeArguments.Count; i++)
        {
            if(!ReferenceEquals(TypeArguments[i], other.TypeArguments[i])
                && TypeArguments[i] != other.TypeArguments[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as DelegateType);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ClrFullName);
        foreach(var arg in TypeArguments)
        {
            hash.Add(arg);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(DelegateType? left, DelegateType? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(DelegateType? left, DelegateType? right)
        => !(left == right);
}