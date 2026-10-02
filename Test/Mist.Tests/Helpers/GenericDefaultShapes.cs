using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

/// <summary>
/// Hand-built generic declarations for the rows judging a constructed type's default: <c>Box</c> holds
/// one slot of its type parameter, <c>Connection</c> has a constructor and no default block, and
/// <c>Inner</c> has no constructor and an initialized slot.
/// </summary>
internal sealed class GenericDefaultShapes(List<MistType>? registry = null)
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    public List<MistType> Types { get; } = registry ?? [];

    public ClassType Integer { get; } = new("Int32", "Standard", new Scope(null), null, null)
    {
        Modifiers = Public with { IsIntrinsic = true },
    };

    public ConcreteType Type(string name, bool isStruct = false, MistType? baseType = null, bool isIntrinsic = false)
    {
        ConcreteType type = isStruct
            ? new StructType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            : new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
        type.Modifiers = Public with { IsIntrinsic = isIntrinsic };
        type.BaseType = baseType;
        Types.Add(type);
        return type;
    }

    public static TypeParameter Parameter(ConcreteType owner, string name = "T", bool defaultConstrained = false)
    {
        var parameter = new TypeParameter(name, owner.TypeParametersList.Count, owner.Scope, owner);
        owner.TypeParametersList.Add(parameter);

        if(defaultConstrained)
        {
            parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        }

        return parameter;
    }

    public static ConstructedType Construct(IMistType definition, params IMistType[] arguments)
    {
        return new ConstructedType(definition, arguments);
    }

    public static Field Slot(ConcreteType owner, string name, IMistType type, Expression? initializer = null)
    {
        var field = new Field(name, type, OwnershipKind.Unrestricted, Public)
        {
            Initializer = initializer,
            DeclarationNode = HandBuiltModel.Node(40),
        };
        owner.FieldsList.Add(field);
        return field;
    }

    public static Constructor Constructor(ConcreteType owner, params IMistType[] parameters)
    {
        var constructor = new Constructor(owner.Name, owner, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), Public)
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(20),
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    public static void Block(ConcreteType owner, params DefaultEntry[] entries)
    {
        owner.DefaultBlock = new DefaultBlock([.. entries], DeclarationFragments.At<DefaultBlockDeclarationNode>());
    }

    public ConcreteType Box(string shape = "constructorless", bool isStruct = false, string name = "Box")
    {
        var box = Type(name, isStruct);
        _ = Slot(box, "Value", Parameter(box));

        if(shape != "constructorless")
        {
            _ = Constructor(box);
        }

        if(shape == "constructor-and-a-block")
        {
            Block(box);
        }

        return box;
    }

    public ConcreteType Connection()
    {
        var connection = Type("Connection");
        _ = Constructor(connection, Integer);
        return connection;
    }

    public ConcreteType Inner()
    {
        var inner = Type("Inner");
        _ = Slot(inner, "Seven", Integer, HandBuiltModel.Literal(Integer, 7L));
        return inner;
    }
}