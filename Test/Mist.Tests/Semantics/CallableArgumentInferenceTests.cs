// Tier 3 (Component): a lambda or a method group passed to a call that has not yet fixed its type
// arguments or chosen its overload. The ordinary arguments fix what they can, the callable is typed
// by that, and its result teaches the rest; under several candidates each types the lambda its own
// way and the lambda can decide between them. Asserted on the model because the emitter states what
// the model chose and C# would infer or select most of these calls again by itself.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class CallableArgumentInferenceTests : ProgramModelProbe
{
    public CallableArgumentInferenceTests()
        : base("MistCallableInference_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Api
        {
            public static R Apply<T, R>(T value, func<T, R> callback) { return callback(value); }

            public static R Apply2<T, R>(T a, T b, func<T, R> callback) { return callback(a); }

            public static R Run<T, R>(func<T, R> callback, T value) { return callback(value); }

            public static int Call<T, R>(func<T, R> callback) { return 0; }

            public static int Fixed<T>(func<T, T> step) { return 0; }

            public static C Pipe<A, B, C>(A start, func<A, B> first, func<B, C> second) { return second(first(start)); }

            public static T Grow<T>(T seed, func<T, T> step) { return step(seed); }

            public static Pair<T, R> Both<T, R>(T seed, func<T, Pair<T, R>> step) { return step(seed); }

            public static T Twice<T>(T value, func<T, T> step) { return step(step(value)); }

            public static T Same<T>(T value) { return value; }

            public static int Next(int x) { return x + 1; }

            public static string Show(int x) { return "int"; }

            public static string Show(string x) { return "string"; }

            public static string Transform(int value, func<int, int> f) { return "int"; }

            public static string Transform(string value, func<string, string> f) { return "string"; }

            public static string Measure(func<int, int> f) { return "int"; }

            public static string Measure(func<string, int> f) { return "string"; }

            public static string Widen(func<int, int> f) { return "int"; }

            public static string Widen(func<int, long> f) { return "long"; }

            public static string Tie(func<int, int> f) { return "int"; }

            public static string Tie(func<long, int> f) { return "long"; }
        }

        public class Pair<A, B>
        {
            public Pair(A first, B second) { }
        }

        public class Labeled
        {
            public Labeled(func<int, int> f) { }

            public Labeled(func<string, int> f) { }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("var r = Api.Apply(1, x => x + 1);", "int, int")]
    [InlineData("var r = Api.Apply(\"mist\", s => s.Length);", "string, int")]
    [InlineData("var r = Api.Apply(2, x => { if(x > 1) { return \"big\"; } return \"small\"; });", "int, string")]
    [InlineData("var r = Api.Run((string s) => s.Length, \"mist\");", "string, int")]
    [InlineData("var r = Api.Call((string s) => s.Length);", "string, int")]
    [InlineData("var r = Api.Pipe(1, x => x + 1, y => y > 1);", "int, int, bool")]
    [InlineData("var r = Api.Apply(4, Api.Next);", "int, int")]
    [InlineData("var r = Api.Apply(4, Api.Show);", "int, string")]
    [InlineData("var r = Api.Apply(\"w\", Api.Show);", "string, string")]
    [InlineData("var r = Api.Twice(3, Api.Same);", "int")]
    [InlineData("var r = Api.Apply(4, (Api.Next));", "int, int")]
    public void ACallLearnsItsTypeArgumentsFromTheCallablesItIsPassed(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Empty(AllErrors(BuildRun(statement)));
        Assert.Equal(expected, string.Join(", ", Assert.IsType<MemberAccessExpression>(call.Receiver).TypeArguments.Select(TypeDisplay.Mist)));
    }

    [Theory]
    [InlineData("var r = Api.Apply(1, x => x + 1);", "var r = Api.Apply<int, int>(1, x => x + 1);")]
    [InlineData("var r = Api.Pipe(1, x => x + 1, y => y > 1);", "var r = Api.Pipe<int, int, bool>(1, x => x + 1, y => y > 1);")]
    [InlineData("var r = Api.Apply(4, Api.Show);", "var r = Api.Apply<int, string>(4, Api.Show);")]
    [InlineData("var r = Api.Twice(3, Api.Same);", "var r = Api.Twice<int>(3, Api.Same);")]
    public void AnInferredCallBindsWhatTheCallStatingItsTypeArgumentsBinds(string inferred, string stated)
    {
        Assert.Equal(Described(InitializerOf<InvocationExpression>(stated)), Described(InitializerOf<InvocationExpression>(inferred)));
    }

    [Fact]
    public void ATypeParameterALambdaWasTypedByTakesNoLaterBoundFromItsResult()
    {
        var call = InitializerOf<InvocationExpression>("var r = Api.Grow(1, x => x * 2L);");

        Assert.Equal("int", TypeDisplay.Mist(call.Type));
        Assert.Equal("func<int, int>", TypeDisplay.Mist(Assert.IsType<LambdaExpression>(call.Arguments[1]).Type));
    }

    [Fact]
    public void AResultNamingAFixedAndAnOpenParameterTeachesOnlyTheOpenOne()
    {
        var call = InitializerOf<InvocationExpression>("var r = Api.Both(1, x => new Pair<long, string>(2L, \"s\"));");

        Assert.Equal("int, string", string.Join(", ", Assert.IsType<MemberAccessExpression>(call.Receiver).TypeArguments.Select(TypeDisplay.Mist)));
    }

    [Theory]
    [InlineData("var r = Api.Transform(3, v => v * 2);", "int, func<int, int>")]
    [InlineData("var r = Api.Transform(\"ab\", v => v);", "string, func<string, string>")]
    [InlineData("var r = Api.Measure(x => x + 1);", "func<int, int>")]
    [InlineData("var r = Api.Measure(x => x * 2);", "func<int, int>")]
    [InlineData("var r = Api.Measure(s => s.Length);", "func<string, int>")]
    [InlineData("var r = Api.Widen(x => x + 1);", "func<int, int>")]
    [InlineData("var r = Api.Widen(x => x + 1L);", "func<int, long>")]
    [InlineData("var r = Api.Widen((int x) => x + 1);", "func<int, int>")]
    [InlineData("var r = Api.Tie((long x) => 4);", "func<long, int>")]
    public void AnOverloadedCallTypesTheLambdaUnderEachCandidateAndTakesTheOneItFits(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Empty(AllErrors(BuildRun(statement)));
        Assert.Equal(expected, string.Join(", ", call.Method!.ParametersList.Select(parameter => TypeDisplay.Mist(parameter.Type))));
        Assert.Equal(TypeDisplay.Mist(call.ParameterTypes![^1]), TypeDisplay.Mist(Assert.IsType<LambdaExpression>(call.Arguments[^1]).Type));
    }

    [Theory]
    [InlineData("var r = Api.Apply(4, Api.Show);", "int")]
    [InlineData("var r = Api.Apply(\"w\", Api.Show);", "string")]
    public void AGroupArgumentConvertsToTheOverloadTheInferredParametersName(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);
        var reference = Assert.IsType<FunctionReferenceExpression>(call.Arguments[1]);

        Assert.Equal(expected, TypeDisplay.Mist(Assert.Single(reference.Method.ParametersList).Type));
    }

    [Theory]
    [InlineData("var r = Api.Apply(4, maybe ?? Api.Next);", "int, int")]
    [InlineData("var r = Api.Twice(4, maybe ?? Api.Next);", "int")]
    public void AGroupNestedInAnArgumentIsConvertedWhereItStands(string statement, string expected)
    {
        const string preamble = "func<int, int>? maybe = void;";
        var call = InitializerOf<InvocationExpression>(statement, extraPreamble: preamble);

        Assert.Empty(AllErrors(BuildRun(statement, extraPreamble: preamble)));
        Assert.Equal(expected, string.Join(", ", Assert.IsType<MemberAccessExpression>(call.Receiver).TypeArguments.Select(TypeDisplay.Mist)));
    }

    [Fact]
    public void AnOverloadedConstructorTypesTheLambdaUnderEachCandidate()
    {
        var created = InitializerOf<NewExpression>("var r = new Labeled(s => s.Length);");

        Assert.Equal("func<string, int>", TypeDisplay.Mist(Assert.Single(created.Constructor!.ParametersList).Type));
    }

    [Fact]
    public void BindingALambdaUnderACandidateThatLosesRecordsNothing()
    {
        var model = BuildModel("var r = Api.Measure(s => Api.Same(s).Length);", "", "", out var diagnostics);

        Assert.Empty(AllErrors(diagnostics));

        var same = model.GenericCalls.All().Where(call => call.Method.Name == "Same").ToList();
        Assert.Equal("string", TypeDisplay.Mist(Assert.Single(Assert.Single(same).TypeArguments)));
    }

    [Theory]
    [InlineData("var r = Api.Apply(1, (string s) => s);", "MIST_PM_TYPE_ARGUMENTS_CONFLICT")]
    [InlineData("var r = Api.Apply2(1, \"x\", x => x);", "MIST_PM_TYPE_ARGUMENTS_CONFLICT")]
    [InlineData("var r = Api.Fixed(x => x);", "MIST_PM_TYPE_ARGUMENTS_NOT_INFERRED")]
    [InlineData("var r = Api.Apply(\"w\", Api.Next);", "MIST_PM_TYPE_ARGUMENTS_NOT_INFERRED")]
    [InlineData("var r = Api.Tie(x => 1);", "MIST_PM_AMBIGUOUS_OVERLOAD")]
    [InlineData("var r = Api.Apply(1, x => x.Nope);", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("var r = Api.Transform(3, v => v.Nope);", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("var r = Api.Measure(true switch { _ => Api.Next });", "MIST_PM_METHOD_GROUP_NOT_CONVERTED")]
    public void ACallItsCallablesCannotCompleteIsOneDiagnostic(string statement, string id)
    {
        var error = Assert.Single(AllErrors(BuildRun(statement)));

        Assert.Equal(id, error.Id.Value);
    }

    [Fact]
    public void AConflictBetweenAnArgumentAndALambdasStatedParameterNamesBoth()
    {
        var conflict = Assert.Single(AllErrors(BuildRun("var r = Api.Apply(1, (string s) => s);")));

        Assert.Contains("'int', 'string'", conflict.Message);
    }

    private static IEnumerable<Diagnostic> AllErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static string Described(InvocationExpression call)
    {
        var arguments = call.Arguments.Select(argument => argument switch
        {
            LambdaExpression lambda =>
                $"lambda({string.Join(", ", lambda.Parameters.Select(parameter => TypeDisplay.Mist(parameter.Type)))}) {TypeDisplay.Mist(lambda.Type)}",
            FunctionReferenceExpression reference =>
                $"{reference.Method.Name}({string.Join(", ", reference.Method.ParametersList.Select(parameter => TypeDisplay.Mist(parameter.Type)))})"
                + $"<{string.Join(", ", reference.TypeArguments.Select(TypeDisplay.Mist))}> {TypeDisplay.Mist(reference.Type)}",
            _ => $"{argument.GetType().Name} {TypeDisplay.Mist(argument.Type)}",
        });

        var typeArguments = Assert.IsType<MemberAccessExpression>(call.Receiver).TypeArguments.Select(TypeDisplay.Mist);

        return $"{call.Method?.Name}<{string.Join(", ", typeArguments)}>({string.Join("; ", arguments)}) "
            + $"over ({string.Join(", ", call.ParameterTypes!.Select(TypeDisplay.Mist))}) is {TypeDisplay.Mist(call.Type)}";
    }
}

public sealed class ProjectedCallableArgumentInferenceTests
{
    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProjectedSelectorsCloseTheirOwnResultAfterReceiverSpecialization(bool explicitResult, bool indexed)
    {
        var f = new BindingFragments();
        var method = Selector(f, indexed);
        var sibling = Selector(f, !indexed);
        var lambda = new UnboundLambdaExpression(Lambda(indexed, BindingFragments.Expression(BindingFragments.Identifier("x"))));
        var trials = new LambdaTrials(f.Binding, f.Builder);

        var choice = OverloadResolution.Select(
            new[] { sibling, method }, new Expression[] { lambda }, null,
            explicitResult ? [f.Built.Type("Int32")] : null, f.Built.Interner, lambdas: trials);

        Assert.Same(method, choice.Selected);
        Assert.Same(f.Built.Type("Int32"), choice.Substitution.Apply(method.ReturnType, f.Built.Interner));
        var target = choice.Substitution.Apply(Assert.Single(method.ParametersList).Type, f.Built.Interner);
        var settled = Assert.IsType<LambdaExpression>(LambdaTrials.BindAgainst(lambda, target, f.Binding, f.Builder));
        var signature = Assert.IsType<FunctionType>(settled.Type);
        Assert.Same(f.Built.Type("Int32"), signature.ReturnType);
        Assert.All(signature.ParameterTypes, input => Assert.Same(f.Built.Type("Int32"), input));
        Assert.Equal(indexed ? 2 : 1, signature.ParameterTypes.Count);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectedPredicateOverloadsSelectTheLambdaArity(bool indexed)
    {
        var f = new BindingFragments();
        var method = Predicate(f, indexed);
        var sibling = Predicate(f, !indexed);
        var lambda = new UnboundLambdaExpression(Lambda(indexed, BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode())));

        var choice = OverloadResolution.Select(
            new[] { sibling, method }, new Expression[] { lambda }, null, null,
            f.Built.Interner, lambdas: new LambdaTrials(f.Binding, f.Builder));

        Assert.Same(method, choice.Selected);
        var signature = Assert.IsType<FunctionType>(Assert.Single(method.ParametersList).Type);
        Assert.Same(f.Built.Type("Boolean"), signature.ReturnType);
        Assert.Equal(indexed ? 2 : 1, signature.ParameterTypes.Count);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("arity")]
    [InlineData("result")]
    [InlineData("parameter")]
    public void IncompatibleProjectedPredicatesRemainUnresolvable(string refusal)
    {
        var f = new BindingFragments();
        var methods = new[] { Predicate(f, false), Predicate(f, true) };
        var result = BindingFragments.Expression(refusal == "result"
            ? BindingFragments.Identifier("x")
            : new TrueLiteralExpressionPrimaryNode());
        var syntax = Lambda(false, result);
        if(refusal == "arity")
        {
            syntax.Params = new ParenthesizedLambdaParamsLambdaParameterListNode();
        }
        else if(refusal == "parameter")
        {
            syntax = BindingFragments.Lambda(result, "String");
        }

        var lambda = new UnboundLambdaExpression(syntax);
        var arguments = new Expression[] { lambda };
        var choice = OverloadResolution.Select(methods, arguments, null, null,
            f.Built.Interner, lambdas: new LambdaTrials(f.Binding, f.Builder));

        Assert.Null(choice.Selected);
        Assert.Equal(methods, choice.Unresolvable);
        OverloadFailure.Report(choice, methods, arguments, "method 'Where'", HandBuiltModel.Node(), f.Diagnostics);
        Assert.Equal("MIST_PM_UNRESOLVABLE_OVERLOAD", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void SiblingArgumentSuppliesTheProjectedSelectorsInputBeforeItsBodyTeachesTheResult()
    {
        var f = new BindingFragments();
        var method = f.Function("Apply", f.Built.Type("Void"));
        var input = new TypeParameter("T", 2, method.Scope!, null);
        var result = new TypeParameter("R", 4, method.Scope!, null);
        method.TypeParametersList.AddRange([input, result]);
        method.ParametersList.AddRange([new Parameter("seed", input), new Parameter("selector", new FunctionType([input], result))]);
        var lambda = new UnboundLambdaExpression(BindingFragments.Lambda(BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode())));

        var inferred = TypeArgumentInference.Infer(method,
            [HandBuiltModel.Literal(f.Built.Type("Int32")), lambda], TypeSubstitution.Empty,
            f.Built.Interner, new LambdaTrials(f.Binding, f.Builder), out var conflict);

        Assert.Null(conflict);
        Assert.Equal(new IMistType[] { f.Built.Type("Int32"), f.Built.Type("Boolean") }, inferred);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    [Trait("Tier", "1")]
    public void NominalDelegateArgumentsTeachStructuralParameterAndResultSlots()
    {
        var f = new HandBuiltModel();
        var scope = new Scope(null);
        var t = new TypeParameter("T", 2, scope, null);
        var r = new TypeParameter("R", 4, scope, null);
        var method = new Method("Apply", r, [new Parameter("callback", new FunctionType([t], r))], default) { Scope = scope };
        method.TypeParametersList.AddRange([t, r]);
        var multicast = new ClassType("MulticastDelegate", "System", new Scope(null), null, null);
        var callback = HandBuiltModel.Class("Transform", multicast);
        callback.MethodsList.Add(new Method("Invoke", f.Type("Boolean"), [new Parameter("value", f.Type("String"))], default));

        var inferred = TypeArgumentInference.Infer(method, [HandBuiltModel.Literal(callback)], TypeSubstitution.Empty, f.Interner, null, out var conflict);

        Assert.Null(conflict);
        Assert.Equal(new IMistType[] { f.Type("String"), f.Type("Boolean") }, inferred);
        var substitution = TypeSubstitution.ForMethod(method, inferred);
        Assert.Same(f.Type("String"), substitution.Apply(t, f.Interner));
        Assert.Same(f.Type("Boolean"), substitution.Apply(r, f.Interner));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void SparseProjectedBlockParametersUnifyAndSubstituteInDeclarationOrder()
    {
        var f = new HandBuiltModel();
        var t = new TypeParameter("T", 2, new Scope(null), null);
        var declared = new FunctionType([t], t);
        var actual = new FunctionType([f.Type("String")], f.Type("String"));

        var inferred = Assert.IsAssignableFrom<IReadOnlyList<IMistType>>(TypeArgumentInference.Unify(declared, actual, [t]));
        Assert.Same(f.Type("String"), Assert.Single(inferred));
        Assert.Same(f.Type("String"), TypeSubstitution.ForParameters([t], inferred).Apply(t, f.Interner));
    }

    private static Method Selector(BindingFragments f, bool indexed)
    {
        var scope = new Scope(null);
        var result = new TypeParameter("TResult", 1, scope, null);
        var method = new Method("Select", result, [], default) { Scope = scope };
        method.TypeParametersList.Add(result);
        method.ParametersList.Add(new Parameter("selector", new FunctionType(
            indexed ? [f.Built.Type("Int32"), f.Built.Type("Int32")] : [f.Built.Type("Int32")], result)));
        return method;
    }

    private static Method Predicate(BindingFragments f, bool indexed)
    {
        return f.Function("Where", f.Built.Type("Void"), new FunctionType(
            indexed ? [f.Built.Type("Int32"), f.Built.Type("Int32")] : [f.Built.Type("Int32")], f.Built.Type("Boolean")));
    }

    private static ExpressionLambdaLambdaExpressionNode Lambda(bool indexed, ExpressionNode body)
    {
        var lambda = BindingFragments.Lambda(body);
        if(indexed)
        {
            lambda.Params = new ParenthesizedLambdaParamsLambdaParameterListNode
            {
                Params = new LambdaParamListNode
                {
                    LambdaParam = new UntypedLambdaParamLambdaParamNode { Name = BindingFragments.Token("x") },
                    LambdaParams = [new UntypedLambdaParamLambdaParamNode { Name = BindingFragments.Token("index") }],
                },
            };
        }

        return lambda;
    }
}