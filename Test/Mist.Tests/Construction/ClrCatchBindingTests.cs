using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class ClrCatchBindingTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void VariadicInputsCloseBeforeCallableResultInference(int width, bool lambda)
    {
        var shapes = new ResultModel();
        var built = shapes.Built;
        var method = Catch(shapes);
        var types = new[] { shapes.Integer, shapes.Text }.Take(width).ToList();
        var target = new Method("Operation", shapes.Boolean, types.Select((type, i) => new Parameter($"arg{i}", type)).ToList(), default);
        var trials = new Trials(shapes.Boolean);
        Expression operation = lambda
            ? new UnboundLambdaExpression(new ExpressionLambdaLambdaExpressionNode())
            : new MethodGroupExpression(Node(), null, target.Name, [target], null);
        var inferred = TypeArgumentInference.Infer(method, [operation, .. types.Select(type => Literal(type))],
            TypeSubstitution.Empty, built.Interner, trials, out var conflict);

        Assert.Null(conflict);
        Assert.Equal(types, Assert.IsType<TypePackValue>(Assert.IsType<LiftedType>(inferred[0]).Value).Elements);
        Assert.Same(shapes.Boolean, inferred[1]);
        if(lambda)
        {
            Assert.Equal(types, trials.Inputs);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlySelectedBoundaryMovesResultInsideTask(bool task)
    {
        var shapes = new ResultModel();
        var method = Catch(shapes);
        var taskDefinition = shapes.Built.Type("Task<>");
        IMistType taskOf(IMistType payload)
        {
            return shapes.Built.Interner.GetOrCreateConstructedType(taskDefinition, [payload]);
        }

        var result = shapes.Result(task ? taskOf(shapes.Integer) : shapes.Integer, shapes.Text);
        var adapted = ClrCatch.Adapt(method, result, taskOf, shapes.Built.Interner);
        if(task)
        {
            var inner = Assert.IsType<ConstructedType>(Assert.Single(Assert.IsType<ConstructedType>(adapted).TypeArguments));
            Assert.Equal([shapes.Integer, shapes.Text], inner.TypeArguments);
        }
        else
        {
            Assert.Same(result, adapted);
        }

        Assert.Same(result, ClrCatch.Adapt(new Method("Raw", result, [], default), result, taskOf, shapes.Built.Interner));
    }

    [Fact]
    public void AnInvocationValueDoesNotBecomeADeferredOperation()
    {
        var shapes = new ResultModel();
        var method = Catch(shapes);
        var called = new Method("AlreadyInvoked", shapes.Integer, [], default);
        var invocation = new InvocationExpression(Node(), new MethodReferenceExpression(Node(), called), [], shapes.Integer, called, []);
        var inferred = TypeArgumentInference.Infer(method, [invocation], TypeSubstitution.Empty, shapes.Built.Interner, null, out _);
        Assert.Null(inferred[1]);
    }

    [Fact]
    public void ValueCallableShapeDoesNotEraseReferenceParameters()
    {
        var shapes = new ResultModel();
        var method = new Method("ByRef", shapes.Integer,
            [new Parameter("value", shapes.Integer, new ParameterModifier { IsRef = true })], default);
        var group = new MethodGroupExpression(Node(), null, method.Name, [method], null);
        Assert.Null(MethodGroupConversion.Convert(group, new FunctionType([shapes.Integer], shapes.Integer), shapes.Built.Interner));
        var nominal = new ClassType("ByRefDelegate", "Probe", null, null, null)
        { BaseType = new ClassType("MulticastDelegate", "System", null, null, null) };
        var invoke = new Method("Invoke", shapes.Integer, method.ParametersList, default) { DeclaringType = nominal };
        nominal.MethodsList.Add(invoke);
        Assert.NotNull(DelegateShape.Of(nominal));
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(nominal,
            new FunctionType([shapes.Integer], shapes.Integer), shapes.Built.Interner));
    }

    [Fact]
    public void AUserExtensionCannotClaimTheStandardIntrinsic()
    {
        var block = new ExtensionBlock("self", "Standard", new Scope(null), null)
        { ResolvedReceiverType = new ClassType("Exception", "System", null, null, null) };
        Assert.False(ClrCatch.Declares(block, "Catch"));
    }

    internal static Method Catch(ResultModel shapes)
    {
        var scope = new AssemblyScope(new ProgramScope(shapes.Built.Model),
            new MistAssembly("Standard", MistAssemblyKind.Standard), shapes.Built.Model, new DiagnosticBag());
        var block = new ExtensionBlock("self", "Standard", scope, null)
        { ResolvedReceiverType = new ClassType("Exception", "System", null, null, null) };
        var pack = new TypeParameter("TArgs", 0, block.Scope, null) { PackKind = TypePackKind.Unbracketed };
        var result = new TypeParameter("TResult", 1, block.Scope, null);
        var method = new Method("Catch", shapes.Result(result, shapes.Text),
            [new Parameter("operation", new FunctionType([new SpreadType(pack)], result)),
                new Parameter("args", new TupleType([new SpreadType(pack)])) { IsFlattened = true }],
            new MemberModifiers { IsIntrinsic = true, IsStatic = true })
        { DeclaringBlock = block };
        method.TypeParametersList.AddRange([pack, result]);
        return method;
    }

    private sealed class Trials(IMistType result) : ILambdaTrials
    {
        public IReadOnlyList<IMistType>? Inputs { get; private set; }

        public IReadOnlyList<IMistType?> StatedParameterTypes(UnboundLambdaExpression lambda)
        {
            return [];
        }

        public IMistType? ResultType(UnboundLambdaExpression lambda, IReadOnlyList<IMistType> parameterTypes)
        {
            Inputs = parameterTypes;
            return result;
        }

        public bool Fits(UnboundLambdaExpression lambda, IMistType parameterType)
        {
            return true;
        }

        public IMistType ComparedType(UnboundLambdaExpression lambda, IMistType left, IMistType right)
        {
            return left;
        }
    }
}