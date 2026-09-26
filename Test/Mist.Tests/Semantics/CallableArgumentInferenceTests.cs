// Tier 3 (Component): a lambda or a method group passed to a call that has not yet fixed its type
// arguments or chosen its overload. The ordinary arguments fix what they can, the callable is typed by
// that, and its result teaches the rest; under several candidates each types the lambda its own way and
// the lambda can decide between them. Asserted on the model because the emitter states what the model
// chose and C# would infer or select most of these calls again by itself. Orders 55 and 108 of
// backlog/emitter-v2-capability-progression.yaml rest on this file.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
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