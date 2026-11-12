using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model;

internal abstract class DefaultEntry
{
    public string FieldName { get; }

    protected DefaultEntry(string fieldName)
    {
        FieldName = fieldName;
    }
}

internal sealed class DefaultValueEntry : DefaultEntry
{
    /// <summary>
    /// Parked unbound by the type builder and replaced in Phase 3, as a field initializer is, because
    /// binding it needs the enclosing type's scope.
    /// </summary>
    public Expression Value { get; internal set; }

    public DefaultValueEntry(string fieldName, Expression value) : base(fieldName)
    {
        Value = value;
    }
}

/// <summary>
/// <c>fieldName : Type { nested entries }</c>
/// </summary>
internal sealed class DefaultBindingEntry : DefaultEntry
{
    /// <summary>
    /// Null when the type did not resolve.
    /// </summary>
    public IMistType? BoundType { get; }

    public IReadOnlyList<DefaultEntry> NestedEntries { get; }

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