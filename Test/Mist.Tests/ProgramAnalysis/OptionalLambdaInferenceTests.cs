using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class OptionalLambdaInferenceTests
{
    [Theory]
    [InlineData("optional", false)]
    [InlineData("optional", true)]
    [InlineData("alias", false)]
    [InlineData("alias", true)]
    [InlineData("plain", false)]
    [InlineData("plain", true)]
    public void InputBoundsAndLambdaResultsInferSeparateParameters(string spelling, bool stated)
    {
        var f = new HandBuiltModel();
        var method = Method(f, "T", "R");
        var t = method.TypeParametersList[0];
        var r = method.TypeParametersList[1];
        var signature = new FunctionType([t], r);
        var target = spelling == "plain" ? signature : Optional(f, signature);
        if(spelling == "alias")
        {
            var alias = new TypeAlias("Callback", "Probe", method.Scope!, null, new TypeAliasDeclarationNode()) { AliasedType = target };
            target = f.Interner.GetOrCreateConstructedType(alias, []);
        }

        var trials = new Trials();
        var lambda = trials.Add(f.Type("Boolean"), stated ? [f.Type("String")] : [null]);
        if(!stated)
        {
            method.ParametersList.Add(new Parameter("seed", t));
        }

        method.ParametersList.Add(new Parameter("callback", target));
        Expression[] arguments = stated ? [lambda] : [HandBuiltModel.Literal(f.Type("String")), lambda];

        var inferred = TypeArgumentInference.Infer(method, arguments, TypeSubstitution.Empty, f.Interner, trials, out var conflict);

        Assert.Null(conflict);
        Assert.Equal([f.Type("String"), f.Type("Boolean")], inferred);
        Assert.Same(f.Type("String"), Assert.Single(Assert.Single(trials.Inputs)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableInputPreventsTheLambdaFromTeachingResults(bool conflicting)
    {
        var f = new HandBuiltModel();
        var method = Method(f, "T", "R");
        var t = method.TypeParametersList[0];
        var trials = new Trials();
        var lambda = trials.Add(f.Type("Boolean"), [null]);
        var arguments = new List<Expression>();
        if(conflicting)
        {
            method.ParametersList.Add(new Parameter("first", t));
            method.ParametersList.Add(new Parameter("second", t));
            arguments.Add(HandBuiltModel.Literal(f.Type("String")));
            arguments.Add(HandBuiltModel.Literal(f.Type("Boolean")));
        }

        method.ParametersList.Add(new Parameter("callback", Optional(f, new FunctionType([t], method.TypeParametersList[1]))));
        arguments.Add(lambda);

        var inferred = TypeArgumentInference.Infer(method, arguments, TypeSubstitution.Empty, f.Interner, trials, out var conflict);

        Assert.All(inferred, Assert.Null);
        Assert.Empty(trials.Inputs);
        if(conflicting)
        {
            Assert.Same(t, Assert.IsType<TypeArgumentConflict>(conflict).Parameter);
        }
        else
        {
            Assert.Null(conflict);
        }
    }

    [Fact]
    public void FrozenInputsTakeNoFurtherBoundsFromTheLambdaResult()
    {
        var f = new HandBuiltModel();
        var method = Method(f, "T", "R");
        var t = method.TypeParametersList[0];
        var r = method.TypeParametersList[1];
        var pair = f.Type("Pair<,>");
        method.ParametersList.Add(new Parameter("seed", t));
        method.ParametersList.Add(new Parameter("callback", Optional(f, new FunctionType([t], f.Interner.GetOrCreateConstructedType(pair, [t, r])))));
        var trials = new Trials();
        var lambda = trials.Add(f.Interner.GetOrCreateConstructedType(pair, [f.Type("Boolean"), f.Type("Boolean")]), [null]);

        var inferred = TypeArgumentInference.Infer(method, [HandBuiltModel.Literal(f.Type("String")), lambda], TypeSubstitution.Empty, f.Interner, trials, out var conflict);

        Assert.Null(conflict);
        Assert.Equal([f.Type("String"), f.Type("Boolean")], inferred);
        Assert.Same(f.Type("String"), Assert.Single(Assert.Single(trials.Inputs)));
    }

    [Fact]
    public void AParameterlessLambdaCanInferItsResultWithoutInputBounds()
    {
        var f = new HandBuiltModel();
        var method = Method(f, "R");
        method.ParametersList.Add(new Parameter("callback", Optional(f, new FunctionType([], method.TypeParametersList[0]))));
        var trials = new Trials();
        var lambda = trials.Add(f.Type("Boolean"), []);

        var inferred = TypeArgumentInference.Infer(method, [lambda], TypeSubstitution.Empty, f.Interner, trials, out var conflict);

        Assert.Null(conflict);
        Assert.Same(f.Type("Boolean"), Assert.Single(inferred));
        Assert.Empty(Assert.Single(trials.Inputs));
    }

    [Fact]
    public void OptionalCallbacksCanTeachOneAnothersInputsAcrossPasses()
    {
        var f = new HandBuiltModel();
        var method = Method(f, "T", "U", "R");
        var t = method.TypeParametersList[0];
        var u = method.TypeParametersList[1];
        var r = method.TypeParametersList[2];
        method.ParametersList.Add(new Parameter("seed", t));
        method.ParametersList.Add(new Parameter("second", Optional(f, new FunctionType([u], r))));
        method.ParametersList.Add(new Parameter("first", Optional(f, new FunctionType([t], u))));
        var trials = new Trials();
        var first = trials.Add(f.Type("Boolean"), [null]);
        var second = trials.Add(f.Type("Int64"), [null]);

        var inferred = TypeArgumentInference.Infer(method, [HandBuiltModel.Literal(f.Type("String")), second, first], TypeSubstitution.Empty, f.Interner, trials, out var conflict);

        Assert.Null(conflict);
        Assert.Equal([f.Type("String"), f.Type("Boolean"), f.Type("Int64")], inferred);
        Assert.Equal(2, trials.Inputs.Count);
        Assert.Same(f.Type("String"), Assert.Single(trials.Inputs[0]));
        Assert.Same(f.Type("Boolean"), Assert.Single(trials.Inputs[1]));
    }

    [Fact]
    public void ReceiverSubstitutionPrecedesOptionalSignatureInference()
    {
        var f = new HandBuiltModel();
        var method = Method(f, "R");
        var receiverParameter = new TypeParameter("T", 0, new Scope(null), null);
        method.ParametersList.Add(new Parameter("callback", Optional(f, new FunctionType([receiverParameter], method.TypeParametersList[0]))));
        var trials = new Trials();
        var lambda = trials.Add(f.Type("Boolean"), [null]);
        var receiver = TypeSubstitution.ForParameters([receiverParameter], [f.Type("String")]);

        var inferred = TypeArgumentInference.Infer(method, [lambda], receiver, f.Interner, trials, out var conflict);

        Assert.Null(conflict);
        Assert.Same(f.Type("Boolean"), Assert.Single(inferred));
        Assert.Same(f.Type("String"), Assert.Single(Assert.Single(trials.Inputs)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneralUnionsDoNotChooseACallableAlternative(bool twoCallables)
    {
        var f = new HandBuiltModel();
        var method = Method(f, "R");
        var signature = new FunctionType([f.Type("String")], method.TypeParametersList[0]);
        IMistType other = twoCallables ? new FunctionType([f.Type("Boolean")], method.TypeParametersList[0]) : f.Type("Boolean");
        method.ParametersList.Add(new Parameter("callback", new UnionType([signature, other, f.Type("Void")])));
        var trials = new Trials();
        var lambda = trials.Add(f.Type("Boolean"), [f.Type("String")]);

        var inferred = TypeArgumentInference.Infer(method, [lambda], TypeSubstitution.Empty, f.Interner, trials, out var conflict);

        Assert.Null(conflict);
        Assert.Null(Assert.Single(inferred));
        Assert.Empty(trials.Inputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalMethodGroupsRemainOutsideThisLambdaExtension(bool optional)
    {
        var f = new HandBuiltModel();
        var method = Method(f, "R");
        var signature = new FunctionType([f.Type("String")], method.TypeParametersList[0]);
        method.ParametersList.Add(new Parameter("callback", optional ? Optional(f, signature) : signature));
        var target = new Method("Callback", f.Type("Boolean"), [new Parameter("value", f.Type("String"))], new MemberModifiers());
        var group = new MethodGroupExpression(HandBuiltModel.Node(), null, "Callback", [target], null);

        var inferred = TypeArgumentInference.Infer(method, [group], TypeSubstitution.Empty, f.Interner, null, out var conflict);

        Assert.Null(conflict);
        Assert.Same(optional ? null : f.Type("Boolean"), Assert.Single(inferred));
    }

    private static Method Method(HandBuiltModel f, params string[] parameters)
    {
        var scope = new Scope(null);
        var method = new Method("Apply", f.Type("Void"), [], new MemberModifiers()) { Scope = scope };
        foreach(var name in parameters)
        {
            method.TypeParametersList.Add(new TypeParameter(name, method.TypeParametersList.Count, scope, null));
        }

        return method;
    }

    private static IMistType Optional(HandBuiltModel f, IMistType signature)
    {
        return f.Interner.GetOrCreateUnionType([signature, f.Type("Void")]);
    }

    private sealed class Trials : ILambdaTrials
    {
        private readonly Dictionary<UnboundLambdaExpression, (IMistType Result, IReadOnlyList<IMistType?> Stated)> callbacks = [];

        public List<IReadOnlyList<IMistType>> Inputs { get; } = [];

        public UnboundLambdaExpression Add(IMistType result, IReadOnlyList<IMistType?> stated)
        {
            var lambda = new UnboundLambdaExpression(new ExpressionLambdaLambdaExpressionNode());
            callbacks.Add(lambda, (result, stated));
            return lambda;
        }

        public IReadOnlyList<IMistType?> StatedParameterTypes(UnboundLambdaExpression lambda)
        {
            return callbacks[lambda].Stated;
        }

        public IMistType? ResultType(UnboundLambdaExpression lambda, IReadOnlyList<IMistType> parameterTypes)
        {
            Inputs.Add(parameterTypes);
            return callbacks[lambda].Result;
        }

        public bool Fits(UnboundLambdaExpression lambda, IMistType parameterType)
        {
            throw new NotSupportedException();
        }

        public IMistType ComparedType(UnboundLambdaExpression lambda, IMistType left, IMistType right)
        {
            throw new NotSupportedException();
        }
    }
}