using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySpecFragments
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Readonly = Public with { IsReadonly = true };
    private static readonly MemberModifiers Pure = Readonly with { IsPure = true };
    private static readonly MemberModifiers PureStatic = Pure with { IsStatic = true };
    private static readonly MemberModifiers Local = Public with { IsLocal = true };
    private static readonly MemberModifiers LocalReadonly = Local with { IsReadonly = true };
    private static readonly MemberModifiers Static = Public with { IsStatic = true };

    private readonly HandBuiltModel model = new();
    private readonly ClassType integer;
    private readonly ClassType text;
    private readonly ClassType boolean;
    private readonly ClassType unit;
    private readonly ClassType host = HandBuiltModel.Class("Host");
    private readonly ClassType globals = HandBuiltModel.Class("Globals");
    private readonly Field counter;
    private readonly Field limit;

    public PuritySpecFragments()
    {
        integer = Primitive("Int32");
        text = Primitive("String");
        boolean = Primitive("Boolean");
        unit = Primitive("Void");
        counter = Field(globals, "Counter", integer, isStatic: true);
        limit = Field(globals, "Limit", integer, isStatic: true, isReadonly: true);
    }

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        return Inference(name) ?? Contracts(name) ?? Functions(name) ?? Defaults(name) ?? Getters(name) ?? Returns(name)
            ?? throw new ArgumentOutOfRangeException(nameof(name));
    }

    private ClassType Primitive(string name)
    {
        var type = model.Type(name);
        type.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        return type;
    }

    private ConstructedType ArrayOfInt()
    {
        return model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [integer, model.Interner.GetOrCreateUnknownValue(integer)]);
    }

    private FunctionType Function(bool pure, IMistType returns, params IMistType[] parameters)
    {
        return model.Interner.GetOrCreateFunctionType(parameters, returns, pure);
    }

    private IReadOnlyList<Diagnostic> Judge(IProgramAnalyzer analyzer, Method member, params MistType[] types)
    {
        return model.Analyze(analyzer, member.Body, types, member.DeclaringType, member);
    }

    private LiteralExpression Number(long value)
    {
        return Literal(integer, value);
    }

    private BinaryExpression Arithmetic(Expression left, BinaryOperatorKind kind, Expression right)
    {
        return new BinaryExpression(Node(), left, kind, right, integer);
    }

    private BinaryExpression Compare(Expression left, BinaryOperatorKind kind, Expression right)
    {
        return new BinaryExpression(Node(), left, kind, right, boolean);
    }

    private static AbstractType Contract(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }

    private static ClassType Implementer(string name, AbstractType contract, bool declared = false)
    {
        var type = declared
            ? new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(37))
            : HandBuiltModel.Class(name);
        type.BaseAbstractTypes.Add(contract);
        return type;
    }

    private static Field Field(ConcreteType owner, string name, IMistType type, bool isStatic = false, bool isReadonly = false)
    {
        var field = new Field(name, type, OwnershipKind.Unrestricted, Public with { IsStatic = isStatic, IsReadonly = isReadonly }) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static Property AutoProperty(ConcreteType owner, string name, IMistType type, bool settable)
    {
        var property = new Property(name, type, Public)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = settable ? new PropertyAccessor { IsAuto = true } : null,
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    private static Method Method(MemberType owner, string name, IMistType returns, MemberModifiers modifiers, Block? body, params LocalVariable[] inputs)
    {
        var method = new Method(name, returns, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = owner, Body = body };
        owner.MethodsList.Add(method);
        return method;
    }

    private static Constructor Constructor(ConcreteType owner, MemberModifiers modifiers, Block body, params LocalVariable[] inputs)
    {
        var constructor = new Constructor(owner.Name, owner, [.. inputs.Select(input => input.Parameter!)], modifiers) { DeclaringType = owner, Body = body };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static LocalVariable Input(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        return new LocalVariable(name, type)
        {
            Ownership = ownership,
            Parameter = new Parameter(name, type, new ParameterModifier(false, false, ownership)),
        };
    }

    private static LocalVariable Variable(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted, bool isReadonly = false)
    {
        return new LocalVariable(name, type) { Ownership = ownership, IsReadonly = isReadonly };
    }

    private static SyntaxNode Node(int offset = 0)
    {
        return HandBuiltModel.Node(offset);
    }

    private static LiteralExpression Literal(IMistType type, object? value = null)
    {
        return HandBuiltModel.Literal(type, value);
    }

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(Node(offset), local);
    }

    private static FieldReferenceExpression Own(Field field, int offset = 0)
    {
        return new FieldReferenceExpression(Node(offset), field);
    }

    private static MemberAccessExpression Access(Expression receiver, Field field, int offset = 0)
    {
        return new MemberAccessExpression(Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private static MemberAccessExpression Read(Expression receiver, Property property, int offset = 0)
    {
        return new MemberAccessExpression(Node(offset), receiver, property.Name, property.Type, member: new PropertyResult(property, receiver.Type, []));
    }

    private static MemberAccessExpression StaticRead(Field field, int offset = 0)
    {
        var owner = field.DeclaringType!;
        return new MemberAccessExpression(Node(offset), new TypeReferenceExpression(Node(), owner), field.Name, field.Type, member: new FieldResult(field, owner, []));
    }

    private static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new AssignmentExpression(Node(), target, value, target.Type);
    }

    private static ExpressionStatement Set(Expression target, Expression value)
    {
        return new ExpressionStatement(Assign(target, value));
    }

    private static LocalDeclarationStatement Declare(LocalVariable local, Expression initializer)
    {
        return new LocalDeclarationStatement(local, initializer);
    }

    private static ReturnStatement Return(Expression value)
    {
        return new ReturnStatement(value, value.Type);
    }

    private static Block Returning(Expression value)
    {
        return HandBuiltModel.Block(Return(value));
    }

    private static IndexExpression Element(LocalVariable array, IMistType elementType, int offset = 0)
    {
        return new IndexExpression(Node(offset), Ref(array), HandBuiltModel.Literal(elementType, 0L), elementType);
    }

    private static NewExpression Alloc(ConcreteType type, Constructor? constructor, int offset = 0, params Expression[] arguments)
    {
        return new NewExpression(Node(offset), type, arguments, constructor, [], constructor?.ParametersList.Select(parameter => parameter.Type).ToList());
    }

    private static InvocationExpression Call(Method method, Expression? receiver = null, int offset = 0, params Expression[] arguments)
    {
        Expression callee = receiver is null
            ? new MethodReferenceExpression(Node(), method)
            : new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(Node(offset), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static InvocationExpression Invoke(LocalVariable function, int offset = 0, params Expression[] arguments)
    {
        var type = (FunctionType)function.Type;
        return new InvocationExpression(Node(offset), Ref(function), arguments, type.ReturnType, null, [.. type.ParameterTypes]);
    }
}