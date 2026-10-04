using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
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
public sealed class ExtensionAsyncPromotionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopeExitCompletionDiscoversExtensionMethodsAndTheirLocalFunctions(bool localFunction)
    {
        var fixture = new Fixture();
        var pending = new LocalVariable("pending", fixture.TaskOf(fixture.Int));
        var scheduled = new Method("Fetch", pending.Type, [], new MemberModifiers { IsAsync = true });
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(pending, Call(scheduled)));
        var owed = new Method("Read", fixture.Int, [], new MemberModifiers()) { Body = body };
        var outer = localFunction
            ? new Method("Outer", fixture.Int, [], new MemberModifiers())
            { Body = HandBuiltModel.Block(new LocalFunctionStatement(owed, [])) }
            : owed;
        fixture.Block.MethodsList.Add(outer);

        using var promotion = AsyncPromotion.Enter(fixture.Built.Model);

        Assert.True(AsyncPromotion.IsPromoted(owed));
        Assert.Equal(!localFunction, AsyncPromotion.IsPromoted(outer));
        Assert.Same(fixture.Int, owed.ReturnType);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitAndPromotedExtensionsShareTaskSignaturesAndPreserveReceiverShape(bool declaredAsync, bool isStatic)
    {
        var fixture = new Fixture();
        var blockParameter = new TypeParameter("T", 0, fixture.Block.Scope, null);
        fixture.Block.TypeParametersList.Add(blockParameter);
        fixture.Block.ResolvedReceiverType = blockParameter;
        var returned = declaredAsync ? fixture.TaskOf(fixture.Int) : fixture.Int;
        var method = new Method("Read", returned, [], new MemberModifiers { IsAsync = declaredAsync, IsStatic = isStatic })
        { Body = HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(fixture.Int, 7), fixture.Int)) };
        var memberParameter = new TypeParameter("U", 1, new Scope(fixture.Block.Scope), null);
        method.TypeParametersList.Add(memberParameter);
        fixture.Block.MethodsList.Add(method);
        fixture.Built.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(fixture.Built.Model);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var emitted = false;
        ExtensionBlockEmitter.Emit(fixture.Block, writer, diagnostics, ref emitted);

        Assert.Contains($"public static async System.Threading.Tasks.Task<int> Read<T, U>({(isStatic ? "" : "this T self")})", writer.ToString());
        Assert.Equal(!declaredAsync, AsyncPromotion.IsPromoted(method));
        Assert.Same(returned, method.ReturnType);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnOrdinaryExtensionRetainsItsSynchronousSignature()
    {
        var fixture = new Fixture();
        fixture.Block.MethodsList.Add(new Method("Read", fixture.Int, [], new MemberModifiers())
        { Body = HandBuiltModel.Block(new ReturnStatement(HandBuiltModel.Literal(fixture.Int, 7), fixture.Int)) });
        using var promotion = AsyncPromotion.Enter(fixture.Built.Model);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var emitted = false;
        ExtensionBlockEmitter.Emit(fixture.Block, writer, diagnostics, ref emitted);

        Assert.Contains("public static int Read(this int self)", writer.ToString());
        Assert.DoesNotContain("async", writer.ToString());
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AClosedGenericExtensionCallCompletesItsPromotedDeclaration()
    {
        var fixture = new Fixture();
        var receiverParameter = new TypeParameter("T", 0, fixture.Block.Scope, null);
        fixture.Block.TypeParametersList.Add(receiverParameter);
        var source = new Method("Read", fixture.Int, [], new MemberModifiers()) { DeclaringBlock = fixture.Block };
        source.TypeParametersList.Add(new TypeParameter("U", 1, fixture.Block.Scope, null));
        var closed = new Method("Read", fixture.Int, [], source.Modifiers)
        {
            SourceDeclaration = source,
            DeclaringBlock = fixture.Block,
            ClosedBlock = fixture.Block,
            BlockClosing = TypeSubstitution.ForParameters([receiverParameter], [fixture.Int]),
        };
        closed.TypeParametersList.AddRange(source.TypeParametersList);
        fixture.Block.MethodsList.Add(source);
        fixture.Built.Model.TaskDrops.RecordDrop(source);
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", fixture.Int));
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Read", fixture.Int, [fixture.Built.Type("String")]);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [], fixture.Int, closed, []);

        using var promotion = AsyncPromotion.Enter(fixture.Built.Model);

        Assert.Equal("await Probe.IntExtensions.Read<int, string>(value)", Emit(call));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APromotedFunctorSchedulesOneAsyncInvocationEvenInsideASynchronousLambda(bool enclosingLambda)
    {
        var fixture = new Fixture();
        var method = new Method("Read", fixture.Int, [], new MemberModifiers());
        fixture.Built.Model.TaskDrops.RecordDrop(method);
        var call = Call(method);
        var lift = new AsyncExpression(HandBuiltModel.Node(), call, fixture.TaskOf(fixture.Int), isLift: true);
        var pending = new LocalVariable("pending", lift.Type);
        Expression expression = enclosingLambda
            ? new LambdaExpression(HandBuiltModel.Node(), [], null,
                HandBuiltModel.Block(new LocalDeclarationStatement(pending, lift),
                    new ReturnStatement(HandBuiltModel.Literal(fixture.Int, 1), fixture.Int)),
                false, new FunctionType([], fixture.Int))
            : lift;

        using var promotion = AsyncPromotion.Enter(fixture.Built.Model);
        var emitted = Emit(expression);

        Assert.Contains("System.Threading.Tasks.Task.Run(async () => await Read())", emitted);
        Assert.DoesNotContain("Read().GetAwaiter", emitted);
        Assert.Equal(1, emitted.Split("Read()", StringSplitOptions.None).Length - 1);

        if(enclosingLambda)
        {
            Assert.Contains("pending.GetAwaiter().GetResult();", emitted);
        }
    }

    [Fact]
    public void AwaitingAFunctorArgumentAlsoUsesAnAsyncScheduledLambda()
    {
        var fixture = new Fixture();
        var promoted = new Method("Read", fixture.Int, [], new MemberModifiers());
        fixture.Built.Model.TaskDrops.RecordDrop(promoted);
        var ordinary = new Method("Identity", fixture.Int, [new Parameter("value", fixture.Int)], new MemberModifiers());
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), ordinary),
            [Call(promoted)], fixture.Int, ordinary, [fixture.Int]);
        var lift = new AsyncExpression(HandBuiltModel.Node(), call, fixture.TaskOf(fixture.Int), isLift: true);

        using var promotion = AsyncPromotion.Enter(fixture.Built.Model);

        Assert.Equal("System.Threading.Tasks.Task.Run(async () => Identity(await Read()))", Emit(lift));
    }

    [Fact]
    public void OrdinaryLiftsAndExplicitAsyncSuppressionRetainTheirContracts()
    {
        var fixture = new Fixture();
        var ordinary = new Method("Read", fixture.Int, [], new MemberModifiers());
        var declared = new Method("Fetch", fixture.TaskOf(fixture.Int), [], new MemberModifiers { IsAsync = true });
        using var promotion = AsyncPromotion.Enter(fixture.Built.Model);

        Assert.Equal("System.Threading.Tasks.Task.Run(() => Read())",
            Emit(new AsyncExpression(HandBuiltModel.Node(), Call(ordinary), fixture.TaskOf(fixture.Int), isLift: true)));
        Assert.Equal("Fetch()",
            Emit(new AsyncExpression(HandBuiltModel.Node(), Call(declared), declared.ReturnType, isLift: false)));
    }

    private static InvocationExpression Call(Method method)
    {
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method),
            [], method.ReturnType, method, []);
    }

    private static string Emit(Expression expression)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private sealed class Fixture
    {
        public HandBuiltModel Built { get; } = new();

        public ClassType Int { get; }

        public ExtensionBlock Block { get; }

        private readonly ClassType task;

        public Fixture()
        {
            Int = Built.Type("Int32");
            task = new ClassType("Task<>", "Standard", new Scope(null), null, null)
            { Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Relevant } };
            task.TypeParametersList.Add(new TypeParameter("T", 0, task.Scope, null));
            _ = Built.Model.RegisterType(task);
            var unit = new CompilationUnit { SourcePath = "hand-built.mist" };
            Built.Model.AddSelfAssembly("Probe").CompilationUnits.Add(unit);
            Block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode())
            { ResolvedReceiverType = Int, CarrierName = "IntExtensions" };
            unit.Extensions.Add(Block);
        }

        public IMistType TaskOf(IMistType result)
        {
            return Built.Interner.GetOrCreateConstructedType(task, [result]);
        }
    }
}