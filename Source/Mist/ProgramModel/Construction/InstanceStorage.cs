using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// One slot of an instance's state: an instance field, or the backing field of an instance property.
/// A property has one when an accessor is automatic or names <c>field</c>, except a lone getter with a
/// body, which is computed and has none (properties Requirement 7). Its initializer is the backing
/// field's (defaultability 6.1), so a default and a constructor answer both kinds alike.
/// </summary>
internal sealed record InstanceStorage
{
    private InstanceStorage(Field? field, Property? property)
    {
        Field = field;
        Property = property;
    }

    public Field? Field { get; }

    public Property? Property { get; }

    public object Member => (object?)Field ?? Property!;

    public string Name => Field?.Name ?? Property!.Name;

    public IMistType Type => Field?.Type ?? Property!.Type;

    public Expression? Initializer => Field is not null ? Field.Initializer : Property!.Initializer;

    public SyntaxNode? DeclarationNode => Field is not null ? Field.DeclarationNode : Property!.DeclarationNode;

    public static IEnumerable<InstanceStorage> Of(ConcreteType type)
    {
        foreach(var field in type.FieldsList.Where(field => !field.Modifiers.IsStatic))
        {
            yield return new InstanceStorage(field, null);
        }

        foreach(var property in type.PropertiesList.Where(IsStorage))
        {
            yield return new InstanceStorage(null, property);
        }
    }

    private static bool IsStorage(Property property)
    {
        return property is { Modifiers.IsStatic: false, Modifiers.IsIntrinsic: false, HasBackingStorage: true }
            && (property.Setter is not null || property.Getter is { IsAuto: true });
    }
}