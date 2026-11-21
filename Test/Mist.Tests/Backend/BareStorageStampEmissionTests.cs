using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class BareStorageStampEmissionTests
{
    private readonly GenericDefaultShapes shapes = new();

    private readonly ProgramModelClass model = new();

    [Fact]
    public void ConcreteArrayOfANonZeroDefault_IsFilledWithThatDefault()
    {
        Assert.Equal("MistStamp.Array(new Seven[2], Seven.__CreateDefault())", Emit(Creation(Seven())));
    }

    [Fact]
    public void ConcreteArrayOfAZeroDefault_KeepsThePlainAllocation()
    {
        Assert.Equal("new Zero[2]", Emit(Creation(Zero())));
    }

    [Fact]
    public void StackallocOfANonZeroDefault_IsFilledWithThatDefault()
    {
        Assert.Equal("MistStamp.Span(stackalloc Seven[2], Seven.__CreateDefault())", Emit(Stackalloc(Seven())));
    }

    [Fact]
    public void StackallocOfAZeroDefault_KeepsThePlainAllocation()
    {
        Assert.Equal("stackalloc Zero[2]", Emit(Stackalloc(Zero())));
    }

    [Fact]
    public void GenericArrayUnderStampable_ReadsTheHolderRegisteredForEachClosingArgument()
    {
        var seven = Seven();
        var (blank, parameter) = StampableMethod("Blank");
        model.GenericCalls.Record(blank, [seven]);

        var (emitted, runtime) = EmitWithRuntime(Creation(parameter));

        Assert.Equal("MistStamp.Array(new T[2])", emitted);
        Assert.Contains("if(type == typeof(Seven))\n        {\n            return Seven.__CreateDefault();", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedGenericStruct_StampsTheDefaultOfItsArgumentInsideIt()
    {
        var box = Construct(shapes.Box(isStruct: true), Seven());

        Assert.Equal(
            "MistStamp.Array(new Probe.Box<Seven>[2], Probe.Box<Seven>.__CreateDefault(() => Seven.__CreateDefault()))",
            Emit(Creation(box)));
    }

    [Fact]
    public void NestedGenericStructOfAZeroDefault_KeepsThePlainAllocation()
    {
        var box = Construct(shapes.Box(isStruct: true), Zero());

        Assert.Equal("new Probe.Box<Zero>[2]", Emit(Creation(box)));
    }

    [Fact]
    public void ForwardedConstruction_IsRegisteredThoughNoSourceNamesIt()
    {
        var seven = Seven();
        var boxDefinition = shapes.Box(isStruct: true);
        var (blank, blankParameter) = StampableMethod("Blank");
        var (outer, outerParameter) = StampableMethod("Outer");
        model.GenericCalls.Record(outer, [seven]);
        model.GenericCalls.Record(blank, [Construct(boxDefinition, outerParameter)]);

        var (_, runtime) = EmitWithRuntime(Creation(blankParameter));

        Assert.Contains("if(type == typeof(Probe.Box<Seven>))", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeLevelStampableParameter_IsRegisteredFromItsClosedConstruction()
    {
        var cell = shapes.Type("Cell", isStruct: true);
        var parameter = Parameter(cell);
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));
        _ = model.TypeInterner.GetOrCreateConstructedType(cell, [Seven()]);

        var (emitted, runtime) = EmitWithRuntime(Stackalloc(parameter));

        Assert.Equal("MistStamp.Span(stackalloc T[2])", emitted);
        Assert.Contains("if(type == typeof(Seven))", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateNestedStruct_IsRegisteredByTheBodyThatNamesIt()
    {
        var program = HandBuiltModel.Class("Program");
        var hidden = new StructType("Hidden", "Probe", new Scope(null), program, null)
        {
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Private },
        };
        _ = Slot(hidden, "Y", shapes.Integer, HandBuiltModel.Literal(shapes.Integer, 9L));
        var (blank, _) = StampableMethod("Blank");
        model.GenericCalls.Record(blank, [hidden]);
        var main = new Method("Main", shapes.Integer, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = program,
            Body = HandBuiltModel.Block(new ExpressionStatement(Creation(hidden))),
        };
        var writer = new CSharpWriter();

        using(StorageStamping.Enter(model))
        {
            BodyEmitter.EmitMethodBody(main, main.Body, writer, new DiagnosticBag(), owner: program);
        }

        Assert.StartsWith(
            "MistDefault<Hidden>.Ensure(() => Hidden.__CreateDefault());",
            writer.ToString(), StringComparison.Ordinal);
    }

    private ConcreteType Seven()
    {
        var seven = shapes.Type("Seven", isStruct: true);
        _ = Slot(seven, "Y", shapes.Integer, HandBuiltModel.Literal(shapes.Integer, 7L));
        return seven;
    }

    private ConcreteType Zero()
    {
        var zero = shapes.Type("Zero", isStruct: true);
        _ = Slot(zero, "Y", Intrinsic());
        return zero;
    }

    private static StructType Intrinsic()
    {
        return new StructType("Int32", "Standard", new Scope(null), null, null)
        {
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true },
        };
    }

    private (Method Method, TypeParameter Parameter) StampableMethod(string name)
    {
        var method = new Method(name, shapes.Integer, [], new MemberModifiers { IsStatic = true });
        var parameter = new TypeParameter("T", 0, new Scope(null), null) { DeclaringMethod = method };
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));
        method.TypeParametersList.Add(parameter);
        return (method, parameter);
    }

    private ArrayCreationExpression Creation(IMistType element)
    {
        return new ArrayCreationExpression(HandBuiltModel.Node(), element, HandBuiltModel.Literal(shapes.Integer, 2L), element);
    }

    private StackallocExpression Stackalloc(IMistType element)
    {
        return new StackallocExpression(HandBuiltModel.Node(), element, HandBuiltModel.Literal(shapes.Integer, 2L), element);
    }

    private string Emit(Expression expression)
    {
        return EmitWithRuntime(expression).Emitted;
    }

    private (string Emitted, string Runtime) EmitWithRuntime(Expression expression)
    {
        var writer = new CSharpWriter();
        var runtime = new CSharpWriter();

        using(StorageStamping.Enter(model))
        {
            BodyEmitter.EmitExpression(expression, writer, new DiagnosticBag());
            StorageStamping.Emit(runtime);
        }

        return (writer.ToString(), runtime.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}