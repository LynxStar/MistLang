// Tier 3 (Component): the conversions between a newtype and a copyable source it clones (newtypes spec
// Requirements 2.4-2.7): out implicitly and losslessly, in only by a cast calling the newtype's conversion,
// and nothing either way for a mutable class that defines no copy.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class NewtypeConversionDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_CAST_HAS_NO_CONVERSION", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY", "MIST_PA_NO_IMPLICIT_CONVERSION", "MIST_PA_CAST_REQUIRED",
        "MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD",
        "MIST_PA_CAST_ARGUMENT_MISMATCH", "MIST_PA_AMBIGUOUS_CAST", "MIST_PM_UNRESOLVABLE_OVERLOAD",
        "MIST_PM_AMBIGUOUS_OVERLOAD", "MIST_PM_UNRESOLVED_MEMBER",
        "MIST_PA_PATTERN_NEVER_MATCHES", "MIST_PM_AS_NO_RELATIONSHIP",
    ];

    public NewtypeConversionDecisionTests()
        : base("MistNewtypeConversion_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Point
        {
            private int hidden;
            public int X;

            public Point(int x)
            {
                this.X = x;
                this.hidden = x;
            }
        }

        public abstract IPriced
        {
            int Price();
        }

        public readonly class Money
        {
            public int Amount { get; }

            public Money(int amount)
            {
                this.Amount = amount;
            }

            public int ReadPrice() { return Amount; }
        }

        with Money : IPriced
        {
            ReadPrice as Price;
        }

        public class Entity
        {
            public int Id;

            public Entity(int id)
            {
                this.Id = id;
            }
        }

        public class Reading
        {
            public Reading() { }

            public static implicit operator Point(Reading r) { return new Point(1); }
        }

        public class Gauge
        {
            public Gauge() { }

            public static implicit operator Position(Gauge g) { return (Position)new Point(2); }
        }

        public class Sink
        {
            public static int Take(Point point) { return 1; }

            public static int Take(unknown anything) { return 2; }
        }

        public struct Pair<A, B>
        {
            private A first;
            public B Second;

            public Pair(A first, B second)
            {
                this.first = first;
                this.Second = second;
            }
        }

        public readonly class Held<T>
        {
            public T Item { get; }

            public Held(T item)
            {
                this.Item = item;
            }
        }

        public class Crate<T>
        {
            public T Item;

            public Crate(T item)
            {
                this.Item = item;
            }
        }

        public class Wrapper
        {
            public Wrapper() { }

            public static implicit operator Wrapper(Held<int> held) { return new Wrapper(); }
        }

        extension(Point self)
        {
            public int Doubled() { return self.X + self.X; }
        }

        extension(Held<int> self)
        {
            public int Doubled() { return self.Item + self.Item; }
        }

        public readonly class Node<T>
        {
            public T Item { get; }
            public Node<T>? Next { get; }

            public Node(T item)
            {
                this.Item = item;
            }
        }

        public struct Tagged<T, params TRest>
        {
            public T First;

            public Tagged(T first)
            {
                this.First = first;
            }
        }

        type Position = new Point;
        type Coord = new Point;
        type Chain<T> = new Node<T>;
        type IntChain = new Node<int>;
        type Packed<T, params TRest> = new Tagged<T, TRest...>;
        type Kept<K> = new Held<K>;
        type Again<A> = new Kept<A>;
        type Swapped<X, Y> = new Pair<Y, X>;
        type Crated<C> = new Crate<C>;
        type IntPair = new Pair<int, string>;
        type Cash = new Money;
        type Actor = new Entity;
        """;

    protected override string Preamble { get; } = """
        var point = new Point(3);
        var position = new Position(4);
        var money = new Money(5);
        var cash = new Cash(6);
        var entity = new Entity(7);
        var actor = new Actor(8);
        var reading = new Reading();
        var pair = new Pair<int, string>(1, "a");
        var intPair = new IntPair(2, "b");
        var kept = new Kept<int>(9);
        var again = new Again<int>(10);
        var swapped = new Swapped<int, string>("c", 3);
        var keptInATuple = (kept, 1);
        Kept<int> | Entity keptOrEntity = kept;
        Position? maybePosition = position;
        unknown boxedPosition = position;
        """;

    [Theory]
    [InlineData("Point leavesAStructClone = position;")]
    [InlineData("Money leavesAReadonlyClassClone = cash;")]
    [InlineData("Position entersByCast = (Position)point;")]
    [InlineData("Cash entersAReadonlyClassByCast = (Cash)money;")]
    [InlineData("Coord crossesThroughTheSource = (Coord)position;")]
    [InlineData("Position entersThroughADeclaredImplicit = (Position)reading;")]
    [InlineData("unknown widensThroughTheSource = position;")]
    [InlineData("Point leavesFreeAfterADeclaredImplicit = new Gauge();")]
    [InlineData("Pair<int, string> leavesAConstructedSource = intPair;")]
    [InlineData("IntPair entersAConstructedSourceByCast = (IntPair)pair;")]
    [InlineData("Held<int> leavesAGenericClone = kept;")]
    [InlineData("Kept<int> entersAGenericCloneByCast = (Kept<int>)new Held<int>(4);")]
    [InlineData("Kept<string> entersAtAnotherInstance = (Kept<string>)new Held<string>(\"s\");")]
    [InlineData("Pair<string, int> leavesAPermutedStruct = swapped;")]
    [InlineData("Swapped<int, string> entersAPermutedStructByCast = (Swapped<int, string>)new Pair<string, int>(\"d\", 4);")]
    [InlineData("Wrapper leavesAGenericCloneFreeBeforeADeclaredImplicit = kept;")]
    [InlineData("(Held<int>, int) leavesAGenericCloneElementwise = keptInATuple;")]
    public void AConversionOfACopyableSource_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("var aStructCloneTested = position is Point;", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("var aReadonlyCloneTested = cash is Money;", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("var aSourceTestedAgainstItsClone = money is Cash;", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("var aCloneTestedUnderNegation = position is not Point;", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("var aCloneTestedInAnArm = position switch { Point p => 1, _ => 0 };", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("var anOptionalCloneTested = maybePosition is Point;", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("var aGenericCloneTested = kept is Held<int>;", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("var aCloneTestedPastItsSource = position as Point;", "MIST_PM_AS_NO_RELATIONSHIP")]
    [InlineData("var aSourceTestedAgainstItsCloneByAs = point as Position;", "MIST_PM_AS_NO_RELATIONSHIP")]
    public void ATypeTestBetweenACloneAndItsSource_IsRefused(string statement, string expected)
    {
        Assert.Equal(expected, Assert.Single(Analyse(statement)).Id.Value);
    }

    [Theory]
    [InlineData("var atRuntimeTheCloneAnswersItself = boxedPosition is Position;")]
    [InlineData("var atRuntimeTheSourceAnswersFalse = boxedPosition is Point;")]
    [InlineData("var aWideningIsStillContainment = point.X is long;")]
    public void ATestNothingButACopyWouldDecide_IsStillAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("Position entersWithoutACast = point;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Coord crossesWithoutACast = position;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Entity leavesAMutableClass = actor;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("Actor entersAMutableClassByCast = (Actor)entity;", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("Pair<string, int> leavesOnlyForItsOwnArguments = intPair;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("Held<string> leavesOnlyForItsOwnInstance = kept;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("Pair<int, string> leavesOnlyAsItsSourcePermutes = swapped;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("Kept<int> entersAGenericCloneWithoutACast = new Held<int>(4);", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Kept<int> entersFromAnotherInstance = (Kept<int>)new Held<string>(\"s\");", "MIST_PA_CAST_ARGUMENT_MISMATCH")]
    [InlineData("Crated<int> entersAMutableGenericClassByCast = (Crated<int>)new Crate<int>(1);", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("Crate<int> leavesAMutableGenericClassByCast = (Crate<int>)new Crated<int>(1);", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("Held<int> | Entity aUnionLeavesOnlyAsItsConstituentsAre = keptOrEntity;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("Chain<int> aSelfTypedSlotCopiesNothing = (Chain<int>)new Node<int>(1);", "MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD")]
    [InlineData("IntChain aSelfTypedSlotCopiesNothingWhenClosed = (IntChain)new Node<int>(1);", "MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD")]
    [InlineData("Node<int> aSelfTypedSlotLeavesNothingEither = new Chain<int>(1);", "MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD")]
    [InlineData("Packed<int, string> aPackConvertsNothing = (Packed<int, string>)new Tagged<int, string>(1);", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    public void AConversionTheSourceDoesNotAdmit_IsRefused(string statement, string expected)
    {
        Assert.Equal(expected, Assert.Single(Analyse(statement)).Id.Value);
    }

    [Theory]
    [InlineData("Chain<int> intoTheNewtype = (Chain<int>)new Node<int>(1);", "'Node<int> | void' in the source is a 'Chain<int> | void'")]
    [InlineData("IntChain intoAClosedNewtype = (IntChain)new Node<int>(1);", "'Node<int> | void' in the source is a 'IntChain | void'")]
    [InlineData("Node<int> outOfTheNewtype = new Chain<int>(1);", "'Node<int> | void' in the source is a 'Chain<int> | void'")]
    public void ACopyThatDoesNotHold_NamesTheFieldAndSaysWhatWouldSayHowToCopyIt(string statement, string slots)
    {
        var refusal = Assert.Single(Analyse(statement));

        Assert.Contains("does not hold at 'Next'", refusal.Message);
        Assert.Contains(slots, refusal.Message);
        Assert.Contains("implements no 'IClonable<Node<int>>'", refusal.Message);
    }

    [Fact]
    public void ACastToTheNewtype_CallsItsConversionFromTheSource()
    {
        var cast = InitializerOf<CastExpression>("var entered = (Position)point;");

        Assert.True(cast.Operator?.CopiesClone);
        Assert.Equal("Probe.Point", cast.ParameterType?.FullyQualifiedName);
        Assert.Equal("Probe.Position", cast.Type.FullyQualifiedName);
    }

    [Fact]
    public void ACastToAnInstanceOfAGenericNewtype_CallsItsConversionAtThatInstance()
    {
        var cast = InitializerOf<CastExpression>("var entered = (Swapped<int, string>)new Pair<string, int>(\"d\", 4);");

        Assert.True(cast.Operator?.CopiesClone);
        Assert.Equal("Probe.Pair<String, Int32>", cast.ParameterType?.FullyQualifiedName);
        Assert.Equal("Probe.Swapped<Int32, String>", cast.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("Point | Entity intoACarrier = position;", "Probe.Point")]
    [InlineData("Held<int> | Entity anInstanceIntoACarrier = kept;", "Probe.Held<Int32>")]
    public void ALeaveIntoAUnion_IsRecordedAsTheCopyItCalls(string statement, string result)
    {
        var conversion = InitializerOf<ImplicitConversionExpression>(statement);

        Assert.True(conversion.Operator?.CopiesClone);
        Assert.Equal(result, conversion.Type.FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ACloneOfACloneLeaves_AsItsImmediateSourceFreelyAndForTheRootThroughOneRecordedCopy()
    {
        Assert.IsNotType<ImplicitConversionExpression>(DeclarationIn("Kept<int> toTheSource = again;", "", "").Initializer);

        var conversion = InitializerOf<ImplicitConversionExpression>("Held<int> toTheRoot = again;");

        Assert.True(conversion.Operator?.CopiesClone);
        Assert.Equal("Probe.Kept<Int32>", conversion.Type.FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ACastIntoACloneOfAClone_CallsItsConversionFromItsImmediateSourceAtThatInstance()
    {
        var cast = InitializerOf<CastExpression>("var entered = (Again<int>)kept;");

        Assert.True(cast.Operator?.CopiesClone);
        Assert.Equal("Probe.Kept<Int32>", cast.ParameterType?.FullyQualifiedName);
        Assert.Equal("Probe.Again<Int32>", cast.Type.FullyQualifiedName);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("IPriced intoAnAbstractOnlyTheSourceConformsTo = cash;")]
    [InlineData("IPriced? intoAnOptionalAbstract = cash;")]
    public void ALeaveIntoAnAbstractOnlyTheSourceConformsTo_IsRecordedAsTheCopyItCalls(string statement)
    {
        var conversion = InitializerOf<ImplicitConversionExpression>(statement);

        Assert.True(conversion.Operator?.CopiesClone);
        Assert.Equal("Probe.Money", conversion.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("Point? intoAnOptional = position;")]
    [InlineData("Held<int>? anInstanceIntoAnOptional = kept;")]
    public void ALeaveCSharpCarriesUnaided_StaysContainment(string statement)
    {
        Assert.Empty(Analyse(statement));
        Assert.IsNotType<ImplicitConversionExpression>(DeclarationIn(statement, "", "").Initializer);
    }

    [Theory]
    [InlineData("var onAClosedNewtype = position.Doubled();")]
    [InlineData("var onAnInstanceOfAGenericNewtype = kept.Doubled();")]
    public void AnExtensionDeclaredForTheSource_IsNotTheNewtypes(string statement)
    {
        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", Assert.Single(Analyse(statement)).Id.Value);
    }

    [Fact]
    public void AnExtensionDeclaredForTheSource_StillReachesTheSource()
    {
        var invocation = InitializerOf<InvocationExpression>("var onTheSource = point.Doubled();");

        Assert.Equal("Doubled", invocation.Method?.Name);
    }

    [Fact]
    public void ALeaveIntoAUnion_EmitsAsItsCarrierSoCSharpCannotChooseAgain()
    {
        var writer = new CSharpWriter();

        BodyEmitter.EmitExpression(DeclarationIn("Point | Entity intoACarrier = position;", "", "").Initializer!, writer, new DiagnosticBag());

        Assert.Equal("((Union_Entity_Point)(((Probe.Point)(position))))", writer.ToString());
    }

    [Theory]
    [InlineData("Plain", 2)]
    [InlineData("Open<>", 0)]
    public void ACloneInsideAGenericOverASourceTheNamespaceCannotName_ConvertsNothing(string encloser, int conversions)
    {
        var model = BuildModel(
            statement: "",
            extraTypes: """
                public class Vault
                {
                    protected readonly class Src<T>
                    {
                        public T Item { get; }

                        public Src(T item) { this.Item = item; }
                    }
                }

                public class Plain : Vault
                {
                    private type W<V> = new Vault.Src<V>;
                }

                public class Open<U> : Vault
                {
                    private type W<V> = new Vault.Src<V>;
                }
                """,
            extraPreamble: "",
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var clone = Assert.IsAssignableFrom<ConcreteType>(Assert.Single(model.TypesByFQN[$"Probe.{encloser}"].NestedTypes));

        Assert.Equal(conversions, clone.CastOperatorsList.Count);
    }

    [Fact]
    public void ANewtypeArgument_RanksItsSourceAboveAWiderParameter()
    {
        var invocation = InitializerOf<InvocationExpression>("var chosen = Sink.Take(position);");

        Assert.Equal("Probe.Point", Assert.Single(invocation.Method!.ParametersList).Type.FullyQualifiedName);
    }

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. buildDiagnostics.Concat(findings.ToImmutableArray()).Where(d => Ids.Contains(d.Id.Value))];
    }
}