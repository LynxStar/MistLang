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

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AsyncSlotBridgeEmissionTests
{
    private readonly HandBuiltModel built = new();
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MistAssembly Source = new("Probe", MistAssemblyKind.Self);

    [Theory]
    [InlineData(false, "int Probe.IReader.Read() => this.Read().GetAwaiter().GetResult();")]
    [InlineData(true, "void Probe.IReader.Read() => this.Read().GetAwaiter().GetResult();")]
    public void ASlotCompletesItsPromotedImplementationWithoutChangingItsReturnType(bool unit, string expected)
    {
        var result = built.Type(unit ? "Void" : "Int32");
        var contract = Contract();
        _ = Member(contract, "Read", result);
        var reader = Reader(contract);
        var source = Member(reader, "Read", result);
        built.Model.TaskDrops.RecordDrop(source);

        Assert.Equal(expected, EmitBridge(reader));
        Assert.Same(result, source.ReturnType);
    }

    [Fact]
    public void AGenericInheritedSlotUsesItsClosedDeclaringInterfaceAndInheritedImplementation()
    {
        var contract = Contract("IReader<>");
        var parameter = new TypeParameter("T", 0, contract.Scope, contract);
        contract.TypeParametersList.Add(parameter);
        _ = Member(contract, "Read", parameter);
        var inheritedContract = Contract("IChild");
        inheritedContract.BaseAbstractTypes.Add(new ConstructedType(contract, [built.Type("Int32")]));
        var baseReader = HandBuiltModel.Class("BaseReader<>");
        var baseParameter = new TypeParameter("U", 0, baseReader.Scope, baseReader);
        baseReader.TypeParametersList.Add(baseParameter);
        var source = Member(baseReader, "Read", baseParameter);
        var reader = Reader(inheritedContract);
        reader.BaseType = new ConstructedType(baseReader, [built.Type("Int32")]);
        built.Model.TaskDrops.RecordDrop(source);

        Assert.Equal("int Probe.IReader<int>.Read() => this.Read().GetAwaiter().GetResult();", EmitBridge(reader));
    }

    [Fact]
    public void AGenericMethodSlotPassesItsOwnTypeArgumentsAndRefParameters()
    {
        var contract = Contract();
        var parameter = new TypeParameter("T", 0, new Scope(contract.Scope), declaringType: null);
        var slot = Member(contract, "Read", parameter);
        slot.TypeParametersList.Add(parameter);
        slot.ParametersList.Add(new Parameter("value", parameter, new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)));
        var reader = Reader(contract);
        var sourceParameter = new TypeParameter("U", 0, new Scope(reader.Scope), declaringType: null);
        var source = Member(reader, "Read", sourceParameter);
        source.TypeParametersList.Add(sourceParameter);
        source.ParametersList.Add(new Parameter("value", sourceParameter, new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)));
        built.Model.TaskDrops.RecordDrop(source);

        Assert.Equal("T Probe.IReader.Read<T>(ref T value) => this.Read<T>(ref value).GetAwaiter().GetResult();", EmitBridge(reader));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinarySyncAndDeclaredAsyncSlotsNeedNoPromotionBridge(bool declaredAsync)
    {
        var result = built.Type("Int32");
        var contract = Contract();
        _ = Member(contract, "Read", result, declaredAsync);
        var reader = Reader(contract);
        _ = Member(reader, "Read", result, declaredAsync);

        Assert.Empty(EmitBridge(reader));
    }

    [Fact]
    public void AnOverloadWithADifferentSignatureCannotFillTheSlot()
    {
        var contract = Contract();
        _ = Member(contract, "Read", built.Type("Int32"));
        var reader = Reader(contract);
        var unrelated = Member(reader, "Read", built.Type("String"));
        built.Model.TaskDrops.RecordDrop(unrelated);

        Assert.Empty(EmitBridge(reader));
    }

    [Theory]
    [InlineData(false, "int Probe.IReader.Read() => this.Fetch().GetAwaiter().GetResult();")]
    [InlineData(true, "int Probe.IReader<int>.Read() => this.Read().GetAwaiter().GetResult();")]
    public void ARetroactiveMappingCompletesTheSelectedSourceExactlyOnce(bool closedGeneric, string expected)
    {
        var contract = Contract(closedGeneric ? "IReader<>" : "IReader");
        var parameter = new TypeParameter("T", 0, contract.Scope, contract);
        var slot = Member(contract, "Read", closedGeneric ? parameter : built.Type("Int32"));
        if(closedGeneric)
        {
            contract.TypeParametersList.Add(parameter);
        }

        var target = closedGeneric
            ? new Method("Read", built.Type("Int32"), [], Public) { DeclaringType = contract }
            : slot;
        IMistType declarer = closedGeneric ? new ConstructedType(contract, [built.Type("Int32")]) : contract;
        var reader = HandBuiltModel.Class("Reader");
        var source = Member(reader, closedGeneric ? "Read" : "Fetch", built.Type("Int32"));
        reader.ConformancesList.Add(new ConformanceDeclaration(reader, declarer, "Probe", [new MethodMapping(source, target, declarer)], [], HandBuiltModel.Node()));
        built.Model.TaskDrops.RecordDrop(source);
        var writer = new CSharpWriter();
        var any = false;

        using(AsyncPromotion.Enter(built.Model))
        {
            ConformanceEmitter.Emit(reader, writer, new DiagnosticBag(), ref any);
            AsyncSlotBridgeEmitter.Emit(reader, writer, new DiagnosticBag(), ref any);
        }

        Assert.Equal(expected, writer.ToString().Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADefaultAndItsRestatementCompleteTaskCallsWithinTheirDeclaredSlot(bool declaredAsync)
    {
        var integer = built.Type("Int32");
        var task = new ClassType("Task<>", "Standard", new Scope(null), null, null);
        _ = built.Model.RegisterType(task);
        var taskInteger = new ConstructedType(task, [integer]);
        var fetch = new Method("Fetch", taskInteger, [], Public with { IsAsync = true });
        var invocation = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), fetch), [], taskInteger, fetch, []);
        var drop = new Method("Drop", integer, [], Public with { IsIntrinsic = true }) { DeclaringType = task };
        var awaited = new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), invocation, "Drop", integer), [], integer, drop, []);
        var contract = Contract();
        var method = new Method("Read", declaredAsync ? taskInteger : integer, [], Public with { IsAsync = declaredAsync })
        {
            DeclaringType = contract,
            Body = HandBuiltModel.Block(new ReturnStatement(awaited, integer)),
        };
        contract.MethodsList.Add(method);
        built.Model.TaskDrops.RecordDrop(method);
        var writer = new CSharpWriter();

        using(AsyncPromotion.Enter(built.Model))
        {
            TypeEmitter.EmitInterfaceMethod(method, writer, new DiagnosticBag());
            TypeEmitter.EmitRestatedDefault(method, writer, new DiagnosticBag());
        }

        var emitted = writer.ToString();
        if(declaredAsync)
        {
            Assert.Contains("async System.Threading.Tasks.Task<int> Read()", emitted, StringComparison.Ordinal);
            Assert.Contains("public async System.Threading.Tasks.Task<int> Read()", emitted, StringComparison.Ordinal);
            Assert.Equal(2, emitted.Split("await Fetch()", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("GetAwaiter", emitted, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("int Read()", emitted, StringComparison.Ordinal);
            Assert.Contains("public int Read()", emitted, StringComparison.Ordinal);
            Assert.Equal(2, emitted.Split("Fetch().GetAwaiter().GetResult()", StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("await ", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain("Task<int> Read", emitted, StringComparison.Ordinal);
        }
    }

    private string EmitBridge(ClassType reader)
    {
        var writer = new CSharpWriter();
        var any = false;
        using(AsyncPromotion.Enter(built.Model))
        {
            AsyncSlotBridgeEmitter.Emit(reader, writer, new DiagnosticBag(), ref any);
        }

        return writer.ToString().Trim();
    }

    private static AbstractType Contract(string name = "IReader")
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null) { Assembly = Source };
    }

    private static ClassType Reader(AbstractType contract)
    {
        var reader = HandBuiltModel.Class("Reader");
        reader.BaseAbstractTypes.Add(contract);
        return reader;
    }

    private static Method Member(MemberType owner, string name, IMistType result, bool declaredAsync = false)
    {
        var method = new Method(name, result, [], Public with { IsAsync = declaredAsync }) { DeclaringType = owner };
        owner.MethodsList.Add(method);
        return method;
    }
}