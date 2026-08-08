using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A single entry in a default block. Always targets a field, and provides
/// either an expression value or a type binding with nested entries.
/// </summary>
internal abstract class DefaultEntry
{
    /// <summary>
    /// The field name this entry targets.
    /// </summary>
    public string FieldName { get; }

    protected DefaultEntry(string fieldName)
    {
        FieldName = fieldName;
    }
}

/// <summary>
/// A simple field override: <c>fieldName = expression</c>.
/// </summary>
internal sealed class DefaultValueEntry : DefaultEntry
{
    /// <summary>
    /// The initializer expression. Parked unbound by the type builder and replaced in Phase 3, the
    /// same two-step a field initializer takes, because binding it needs the enclosing type's scope.
    /// </summary>
    public Expression Value { get; internal set; }

    public DefaultValueEntry(string fieldName, Expression value) : base(fieldName)
    {
        Value = value;
    }
}

/// <summary>
/// A type binding entry: <c>fieldName : Type { nested entries }</c>.
/// Binds a field to a type and provides nested default entries for that type's fields.
/// </summary>
internal sealed class DefaultBindingEntry : DefaultEntry
{
    /// <summary>
    /// The bound type reference, or null if not yet resolved.
    /// </summary>
    public IMistType? BoundType { get; }

    /// <summary>
    /// Nested default entries for the bound type's fields.
    /// Each nested entry is itself a <see cref="DefaultEntry"/>.
    /// </summary>
    public IReadOnlyList<DefaultEntry> NestedEntries { get; }

    /// <summary>
    /// The AST node for the type binding, preserved for later type resolution.
    /// </summary>
    public DefaultTypeBindingDefaultFieldInitializerNode BindingNode { get; }

    public DefaultBindingEntry(
        string fieldName,
        IMistType? boundType,
        IReadOnlyList<DefaultEntry> nestedEntries,
        DefaultTypeBindingDefaultFieldInitializerNode bindingNode)
        : base(fieldName)
    {
        BoundType = boundType;
        NestedEntries = nestedEntries;
        BindingNode = bindingNode;
    }
}