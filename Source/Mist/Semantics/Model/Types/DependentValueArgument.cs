using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents a concrete dependent value in a constructed type (e.g., the <c>5</c> in <c>FixedBuffer&lt;5&gt;</c>).
/// <para>
/// A dependent value is usually a scalar comptime constant (an <see cref="int"/>, <see cref="bool"/>,
/// <see cref="char"/>, ...). A type-parameter pack extends the same machinery to a comptime, ordered
/// <c>Type[]</c> value: the <c>[int, string]</c> bound to the pack slot of a constructed variadic type
/// such as <c>Foo&lt;[int, string]&gt;</c>. A pack argument is created with <see cref="ForPack"/> and
/// carries its ordered element types in <see cref="PackElementTypes"/>.
/// </para>
/// </summary>
public sealed class DependentValueArgument
{
    /// <summary>The compile-time constant value. For a pack this is the ordered element-type list.</summary>
    public object Value { get; }

    /// <summary>The type of the value (int, bool, char, etc.); the <c>Type[]</c> metatype for a pack.</summary>
    public IType ValueType { get; internal set; }

    /// <summary>Display string for diagnostics (e.g., "5", "true", "'x'", "[int, string]").</summary>
    public string DisplayValue { get; }

    /// <summary>
    /// When non-null, this dependent argument is the comptime <c>Type[]</c> value bound to a
    /// type-parameter pack's slot in a constructed variadic type — the ordered array of element
    /// types (for example the <c>[int, string]</c> in <c>Foo&lt;[int, string]&gt;</c>). This is the
    /// pack side of the dependent-type reduction: just as the scalar <c>5</c> in
    /// <c>FixedBuffer&lt;5&gt;</c> is a comptime value that indexes type identity, a pack binds a
    /// comptime <c>Type[]</c> value that does the same. Null for an ordinary scalar dependent value.
    /// <para>
    /// Two pack arguments are equal exactly when their element lists have the same length and are
    /// elementwise-equal under the type-equality relation used for ordinary type arguments, so the
    /// enclosing constructed type's identity is order-sensitive and arity-sensitive (the comparison
    /// itself lives with the type checker's dependent-argument equality).
    /// </para>
    /// </summary>
    public IReadOnlyList<IType>? PackElementTypes { get; private init; }

    /// <summary>
    /// True when this dependent argument carries a pack's comptime <c>Type[]</c> value
    /// (see <see cref="PackElementTypes"/>) rather than a scalar constant.
    /// </summary>
    public bool IsPack => PackElementTypes is not null;

    public DependentValueArgument(object value, IType valueType, string displayValue)
    {
        Value = value;
        ValueType = valueType;
        DisplayValue = displayValue;
    }

    /// <summary>
    /// Creates a dependent argument for a type-parameter pack slot: the comptime, ordered
    /// <c>Type[]</c> value bound to the pack (for example <c>[int, string]</c> in
    /// <c>Foo&lt;[int, string]&gt;</c>, or the empty pack in <c>Foo&lt;[]&gt;</c>). The enclosing
    /// <see cref="ConstructedType"/>'s identity then falls out of the existing constructed-type
    /// equality, which compares this argument elementwise and order-sensitively against the
    /// corresponding pack slot of another construction.
    /// </summary>
    /// <param name="elementTypes">The pack's element types, in order (empty for the empty pack).</param>
    /// <param name="packDomainType">The pack's value domain — the <c>Type[]</c> metatype.</param>
    /// <param name="displayValue">
    /// Optional display text; defaults to the bracketed element list (for example <c>[int, string]</c>).
    /// </param>
    public static DependentValueArgument ForPack(
        IReadOnlyList<IType> elementTypes,
        IType packDomainType,
        string? displayValue = null)
    {
        var display = displayValue ?? $"[{string.Join(", ", elementTypes.Select(t => t.DisplayName))}]";
        return new DependentValueArgument(elementTypes, packDomainType, display)
        {
            PackElementTypes = elementTypes,
        };
    }
}