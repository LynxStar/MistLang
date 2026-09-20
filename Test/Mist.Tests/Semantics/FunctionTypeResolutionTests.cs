// Function types and lambdas: what the result slot is, what a lambda's parameters and result are typed
// by, and which overload a method group converts to. Asserted on the model because emission maps a
// function type onto a CLR family that C# would often accept whatever Mist decided -- an inferred
// parameter type, an inferred result, and an overload picked by target are each invisible in output that
// merely runs. Orders 104, 105, 108 and 110 of backlog/emitter-v2-capability-progression.yaml rest on
// this file.

using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class FunctionTypeResolutionTests : ProgramModelProbe
{
    public FunctionTypeResolutionTests()
        : base("MistFunctionType_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Doubler
        {
            public int Twice(int x)
            {
                return x * 2;
            }
        }

        public class Overloaded
        {
            public static int Pick(int x)
            {
                return x;
            }

            public static string Pick(string s)
            {
                return s;
            }

            public static int Add(int a, int b)
            {
                return a + b;
            }

            public static void Sink(string s)
            {
            }

            public static T Echo<T>(T value)
            {
                return value;
            }

            public static R Turn<T, R>(T value, R fallback)
            {
                return fallback;
            }

            public static T First<T>(T left, T right)
            {
                return left;
            }

            public static Pair<T, T> Fresh<T>()
            {
                return new Pair<T, T>();
            }

            public static int Apply(func<long, long> f)
            {
                return 0;
            }

            public static int Apply(func<string, string, string> f)
            {
                return 1;
            }

            public static int Either<T, R>(T | R value)
            {
                return 0;
            }

            public static string Shown(int value)
            {
                return "plain";
            }

            public static string Shown<T>(T value)
            {
                return "generic";
            }
        }

        public class Keeper<K>
        {
            public K Kept(K value)
            {
                return value;
            }

            public K Through(K value)
            {
                func<K, K> kept = Kept;
                func<K, K> echoed = Overloaded.Echo;
                return echoed(kept(value));
            }

            public string With<U>(U other)
            {
                return "with";
            }
        }

        public abstract ITaker<K>
        {
            int Take(K? key);
        }

        public class Lifted<K> : ITaker<K>
        {
            public int Take(K? key)
            {
                return 1;
            }

            public int Take(int? number)
            {
                return 2;
            }
        }

        public class Pair<A, B>
        {
            public int Take(A left)
            {
                return 1;
            }

            public int Take(B right)
            {
                return 2;
            }
        }
        """;

    protected override string Preamble { get; } = "var seed = 1;";

    // ── Order 104: the type ──────────────────────────────────────────────

    [Fact]
    public void TheLastTypeArgumentIsTheResultAndTheRestAreParameters()
    {
        var function = Assert.IsType<FunctionType>(TypeOfLocal("func<int, string, bool> f = null;"));

        Assert.Equal(
            ["Standard.Int32", "Standard.String"],
            function.ParameterTypes.Select(parameter => parameter.FullyQualifiedName));

        Assert.Equal("Standard.Boolean", function.ReturnType.FullyQualifiedName);
    }

    [Fact]
    public void ASoleTypeArgumentIsTheResultAndThereAreNoParameters()
    {
        var function = Assert.IsType<FunctionType>(TypeOfLocal("func<bool> f = null;"));

        Assert.Empty(function.ParameterTypes);
        Assert.Equal("Standard.Boolean", function.ReturnType.FullyQualifiedName);
    }

    [Fact]
    public void TwoSpellingsOfOneSignatureReachOneInternedType()
    {
        var body = BodyIn("func<int, bool> second = null;", extraPreamble: "func<int, bool> first = null;");
        var declared = body.Statements.OfType<LocalDeclarationStatement>().ToList();

        Assert.Same(
            declared.First(statement => statement.Local.Name == "first").Local.Type,
            declared.First(statement => statement.Local.Name == "second").Local.Type);
    }

    [Fact]
    public void AFunctionTypeAndATupleOfTheSameTypesAreNotTheSameType()
    {
        var body = BodyIn("(int, bool) pair = (1, true);", extraPreamble: "func<int, bool> f = null;");
        var declared = body.Statements.OfType<LocalDeclarationStatement>().ToList();

        Assert.NotSame(
            declared.First(statement => statement.Local.Name == "f").Local.Type,
            declared.First(statement => statement.Local.Name == "pair").Local.Type);
    }

    [Fact]
    public void CallingAFunctionTypedValueResultsInItsReturnType()
    {
        var local = SingleLocal("var got = f(1);", extraPreamble: "func<int, bool> f = null;");

        Assert.Equal("Standard.Boolean", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void CallingWithTheWrongNumberOfArgumentsIsRefused()
    {
        var diagnostics = BuildRun("var got = f(1, 2);", extraPreamble: "func<int, bool> f = null;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_FUNCTION_CALL_ARITY"));
    }

    // ── Order 105: lambdas ──────────────────────────────────────────────

    [Fact]
    public void AnUntypedLambdaParameterIsTypedByTheTarget()
    {
        var lambda = LambdaIn("func<int, int> f = x => x;");

        Assert.Equal("Standard.Int32", Assert.Single(lambda.Parameters).Type.FullyQualifiedName);
    }

    [Fact]
    public void AnAnnotatedLambdaParameterStandsWithoutATarget()
    {
        var lambda = LambdaIn("var f = (int x) => x;");
        var function = Assert.IsType<FunctionType>(lambda.Type);

        Assert.Equal("Standard.Int32", function.ReturnType.FullyQualifiedName);
    }

    [Fact]
    public void AnUntypedLambdaParameterWithNoTargetIsRefused()
    {
        var diagnostics = BuildRun("var f = x => x;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_LAMBDA_PARAMETER_NO_TYPE"));
    }

    [Fact]
    public void ALambdaOfTheWrongArityForItsTargetIsRefused()
    {
        var diagnostics = BuildRun("func<int, int> f = (x, y) => x;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_LAMBDA_ARITY"));
    }

    [Fact]
    public void ABlockLambdasResultIsReadOffItsReturns()
    {
        var lambda = LambdaIn("var f = (int n) => { return \"text\"; };");
        var function = Assert.IsType<FunctionType>(lambda.Type);

        Assert.Equal("Standard.String", function.ReturnType.FullyQualifiedName);
    }

    [Fact]
    public void ABlockLambdaThatReturnsNothingIsVoid()
    {
        var lambda = LambdaIn("var f = (int n) => { var unused = n; };");
        var function = Assert.IsType<FunctionType>(lambda.Type);

        Assert.Equal(WellKnownTypes.Void.FullyQualifiedName, function.ReturnType.FullyQualifiedName);
    }

    [Fact]
    public void ABlockLambdaWhoseReturnsDisagreeIsRefused()
    {
        var diagnostics = BuildRun(
            "var f = (int n) => { if (n > 0) { return 1; } return \"text\"; };");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_LAMBDA_RETURNS_NO_COMMON_TYPE"));
    }

    /// <summary>
    /// The parameters are the SAME locals the body's references resolve to, which is what makes capture
    /// by reference rather than by copy -- order 106 rests on this being true of the model and not only
    /// of the C# it emits.
    /// </summary>
    [Fact]
    public void ALambdaBodyReadsTheVeryParameterTheLambdaDeclared()
    {
        var lambda = LambdaIn("func<int, int> f = x => x;");
        var read = Assert.IsType<LocalReferenceExpression>(lambda.Result);

        Assert.Same(Assert.Single(lambda.Parameters), read.Local);
    }

    [Fact]
    public void AStaticLambdaIsRecordedAsStatic()
    {
        Assert.True(LambdaIn("func<int, int> f = static x => x;").IsStatic);
        Assert.False(LambdaIn("func<int, int> f = x => x;").IsStatic);
    }

    // ── Order 108: method group conversion ──────────────────────────────

    [Fact]
    public void AMethodGroupConvertsToTheOverloadMatchingTheTarget()
    {
        var reference = FunctionReferenceIn("func<string, string> f = Overloaded.Pick;");

        Assert.Equal("Standard.String", Assert.Single(reference.Method.ParametersList).Type.FullyQualifiedName);
    }

    [Fact]
    public void AMethodGroupConvertsAgainstAVoidResult()
    {
        var reference = FunctionReferenceIn("func<string, void> f = Overloaded.Sink;");

        Assert.Equal("Sink", reference.Method.Name);
    }

    [Fact]
    public void AMethodGroupOfTheWrongArityDoesNotConvert()
    {
        var diagnostics = BuildRun("func<int, int> f = Overloaded.Add;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_METHOD_GROUP_NOT_CONVERTED"));
    }

    /// <summary>
    /// Identity per position and not assignability: an <c>int</c> parameter would WIDEN to a long at a
    /// call, but a delegate binds by exact signature, so the conversion is declined.
    /// </summary>
    [Fact]
    public void AMethodGroupWhoseParameterMerelyWidensDoesNotConvert()
    {
        var diagnostics = BuildRun("func<long, int> f = Overloaded.Pick;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_METHOD_GROUP_NOT_CONVERTED"));
    }

    [Theory]
    [InlineData("func<int, int> f = Overloaded.Echo;", "Echo", "Standard.Int32")]
    [InlineData("func<string, string> f = Overloaded.Echo;", "Echo", "Standard.String")]
    [InlineData("func<string, int, int> f = Overloaded.Turn;", "Turn", "Standard.String, Standard.Int32")]
    [InlineData("func<int, int, int> f = Overloaded.First;", "First", "Standard.Int32")]
    [InlineData("func<bool, string> f = keeper.With;", "With", "Standard.Boolean")]
    [InlineData("func<Pair<string, string>> f = Overloaded.Fresh;", "Fresh", "Standard.String")]
    public void AGenericMethodGroupTakesItsTypeArgumentsFromTheTarget(string statement, string method, string typeArguments)
    {
        var reference = FunctionReferenceIn(statement, extraPreamble: "var keeper = new Keeper<int>();");

        Assert.Equal(method, reference.Method.Name);
        Assert.Equal(typeArguments, string.Join(", ", reference.TypeArguments.Select(argument => argument.FullyQualifiedName)));
    }

    [Theory]
    [InlineData("func<int, long, long> f = Overloaded.First;")]
    [InlineData("func<int | string, int> f = Overloaded.Either;")]
    [InlineData("func<int, string, int> f = Overloaded.Echo;")]
    public void AGenericMethodGroupTheTargetLeavesOpenOrPullsTwoWaysDoesNotConvert(string statement)
    {
        _ = Assert.Single(Errors(BuildRun(statement), "MIST_PM_METHOD_GROUP_NOT_CONVERTED"));
    }

    [Theory]
    [InlineData("func<int, string> f = Overloaded.Shown;", 0)]
    [InlineData("func<string, string> f = Overloaded.Shown;", 1)]
    public void AGenericMethodGroupYieldsToAPlainOverloadOfTheSameSignature(string statement, int typeParameters)
    {
        Assert.Equal(typeParameters, FunctionReferenceIn(statement).Method.TypeParametersList.Count);
    }

    [Fact]
    public void AMethodGroupMatchesAPositionTypedByTheEnclosingTypesOwnParameter()
    {
        Assert.Empty(Errors(BuildRun(""), "MIST_PM_METHOD_GROUP_NOT_CONVERTED"));
    }

    [Theory]
    [InlineData("func<long, long> f = Overloaded.Echo;")]
    [InlineData("var applied = Overloaded.Apply(Overloaded.Echo);")]
    public void AnInferredMethodGroupIsRecordedAsTheInstantiationItNames(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out _);
        var echo = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Overloaded"]).MethodsList.Single(method => method.Name == "Echo");

        Assert.Contains(
            model.GenericCalls.All(),
            call => ReferenceEquals(call.Method, echo) && call.TypeArguments is [{ FullyQualifiedName: "Standard.Int64" }]);
    }

    /// <summary>
    /// The receiver's argument makes both a <c>Take(int?)</c>; the one fixed at the declaration is
    /// chosen as a call would choose it (method-overloading Requirement 3.12).
    /// </summary>
    [Fact]
    public void AMethodGroupOfACoincidingPairConvertsToTheFixedOverload()
    {
        var reference = FunctionReferenceIn(
            "func<int?, int> f = lifted.Take;", extraPreamble: "var lifted = new Lifted<int>();");

        Assert.Equal("int | void", TypeDisplay.Mist(Assert.Single(reference.Method.ParametersList).Type));
    }

    [Fact]
    public void AMethodGroupConvertsToTheFillerAndNotTheSlotItAnswers()
    {
        var reference = FunctionReferenceIn(
            "func<string?, int> f = words.Take;", extraPreamble: "var words = new Lifted<string>();");

        Assert.IsNotType<AbstractType>(reference.Method.DeclaringType);
    }

    [Fact]
    public void AMethodGroupNoPositionSeparatesIsAmbiguous()
    {
        var diagnostics = BuildRun("func<int, int> f = pair.Take;", extraPreamble: "var pair = new Pair<int, int>();");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_METHOD_GROUP_AMBIGUOUS"));
    }

    [Fact]
    public void AnInstanceMethodGroupKeepsItsReceiver()
    {
        var reference = FunctionReferenceIn(
            "func<int, int> f = d.Twice;", extraPreamble: "var d = new Doubler();");

        Assert.NotNull(reference.Receiver);
        Assert.Equal("Twice", reference.Method.Name);
    }

    // ── Order 110: aliases ──────────────────────────────────────────────

    [Fact]
    public void AnAliasOverAFunctionTypeClosesBothItsParametersAndItsResult()
    {
        var function = Assert.IsType<FunctionType>(TypeOfLocal(
            "Transform<int, string> f = null;",
            extraTypes: "public type Transform<TIn, TOut> = func<TIn, TOut>;"));

        Assert.Equal("Standard.Int32", Assert.Single(function.ParameterTypes).FullyQualifiedName);
        Assert.Equal("Standard.String", function.ReturnType.FullyQualifiedName);
    }

    [Fact]
    public void AnAliasOverAFunctionTypeReachesTheSameInternedTypeAsSpellingItOut()
    {
        var body = BodyIn(
            "func<int, bool> spelled = null;",
            extraTypes: "public type Predicate<T> = func<T, bool>;",
            extraPreamble: "Predicate<int> aliased = null;");

        var declared = body.Statements.OfType<LocalDeclarationStatement>().ToList();

        Assert.Same(
            declared.First(statement => statement.Local.Name == "aliased").Local.Type,
            declared.First(statement => statement.Local.Name == "spelled").Local.Type);
    }

    [Fact]
    public void ANestedFunctionTypeCarriesAFunctionTypeAsItsResult()
    {
        var function = Assert.IsType<FunctionType>(TypeOfLocal("func<int, func<int, int>> f = null;"));
        var inner = Assert.IsType<FunctionType>(function.ReturnType);

        Assert.Equal("Standard.Int32", Assert.Single(inner.ParameterTypes).FullyQualifiedName);
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private IMistType TypeOfLocal(string statement, string extraTypes = "")
    {
        return DeclarationIn(statement, extraTypes).Local.Type;
    }

    private LambdaExpression LambdaIn(string statement)
    {
        return InitializerOf<LambdaExpression>(statement);
    }

    private FunctionReferenceExpression FunctionReferenceIn(string statement, string extraPreamble = "")
    {
        return InitializerOf<FunctionReferenceExpression>(statement, extraPreamble: extraPreamble);
    }
}