// Tier 3 (Component): which overload a call selects, asserted against a real ProgramModelBuilder run
// over minimal source. See ProgramModelProbe for the harness and for why these assert on the model
// rather than on output.
//
// Several fixtures below declare the wanted overload SECOND on purpose. Taking the first candidate is
// what selection replaced, so a fixture that happens to want the first one passes either way.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class OverloadSelectionTests : ProgramModelProbe
{
    private const string NoOverloadId = "MIST_PM_NO_APPLICABLE_OVERLOAD";
    private const string AmbiguousId = "MIST_PM_AMBIGUOUS_OVERLOAD";
    private const string UnresolvableId = "MIST_PM_UNRESOLVABLE_OVERLOAD";
    private const string MethodGroupId = "MIST_PM_METHOD_GROUP_NOT_CONVERTED";

    private const string Mixer = """
        public class Mixer
        {
            public static int Take(int a, int b) { return 1; }

            public static int Take(double a, double b) { return 2; }
        }
        """;

    private const string FixtureTypes = """
        public class Printer
        {
            public Printer() { }

            public Printer(int width) { }

            public string Format(int value) { return "int"; }

            public string Format(bool value) { return "bool"; }

            public string Format(double value) { return "double"; }

            public string Format(string value) { return "string"; }
        }

        public class Sizer
        {
            public static int Of(int a) { return 1; }

            public static int Of(int a, int b) { return 2; }

            public static int Of(int a, int b, int c) { return 3; }
        }

        public class Plain
        {
            public static int Once(int a) { return a; }
        }
        """;

    public OverloadSelectionTests()
        : base("MistOverload_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        int i = 1;
        bool b = true;
        double d = 1.5;
        string s = "text";
        var p = new Printer();
        """;

    /// <summary>
    /// Arguments are explicitly typed locals rather than literals, so a wrong answer here cannot be
    /// blamed on how <c>1.5</c> types.
    /// </summary>
    [Theory]
    [InlineData("i", "Standard.Int32")]
    [InlineData("b", "Standard.Boolean")]
    [InlineData("d", "Standard.Double")]
    [InlineData("s", "Standard.String")]
    public void SameArityOverloads_SelectByArgumentType(string argument, string expectedParameterType)
    {
        var invocation = SingleInvocation($"var chosen = p.Format({argument});");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal(expectedParameterType, parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void ALiteralArgument_SelectsTheMatchingOverload()
    {
        var invocation = SingleInvocation("var chosen = p.Format(\"text\");");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.String", parameter.Type.FullyQualifiedName);
    }

    /// <summary>
    /// The string overload is declared FIRST deliberately: a fixture whose wanted overload sits first
    /// passes even when nothing is selected at all.
    /// </summary>
    [Fact]
    public void TheSelectedOverloadsReturnType_FlowsIntoAnInferredLocal()
    {
        var local = SingleLocal(
            "var picked = Widen.Pick(i);",
            extraTypes: """
                public class Widen
                {
                    public static string Pick(string value) { return value; }

                    public static int Pick(int value) { return value; }
                }
                """);

        Assert.Equal("Standard.Int32", local.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("i", 1)]
    [InlineData("i, i", 2)]
    [InlineData("i, i, i", 3)]
    public void OverloadsDifferingInArity_SelectByArgumentCount(string arguments, int expectedArity)
    {
        var invocation = SingleInvocation($"var chosen = Sizer.Of({arguments});");

        Assert.NotNull(invocation.Method);
        Assert.Equal(expectedArity, invocation.Method.ParametersList.Count);
    }

    /// <summary>
    /// Constructors reach the same selection because <c>Constructor</c> derives from <c>Method</c>.
    /// </summary>
    [Theory]
    [InlineData("", 0)]
    [InlineData("i", 1)]
    public void ConstructorOverloads_SelectByArgumentCount(string arguments, int expectedArity)
    {
        var creation = SingleNew($"var made = new Printer({arguments});");

        Assert.NotNull(creation.Constructor);
        Assert.Equal(expectedArity, creation.Constructor.ParametersList.Count);
    }

    [Fact]
    public void ACallMatchingNoArity_IsRefusedAtTheMistSource()
    {
        var diagnostics = BuildRun("var chosen = Sizer.Of(i, i, i, i);");

        var reported = Assert.Single(Errors(diagnostics, NoOverloadId));
        Assert.Contains("Of", reported.Message, StringComparison.Ordinal);
        Assert.Contains("1, 2, 3", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Named as a constructor, so the reader is not sent looking for a method.
    /// </summary>
    [Fact]
    public void AConstructionMatchingNoArity_IsRefused()
    {
        var diagnostics = BuildRun("var made = new Printer(i, i, i);");

        var reported = Assert.Single(Errors(diagnostics, NoOverloadId));
        Assert.Contains("constructor 'Printer'", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A resolver that simply rejects more is not a resolver that chooses better, so every rule above
    /// needs a case it must leave alone.
    /// </summary>
    [Theory]
    [InlineData("var chosen = Sizer.Of(i);")]
    [InlineData("var chosen = Sizer.Of(i, i);")]
    [InlineData("var chosen = Sizer.Of(i, i, i);")]
    [InlineData("var chosen = p.Format(i);")]
    [InlineData("var chosen = Plain.Once(i);")]
    [InlineData("var made = new Printer();")]
    [InlineData("var made = new Printer(i);")]
    public void AWellFormedCall_IsNotRefused(string statement)
    {
        var diagnostics = BuildRun(statement);

        Assert.Empty(Errors(diagnostics, NoOverloadId));
        Assert.Empty(Errors(diagnostics, AmbiguousId));
        Assert.Empty(Errors(diagnostics, UnresolvableId));
    }

    [Fact]
    public void ANonOverloadedCall_StillRecordsItsMethod()
    {
        var invocation = SingleInvocation("var chosen = Plain.Once(i);");

        Assert.NotNull(invocation.Method);
        Assert.Equal("Once", invocation.Method.Name);
    }

    [Fact]
    public void ATypeWithNoDeclaredConstructor_RecordsNoneAndIsNotRefused()
    {
        var diagnostics = BuildRun("var made = new Bare();", extraTypes: "public class Bare { }");
        Assert.Empty(Errors(diagnostics, NoOverloadId));

        var creation = SingleNew("var made = new Bare();", extraTypes: "public class Bare { }");
        Assert.Null(creation.Constructor);
    }

    /// <summary>
    /// Settled by APPLICABILITY alone, with no tiebreak involved: only the double form accepts both
    /// arguments, so selecting the int form would pick a candidate the call cannot legally reach.
    /// </summary>
    [Fact]
    public void MixedNumericArguments_SelectTheOnlyApplicableOverload()
    {
        var invocation = SingleInvocation("var chosen = Mixer.Take(i, d);", extraTypes: Mixer);

        Assert.NotNull(invocation.Method);
        Assert.All(invocation.Method.ParametersList, p => Assert.Equal("Standard.Double", p.Type.FullyQualifiedName));
    }

    /// <summary>
    /// Both <c>Take</c> forms accept two ints, so applicability leaves a contest and betterness
    /// settles it.
    /// </summary>
    [Fact]
    public void AnExactMatch_BeatsAWidening()
    {
        var invocation = SingleInvocation("var chosen = Mixer.Take(i, i);", extraTypes: Mixer);

        Assert.NotNull(invocation.Method);
        Assert.All(invocation.Method.ParametersList, p => Assert.Equal("Standard.Int32", p.Type.FullyQualifiedName));
    }

    /// <summary>
    /// Neither candidate is exact, so betterness falls to the narrower target. The double form is
    /// declared first, so first-match would pick it.
    /// </summary>
    [Fact]
    public void WithNoExactMatch_TheNarrowerTargetWins()
    {
        var invocation = SingleInvocation(
            "var chosen = Widths.Hold(i);",
            extraTypes: """
                public class Widths
                {
                    public static int Hold(double value) { return 1; }

                    public static int Hold(long value) { return 2; }
                }
                """);

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.Int64", parameter.Type.FullyQualifiedName);
    }

    /// <summary>
    /// Applicability is not only numeric: <c>Accept</c> is declared with the string form second, and
    /// only the base-typed one admits a <c>Cub</c>.
    /// </summary>
    [Fact]
    public void ADerivedArgument_ReachesABaseTypedParameter()
    {
        var invocation = SingleInvocation(
            "var chosen = Sink.Accept(new Cub());",
            extraTypes: """
                public class Animal { }

                public class Cub : Animal { }

                public class Sink
                {
                    public static int Accept(Animal a) { return 1; }

                    public static int Accept(string s) { return 2; }
                }
                """);

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Probe.Animal", parameter.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("var chosen = Held.Take(box);")]
    [InlineData("var chosen = Held.Take(intBox);")]
    [InlineData("var chosen = HeldOrAnything.Take(box);")]
    public void AConstructedArgument_ReachesTheBaseItsDefinitionDeclaresOverItsOwnArguments(string statement)
    {
        var invocation = SingleInvocation(statement, ConstructedTypes, ConstructedPreamble);

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("IHold<int>", TypeDisplay.Mist(parameter.Type));
    }

    [Fact]
    public void AConstructedArgument_DoesNotReachABaseConstructedOverAnotherArgument()
    {
        var diagnostics = BuildRun("var chosen = HeldText.Take(box);", ConstructedTypes, ConstructedPreamble);

        _ = Assert.Single(Errors(diagnostics, UnresolvableId));
    }

    [Theory]
    [InlineData("var chosen = Signs.Take(small);")]
    [InlineData("var chosen = WideSigns.Take(mid);")]
    [InlineData("var chosen = ConstantSigns.Take(2);")]
    public void BetweenIntegralParametersNeitherContaining_NeitherIsPreferred(string statement)
    {
        _ = Assert.Single(Errors(BuildRun(statement, SignTypes, "byte small = 2; ushort mid = 3;"), AmbiguousId));
    }

    [Theory]
    [InlineData("var chosen = Signs.Take(mid);", "Standard.UInt32")]
    [InlineData("var chosen = ConstantSigns.Take(200);", "Standard.Byte")]
    [InlineData("var chosen = Signs.Take((short)small);", "Standard.Int16")]
    [InlineData("var chosen = Signs.Take((uint)small);", "Standard.UInt32")]
    [InlineData("var chosen = ConstantSigns.Take((sbyte)2);", "Standard.SByte")]
    [InlineData("var chosen = ConstantSigns.Take((byte)2);", "Standard.Byte")]
    [InlineData("sbyte signedTwo = 2; var chosen = ConstantSigns.Take(signedTwo);", "Standard.SByte")]
    public void BetweenIntegralParametersNeitherContaining_OneTheArgumentAloneReachesIsChosen(string statement, string expectedParameterType)
    {
        var invocation = SingleInvocation(statement, SignTypes, "byte small = 2; ushort mid = 3;");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal(expectedParameterType, parameter.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("var chosen = Fit.Take(2);", "Standard.Byte")]
    [InlineData("var chosen = Fit.Take(255);", "Standard.Byte")]
    [InlineData("var chosen = Fit.Take(256);", "Standard.Int64")]
    [InlineData("var chosen = Fit.Take(-2);", "Standard.Int64")]
    [InlineData("var chosen = OptionalFit.Take(2);", "Standard.Byte | Standard.Void")]
    [InlineData("var chosen = WideOptionalFit.Take(2);", "Standard.Byte | Standard.Void")]
    public void AnIntegerConstant_ReachesTheNarrowestIntegralParameterItFits(string statement, string expectedParameterType)
    {
        var invocation = SingleInvocation(statement, ConstantTypes);

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal(expectedParameterType, parameter.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("var chosen = Unfit.Take(300);")]
    [InlineData("var chosen = Unfit.Take(i);")]
    public void AnIntegralValueThatIsNotAFittingConstant_ReachesNoNarrowerParameter(string statement)
    {
        var diagnostics = BuildRun(statement, ConstantTypes);

        _ = Assert.Single(Errors(diagnostics, UnresolvableId));
    }

    /// <summary>
    /// Both forms accept two ints and each is exact in one position and widened in the other, so
    /// neither beats the other.
    /// </summary>
    [Fact]
    public void AGenuineTie_IsReportedAsAmbiguous()
    {
        var diagnostics = BuildRun(
            "var chosen = Cross.Pair(i, i);",
            extraTypes: """
                public class Cross
                {
                    public static int Pair(int a, double b) { return 1; }

                    public static int Pair(double a, int b) { return 2; }
                }
                """);

        var reported = Assert.Single(Errors(diagnostics, AmbiguousId));
        Assert.Contains("Pair", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(int, double)", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(double, int)", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tie names only the candidates nothing beats. An int reaches decimal, float and double, but
    /// float beats double, so double must not appear -- comparing each candidate against a running
    /// winner rather than against every other would leave it there.
    /// </summary>
    [Fact]
    public void AReportedTie_ExcludesCandidatesAnotherBeats()
    {
        var diagnostics = BuildRun(
            "var chosen = Spread.Hold(i);",
            extraTypes: """
                public class Spread
                {
                    public static int Hold(decimal value) { return 1; }

                    public static int Hold(double value) { return 2; }

                    public static int Hold(float value) { return 3; }
                }
                """);

        var reported = Assert.Single(Errors(diagnostics, AmbiguousId));
        Assert.Contains("decimal", reported.Message, StringComparison.Ordinal);
        Assert.Contains("float", reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("double", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An int reaches neither a string nor a bool by any conversion ConversionRelation models, so
    /// applicability eliminates every candidate and leaves nothing to choose on. Refused rather than
    /// bound to whichever form is declared first, which is the one thing a partial relation does not
    /// license: declining to refuse is not the same as inventing a selection.
    /// </summary>
    [Fact]
    public void WhenNoCandidateIsKnownApplicable_TheCallIsRefused()
    {
        const string unreachable = """
            public class Unreachable
            {
                public static int Take(string a) { return 1; }

                public static int Take(bool a) { return 2; }
            }
            """;

        var diagnostics = BuildRun("var chosen = Unreachable.Take(i);", extraTypes: unreachable);

        var reported = Assert.Single(Errors(diagnostics, UnresolvableId));
        Assert.Contains("Take", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(string)", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(bool)", reported.Message, StringComparison.Ordinal);

        Assert.Empty(Errors(diagnostics, NoOverloadId));
        Assert.Empty(Errors(diagnostics, AmbiguousId));
    }

    /// <summary>
    /// <c>BackendDiagnostics.MethodGroupReachedEmission</c> is meant to be unreachable, which holds
    /// only while every position unable to consume a group reports one instead. These are the two
    /// such positions: a name nothing follows, and an argument, which cannot resolve because the
    /// overload it belongs to is not selected until after its arguments are bound.
    /// </summary>
    [Theory]
    [InlineData("var f = Plain.Once;")]
    [InlineData("var c = Plain.Once(Plain.Once);")]
    public void AMethodGroupNothingConsumes_IsReportedAtBinding(string statement)
    {
        var diagnostics = BuildRun(statement);

        var reported = Assert.Single(Errors(diagnostics, MethodGroupId));
        Assert.Contains("Once", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// TWO overloads of the same arity, which is what leaves the group with no target type: the binder
    /// cannot know which parameter the argument is for until the argument's type has picked it. So the
    /// group survives binding untyped and SELECTION converts it, asking each candidate whether one of
    /// the group's overloads is the function it declares.
    /// </summary>
    [Fact]
    public void AMethodGroupAmongSeveralSameArityOverloads_PicksTheFunctionTypedOne()
    {
        var invocation = SingleInvocation("var chosen = Combiner.Apply(Combiner.Add);", GroupTypes);

        Assert.NotNull(invocation.Method);
        _ = Assert.IsType<FunctionType>(Assert.Single(invocation.Method.ParametersList).Type);

        var argument = Assert.IsType<FunctionReferenceExpression>(Assert.Single(invocation.Arguments));
        Assert.Equal("Add", argument.Method.Name);
    }

    [Fact]
    public void AMethodGroupNoSameArityOverloadAccepts_IsReportedOnceAsTheCall()
    {
        var diagnostics = BuildRun("var chosen = Combiner.Apply(Combiner.Join);", GroupTypes);

        _ = Assert.Single(Errors(diagnostics, UnresolvableId));
        Assert.Empty(Errors(diagnostics, MethodGroupId));
    }

    /// <summary>
    /// TWO overloads on purpose. A lone candidate is returned without applicability being consulted at
    /// all, so a single-overload fixture passes whether or not the conversion is seen -- which is why
    /// the CastOperators sample cannot cover this and this fixture can.
    /// </summary>
    [Fact]
    public void AUserDefinedImplicitConversion_MakesAnOverloadApplicable()
    {
        var invocation = SingleInvocation("var chosen = Sink.Take(src);", ConversionTypes, "var src = new Source();");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Probe.Wanted", parameter.Type.FullyQualifiedName);
    }

    /// <summary>
    /// The same fixture with the operator declared explicit. An explicit conversion does not happen on
    /// its own, so no candidate is applicable and the call is refused rather than silently taking the
    /// one whose arity fits.
    /// </summary>
    [Fact]
    public void AUserDefinedExplicitConversion_DoesNotMakeAnOverloadApplicable()
    {
        var diagnostics = BuildRun(
            "var chosen = Sink.Take(src);",
            ConversionTypes.Replace("implicit operator", "explicit operator", StringComparison.Ordinal),
            "var src = new Source();");

        var reported = Assert.Single(Errors(diagnostics, UnresolvableId));
        Assert.Contains("Take", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A DELIBERATE DIVERGENCE FROM CLASSIC, which compiles this call and resolves it to <c>Take(A)</c>
    /// on the strength of A declaring an operator to B. Mist refuses it: an <c>implicit operator</c>
    /// asserts no containment, so it cannot say which parameter is the narrower home, and a call whose
    /// meaning would turn on an operator declared elsewhere between two parameter types is one the
    /// caller should disambiguate. See ConversionRelation.IsWithin.
    /// </summary>
    [Fact]
    public void TwoCandidatesSeparatedOnlyByAUserDefinedConversion_AreAmbiguous()
    {
        var diagnostics = BuildRun("var chosen = Pick.Take(s2);", RankingTypes, "var s2 = new S2();");

        var reported = Assert.Single(Errors(diagnostics, AmbiguousId));
        Assert.Contains("(A)", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(B)", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A receiver's type argument can close a parameter written as a type parameter onto a sibling's
    /// written type, leaving two candidates no argument separates. The written one wins -- see
    /// <c>OverloadResolution.BeatsOnSignature</c> for why refusing them left neither callable.
    /// </summary>
    [Theory]
    [InlineData("var chosen = keyed.Get(3);", "int")]
    [InlineData("var chosen = inherited.Get(3);", "int")]
    [InlineData("var chosen = Util<int>.Get(3);", "int")]
    [InlineData("var chosen = nested.Take(wrapped);", "Wrap<int>")]
    [InlineData("var chosen = nested.Take(items);", "int[]")]
    [InlineData("var chosen = nested.Take(pair);", "(int, int)")]
    [InlineData("var chosen = nested.Take(fn);", "func<int, int>")]
    [InlineData("var chosen = lifted.Take(3);", "int | void")]
    [InlineData("var chosen = lifted.Take(maybe);", "int | void")]
    public void AWrittenParameter_BeatsOneAReceiversTypeArgumentMadeIdentical(
        string statement, string expectedParameterType)
    {
        var invocation = SingleInvocation(statement, ClosedTypes, ClosedPreamble);

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal(expectedParameterType, TypeDisplay.Mist(parameter.Type));
    }

    [Fact]
    public void ASignatureWrittenInOnePosition_BeatsOneGenericInBoth()
    {
        var invocation = SingleInvocation("var chosen = partly.Take(1, 2);", ClosedTypes, ClosedPreamble);

        Assert.NotNull(invocation.Method);
        Assert.Equal("K", TypeDisplay.Mist(invocation.Method.ParametersList[0].Type));
        Assert.Equal("int", TypeDisplay.Mist(invocation.Method.ParametersList[1].Type));
    }

    [Fact]
    public void AConstructorsWrittenParameter_BeatsTheOneItsTypeArgumentMadeIdentical()
    {
        var creation = SingleNew("var made = new Box<int>(3);", ClosedTypes);

        Assert.NotNull(creation.Constructor);
        var parameter = Assert.Single(creation.Constructor.ParametersList);
        Assert.Equal("int", TypeDisplay.Mist(parameter.Type));
    }

    [Fact]
    public void AnIndexersWrittenParameter_BeatsTheOneItsTypeArgumentMadeIdentical()
    {
        var read = InitializerOf<IndexExpression>("var read = keyed[3];", ClosedTypes, ClosedPreamble);
        var definition = (ClassType)((ConstructedType)read.Receiver.Type).Definition;

        Assert.Same(definition.IndexersList[1], read.Indexer);
        Assert.Equal("Standard.Int32", read.ParameterType?.FullyQualifiedName);
    }

    [Theory]
    [InlineData("var chosen = own.Get(3);", "int")]
    [InlineData("var chosen = both.Get(1, 2);", "int")]
    public void AMethodsOwnTypeParameter_LosesToAWrittenType(string statement, string expectedLastParameter)
    {
        var invocation = SingleInvocation(statement, ClosedTypes, ClosedPreamble);

        Assert.NotNull(invocation.Method);
        Assert.Equal(expectedLastParameter, TypeDisplay.Mist(invocation.Method.ParametersList[^1].Type));
    }

    [Fact]
    public void ACandidateWithNoTypeParametersOfItsOwn_WinsBeforeParametersAreCompared()
    {
        var invocation = SingleInvocation("var chosen = ordered.Get(1, 2);", ClosedTypes, ClosedPreamble);

        Assert.NotNull(invocation.Method);
        Assert.Empty(invocation.Method.TypeParametersList);
        Assert.Equal("K", TypeDisplay.Mist(invocation.Method.ParametersList[0].Type));
    }

    [Theory]
    [InlineData("var chosen = pairs.Take(3);")]
    [InlineData("var chosen = crossed.Take(1, 2);")]
    public void CandidatesNoWrittenPositionSeparates_StayAmbiguous(string statement)
    {
        _ = Assert.Single(Errors(BuildRun(statement, ClosedTypes, ClosedPreamble), AmbiguousId));
    }

    [Fact]
    public void ATieInOnePosition_IsNotSettledByGenericityInAnother()
    {
        var diagnostics = BuildRun("var chosen = signed.Take(small, 1);", ClosedTypes, ClosedPreamble);

        var reported = Assert.Single(Errors(diagnostics, AmbiguousId));
        Assert.Contains("(uint, T)", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(short, int)", reported.Message, StringComparison.Ordinal);
    }

    // ── Harness ──────────────────────────────────────────────────

    private const string ClosedPreamble = """
        var keyed = new Keyed<int>();
        var inherited = new IntKeyed();
        var nested = new Nested<int>();
        var partly = new Partly<int>();
        var pairs = new Pair<int, int>();
        var crossed = new Crossed<int>();
        var lifted = new Lifted<int>();
        var own = new OwnAndWritten();
        var both = new BothGeneric();
        var signed = new Signed();
        var ordered = new Ordered<int>();
        var wrapped = new Wrap<int>();
        int[] items = [1, 2];
        (int, int) pair = (1, 2);
        func<int, int> fn = Plain.Once;
        byte small = 2;
        int? maybe = void;
        """;

    private const string ClosedTypes = """
        public class Wrap<T>
        {
            public Wrap() { }
        }

        public class Keyed<K>
        {
            public string Get(K key) { return "K"; }

            public string Get(int i) { return "int"; }

            public string this[K key] { get { return "K"; } }

            public string this[int i] { get { return "int"; } }
        }

        public class IntKeyed : Keyed<int>
        {
            public IntKeyed() { }
        }

        public class Util<K>
        {
            public static string Get(K key) { return "K"; }

            public static string Get(int i) { return "int"; }
        }

        public class Box<T>
        {
            public Box(T item) { }

            public Box(int item) { }
        }

        public class Nested<K>
        {
            public string Take(Wrap<K> wrapped) { return "Wrap<K>"; }

            public string Take(Wrap<int> wrapped) { return "Wrap<int>"; }

            public string Take(K[] items) { return "K[]"; }

            public string Take(int[] items) { return "int[]"; }

            public string Take((K, int) pair) { return "(K, int)"; }

            public string Take((int, int) pair) { return "(int, int)"; }

            public string Take(func<K, int> f) { return "func<K, int>"; }

            public string Take(func<int, int> f) { return "func<int, int>"; }
        }

        public class Partly<K>
        {
            public string Take(K first, K second) { return "K,K"; }

            public string Take(K first, int second) { return "K,int"; }
        }

        public class Pair<A, B>
        {
            public string Take(A a) { return "A"; }

            public string Take(B b) { return "B"; }
        }

        public class Crossed<K>
        {
            public string Take(K first, int second) { return "K,int"; }

            public string Take(int first, K second) { return "int,K"; }
        }

        public class Lifted<K>
        {
            public string Take(K? key) { return "K?"; }

            public string Take(int? i) { return "int?"; }
        }

        public class OwnAndWritten
        {
            public string Get<T>(T value) { return "T"; }

            public string Get(int i) { return "int"; }
        }

        public class BothGeneric
        {
            public string Get<U>(U first, U second) { return "U,U"; }

            public string Get<T>(T first, int second) { return "T,int"; }
        }

        public class Ordered<K>
        {
            public string Get<T>(int first, T second) { return "int,T"; }

            public string Get(K first, int second) { return "K,int"; }
        }

        public class Signed
        {
            public string Take<T>(uint first, T second) { return "uint,T"; }

            public string Take(short first, int second) { return "short,int"; }
        }
        """;

    /// <summary>
    /// S2 reaches both A and B, and is neither. A reaches B, so a relation that ranked on user-defined
    /// conversions would make A the narrower home and pick it.
    /// </summary>
    private const string RankingTypes = """
        public class B
        {
            public B() { }
        }

        public class A
        {
            public A() { }

            public static implicit operator B(A a) { return new B(); }
        }

        public class S2
        {
            public S2() { }

            public static implicit operator A(S2 s) { return new A(); }

            public static implicit operator B(S2 s) { return new B(); }
        }

        public class Pick
        {
            public static int Take(A a) { return 1; }

            public static int Take(B b) { return 2; }
        }
        """;

    private const string ConstructedPreamble = """
        var box = new Box<int>(4);
        var intBox = new IntBox();
        """;

    private const string ConstructedTypes = """
        public abstract IHold<T>
        {
            T Get();
        }

        public class Box<T> : IHold<T>
        {
            private T item;

            public Box(T item) { this.item = item; }

            public T Get() { return item; }
        }

        public class IntBox : Box<int>
        {
            public IntBox() : base(4) { }
        }

        public class Held
        {
            public static int Take(string s) { return 1; }

            public static int Take(IHold<int> held) { return 2; }
        }

        public class HeldOrAnything
        {
            public static int Take(unknown value) { return 1; }

            public static int Take(IHold<int> held) { return 2; }
        }

        public class HeldText
        {
            public static int Take(bool b) { return 1; }

            public static int Take(IHold<string> held) { return 2; }
        }
        """;

    private const string SignTypes = """
        public class Signs
        {
            public static int Take(uint u) { return 1; }

            public static int Take(short s) { return 2; }
        }

        public class WideSigns
        {
            public static int Take(ulong u) { return 1; }

            public static int Take(long l) { return 2; }
        }

        public class ConstantSigns
        {
            public static int Take(byte b) { return 1; }

            public static int Take(sbyte s) { return 2; }
        }
        """;

    private const string ConstantTypes = """
        public class Fit
        {
            public static int Take(string s) { return 1; }

            public static int Take(long l) { return 2; }

            public static int Take(byte b) { return 3; }
        }

        public class Unfit
        {
            public static int Take(string s) { return 1; }

            public static int Take(byte b) { return 2; }
        }

        public class OptionalFit
        {
            public static int Take(string s) { return 1; }

            public static int Take(byte? b) { return 2; }
        }

        public class WideOptionalFit
        {
            public static int Take(long? l) { return 1; }

            public static int Take(byte? b) { return 2; }
        }
        """;

    private const string GroupTypes = """
        public class Combiner
        {
            public static int Add(int a, int b) { return a + b; }

            public static int Join(int a, string b) { return a; }

            public static int Apply(bool flag) { return 1; }

            public static int Apply(func<int, int, int> combine) { return combine(2, 3); }
        }
        """;

    private const string ConversionTypes = """
        public class Wanted
        {
            public Wanted() { }
        }

        public class Source
        {
            public Source() { }

            public static implicit operator Wanted(Source s) { return new Wanted(); }
        }

        public class Sink
        {
            public static int Take(Wanted w) { return 1; }

            public static int Take(bool b) { return 2; }
        }
        """;

    private InvocationExpression SingleInvocation(
        string statement, string extraTypes = "", string extraPreamble = "")
    {
        return InitializerOf<InvocationExpression>(statement, extraTypes, extraPreamble);
    }

    private NewExpression SingleNew(string statement, string extraTypes = "")
    {
        return InitializerOf<NewExpression>(statement, extraTypes);
    }
}