using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyParameterReceiverTests
{
    private const string CallsMutable = "MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE";
    private const string FieldAssigned = "MIST_PA_FIELD_ASSIGNED_ON_READONLY_PARAMETER";
    private const string SetsProperty = "MIST_PA_READONLY_PARAMETER_SETS_PROPERTY";

    private readonly HandBuiltModel model = new();
    private readonly ClassType counter = HandBuiltModel.Class("Counter");
    private readonly ClassType integer;
    private readonly Field count;
    private readonly Field inner;
    private readonly Method bump;
    private readonly Method peek;
    private readonly Property level;
    private readonly Property fixedLevel;
    private readonly Property self;
    private readonly Indexer slots;

    public ReadonlyParameterReceiverTests()
    {
        integer = model.Type("Int32");
        count = Member(new Field("Count", integer, default, default) { DeclaringType = counter });
        inner = Member(new Field("Inner", counter, default, default) { DeclaringType = counter });
        var writes = HandBuiltModel.Block(new ExpressionStatement(Write(new FieldReferenceExpression(HandBuiltModel.Node(), count))));
        bump = new Method("Bump", integer, [], default) { Body = writes, DeclaringType = counter };
        peek = new Method("Peek", integer, [], new MemberModifiers { IsReadonly = true })
        {
            Body = HandBuiltModel.Block(new ReturnStatement(new FieldReferenceExpression(HandBuiltModel.Node(), count), integer)),
            DeclaringType = counter,
        };
        counter.MethodsList.AddRange([bump, peek]);
        level = Auto("Level", integer, default);
        fixedLevel = Auto("Fixed", integer, new MemberModifiers { IsReadonly = true });
        self = new Property("Self", counter, default)
        {
            Getter = new PropertyAccessor { Body = writes },
            DeclaringType = counter,
        };
        slots = new Indexer(integer, [new Parameter("index", integer)], default)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { Body = writes },
            DeclaringType = counter,
        };
    }

    [Theory]
    [InlineData("c.Inner.Bump()", CallsMutable, "Cannot call 'Bump' on 'c.Inner', reached through readonly parameter 'c': it is not readonly.")]
    [InlineData("c.Inner.Inner.Bump()", CallsMutable, "Cannot call 'Bump' on 'c.Inner.Inner', reached through readonly parameter 'c': it is not readonly.")]
    [InlineData("c.Self.Bump()", CallsMutable, "Cannot call 'Bump' on 'c.Self', reached through readonly parameter 'c': it is not readonly.")]
    [InlineData("c.Inner.Count = 1", FieldAssigned, "Cannot assign to field 'Count' of 'c.Inner', reached through readonly parameter 'c'.")]
    [InlineData("c.Inner.Inner.Count = 1", FieldAssigned, "Cannot assign to field 'Count' of 'c.Inner.Inner', reached through readonly parameter 'c'.")]
    [InlineData("c.Inner.Level = 1", SetsProperty, "Cannot set 'Level' on 'c.Inner', reached through readonly parameter 'c': a readonly parameter admits no setter.")]
    [InlineData("c.Inner[0] = 1", SetsProperty, "Cannot set 'this[]' on 'c.Inner', reached through readonly parameter 'c': a readonly parameter admits no setter.")]
    public void AMutationReachedThroughAReadonlyParameterIsRefusedNamingTheReceiver(string shape, string expected, string message)
    {
        var finding = Assert.Single(Analyze(shape, ParameterKind.Readonly));

        Assert.Equal(expected, finding.Id.Value);
        Assert.Equal(message, finding.Message);
        Assert.Equal(38, finding.Location.Start.Column);
    }

    [Theory]
    [InlineData("c.Bump()", "Cannot call 'Bump' on readonly parameter 'c': it is not readonly.")]
    [InlineData("c.Count = 1", "Cannot assign to field 'Count' of readonly parameter 'c'.")]
    [InlineData("c.Level = 1", "Cannot set 'Level' on readonly parameter 'c': a readonly parameter admits no setter.")]
    public void AMutationDirectlyOnAReadonlyParameterKeepsItsMessage(string shape, string message)
    {
        Assert.Equal(message, Assert.Single(Analyze(shape, ParameterKind.Readonly)).Message);
    }

    [Fact]
    public void AFieldWriteThroughAReadonlyRefParameterIsRefusedAtAnyDepth()
    {
        Assert.Equal(FieldAssigned, Assert.Single(Analyze("c.Inner.Count = 1", ParameterKind.ReadonlyRef)).Id.Value);
    }

    [Theory]
    [InlineData("c.Inner.Peek()")]
    [InlineData("var read = c.Inner.Count")]
    [InlineData("var read = c.Self")]
    [InlineData("c.Inner.Fixed = 1")]
    public void AReadOrAReadonlyAccessThroughAReadonlyParameterIsAdmitted(string shape)
    {
        Assert.Empty(Analyze(shape, ParameterKind.Readonly));
    }

    [Theory]
    [InlineData("c.Inner.Bump()")]
    [InlineData("c.Inner.Count = 1")]
    [InlineData("c.Inner.Level = 1")]
    [InlineData("c.Inner[0] = 1")]
    public void TheSameChainFromAPlainParameterStaysMutable(string shape)
    {
        Assert.Empty(Analyze(shape, ParameterKind.Plain));
    }

    [Theory]
    [InlineData("c.Inner.Bump()")]
    [InlineData("c.Inner.Level = 1")]
    public void ACallOrSetterThroughAReadonlyRefParameterIsLeftToTheReferenceRules(string shape)
    {
        Assert.Empty(Analyze(shape, ParameterKind.ReadonlyRef));
    }

    [Fact]
    public void InAReadonlyMemberADeepChainIsOneComplaintFromTheContext()
    {
        var finding = Assert.Single(Analyze("c.Inner.Bump()", ParameterKind.Readonly, readonlyMember: true));

        Assert.Equal("MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE", finding.Id.Value);
    }

    private enum ParameterKind
    {
        Plain,
        Readonly,
        ReadonlyRef,
    }

    private IReadOnlyList<Diagnostic> Analyze(string shape, ParameterKind kind, bool readonlyMember = false)
    {
        var isRef = kind == ParameterKind.ReadonlyRef;
        var modifier = new ParameterModifier(kind != ParameterKind.Plain, isRef, OwnershipKind.Unrestricted, isRef ? ByRefDirection.In : ByRefDirection.None);
        var c = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("c", counter) { IsRef = isRef, Parameter = new Parameter("c", counter, modifier) });
        var statement = Statement(shape, c);
        var body = HandBuiltModel.Block(statement);
        var method = new Method("Touch", integer, [], new MemberModifiers { IsReadonly = readonlyMember, IsStatic = true })
        {
            Body = body,
            DeclaringType = counter,
        };

        return model.Analyze(new ReadonlyAnalyzer(), body, owner: counter, member: method);
    }

    private Statement Statement(string shape, LocalReferenceExpression c)
    {
        return shape switch
        {
            "c.Bump()" => new ExpressionStatement(Call(c, bump)),
            "c.Inner.Bump()" => new ExpressionStatement(Call(FieldOf(c, inner), bump)),
            "c.Inner.Inner.Bump()" => new ExpressionStatement(Call(FieldOf(FieldOf(c, inner), inner), bump)),
            "c.Self.Bump()" => new ExpressionStatement(Call(PropertyOf(c, self), bump)),
            "c.Inner.Peek()" => new ExpressionStatement(Call(FieldOf(c, inner), peek)),
            "c.Count = 1" => new ExpressionStatement(Write(FieldOf(c, count, 37))),
            "c.Inner.Count = 1" => new ExpressionStatement(Write(FieldOf(FieldOf(c, inner), count, 37))),
            "c.Inner.Inner.Count = 1" => new ExpressionStatement(Write(FieldOf(FieldOf(FieldOf(c, inner), inner), count, 37))),
            "c.Level = 1" => new ExpressionStatement(Write(PropertyOf(c, level, 37))),
            "c.Inner.Level = 1" => new ExpressionStatement(Write(PropertyOf(FieldOf(c, inner), level, 37))),
            "c.Inner.Fixed = 1" => new ExpressionStatement(Write(PropertyOf(FieldOf(c, inner), fixedLevel, 37))),
            "c.Inner[0] = 1" => new ExpressionStatement(Write(
                new IndexExpression(HandBuiltModel.Node(37), FieldOf(c, inner), HandBuiltModel.Literal(integer, 0L), integer) { Indexer = slots })),
            "var read = c.Inner.Count" => new LocalDeclarationStatement(new LocalVariable("read", integer), FieldOf(FieldOf(c, inner), count)),
            "var read = c.Self" => new LocalDeclarationStatement(new LocalVariable("read", counter), PropertyOf(c, self)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private Field Member(Field field)
    {
        counter.FieldsList.Add(field);
        return field;
    }

    private Property Auto(string name, ClassType type, MemberModifiers modifiers)
    {
        return new Property(name, type, modifiers)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            DeclaringType = counter,
        };
    }

    private static InvocationExpression Call(Expression receiver, Method method)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(37), new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType), [], method.ReturnType, method, []);
    }

    private MemberAccessExpression FieldOf(Expression receiver, Field field, int offset = 0)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, counter, []));
    }

    private MemberAccessExpression PropertyOf(Expression receiver, Property property, int offset = 0)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, property.Name, property.Type, member: new PropertyResult(property, counter, []));
    }

    private AssignmentExpression Write(Expression target)
    {
        return new AssignmentExpression(HandBuiltModel.Node(37), target, HandBuiltModel.Literal(integer, 1L), integer);
    }
}