// Tier 3 (Component): a newtype over a Mist class or struct is its source's declaration bound again under
// the newtype's name, with no nominal relationship to the source (newtypes spec Requirements 2.1-2.3 and
// 3.2-3.6), asserted on the model and on the analysis findings a clone adds.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class NewtypeCloneTests : ProgramModelProbe
{
    public NewtypeCloneTests()
        : base("MistNewtypeClone_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract IStriped
        {
            int Stripes();
        }

        public abstract INamed
        {
            string Label();
        }

        public class Cat
        {
            public string Name;

            public Cat(string name)
            {
                this.Name = name;
            }
        }

        public class Tiger : Cat, IStriped
        {
            public Stripe Pattern;

            public Tiger(string name) : base(name)
            {
                this.Pattern = new Stripe();
            }

            public int Stripes() { return 100; }

            public string Title() { return this.Name; }

            public Tiger Twin() { return new Tiger(this.Name); }

            public class Stripe
            {
                public Stripe() { }
            }
        }

        with Tiger : INamed
        {
            Title as Label;
        }

        public struct Point
        {
            public int X;

            public Point(int x)
            {
                this.X = x;
            }

            public Point Moved() { return new Point(this.X + 1); }
        }

        type Jaguar = new Tiger;
        type Position = new Point;
        """;

    private const string Generics = """
        public class Box<T>
        {
            private T item;

            public Box(T item) { this.item = item; }

            public T Get() { return item; }

            public Box<T> Again() { return new Box<T>(item); }

            public Box<string> Named() { return new Box<string>("named"); }
        }

        public struct Pair<A, B>
        {
            public A First;
            public B Second;

            public Pair(A first, B second)
            {
                this.First = first;
                this.Second = second;
            }

            public Pair<B, A> Flipped() { return new Pair<B, A>(Second, First); }
        }

        public abstract IRank
        {
            int Rank();
        }

        public class Sorted<T> where T : IRank
        {
            public T Low;

            public Sorted(T low) { this.Low = low; }

            public int Floor() { return Low.Rank(); }
        }

        public class Bundle<params TArgs>
        {
            public Bundle() { }

            public Bundle<TArgs...> Again() { return new Bundle<TArgs...>(); }
        }

        public class Outer<T>
        {
            public class Inner
            {
                public T Held;

                public Inner(T held) { this.Held = held; }
            }
        }

        type Coord = Pair<int, string>;
        type IntBox = new Box<int>;
        type Boxes<U> = new Box<U>;
        type Swap<A, B> = new Pair<B, A>;
        type Ranked<V> = new Sorted<V>;
        type Spot = new Coord;
        type BoxAgain = new IntBox;
        type BoxesAgain<W> = new Boxes<W>;
        type SwapBack<X, Y> = new Swap<Y, X>;
        type SpotAgain = new Spot;
        type Two = new Bundle<int, string>;
        type Holding = new Outer<string>.Inner;
        type Parenthesized = new (Point);
        type Id<T> = new Point;
        """;

    private const string Frozen = """
        public sealed class Gauge
        {
            public int Level;

            public Gauge(int level)
            {
                this.Level = level;
            }

            public int Doubled() { return this.Level * 2; }
        }

        internal readonly type FrozenGauge = new Gauge;
        type FrozenAgain = new FrozenGauge;
        type LooseGauge = new Gauge;
        """;

    protected override string Preamble { get; } = """
        var jaguar = new Jaguar("jag");
        var position = new Position(1);
        """;

    [Fact]
    public void ANewtypeOverAClass_IsAClassWithItsSourcesBases()
    {
        var jaguar = Assert.IsType<ClassType>(TypeNamed("Probe.Jaguar"));

        Assert.Equal("Probe.Tiger", jaguar.CloneSource?.FullyQualifiedName);
        Assert.Equal("Probe.Cat", jaguar.BaseType?.FullyQualifiedName);
        Assert.Contains(jaguar.BaseAbstractTypes, abstractBase => abstractBase.FullyQualifiedName == "Probe.IStriped");
    }

    [Fact]
    public void ANewtypeOverAStruct_IsAStruct()
    {
        var position = Assert.IsType<StructType>(TypeNamed("Probe.Position"));

        Assert.Equal("Probe.Point", position.CloneSource?.FullyQualifiedName);
    }

    [Fact]
    public void TheClone_IsWithinItsSourcesBasesAndNeverTheSource()
    {
        var model = BuildModel("int ok = 1;", "", "", out _);
        var jaguar = model.TypesByFQN["Probe.Jaguar"];
        var tiger = model.TypesByFQN["Probe.Tiger"];

        Assert.True(ConversionRelation.IsWithin(jaguar, model.TypesByFQN["Probe.Cat"]));
        Assert.True(ConversionRelation.IsWithin(jaguar, model.TypesByFQN["Probe.IStriped"]));
        Assert.False(ConversionRelation.IsWithin(jaguar, tiger));
        Assert.False(ConversionRelation.IsWithin(tiger, jaguar));
    }

    [Theory]
    [InlineData("var twin = jaguar.Twin();", "Probe.Jaguar")]
    [InlineData("var moved = position.Moved();", "Probe.Position")]
    public void AMemberAnsweringTheSource_AnswersTheClone(string statement, string expected)
    {
        Assert.Equal(expected, SingleLocal(statement).Type.FullyQualifiedName);
    }

    [Fact]
    public void TheClonesBody_ConstructsTheClone()
    {
        var jaguar = Assert.IsType<ClassType>(TypeNamed("Probe.Jaguar"));
        var twin = jaguar.MethodsList.Single(method => method.Name == "Twin");

        var returned = Assert.IsType<ReturnStatement>(Assert.Single(twin.Body!.Statements));
        Assert.Equal("Probe.Jaguar", Assert.IsType<NewExpression>(returned.Value).Type.FullyQualifiedName);
    }

    [Fact]
    public void ATypeNestedInTheSource_IsNotCloned()
    {
        var jaguar = Assert.IsType<ClassType>(TypeNamed("Probe.Jaguar"));

        Assert.Empty(jaguar.NestedTypes);
        Assert.Equal("Probe.Tiger.Stripe", jaguar.FieldsList.Single(field => field.Name == "Pattern").Type.FullyQualifiedName);
    }

    [Fact]
    public void AConformanceDeclaredOutsideTheSource_DoesNotReachTheClone()
    {
        var model = BuildModel("int ok = 1;", "", "", out _);
        var jaguar = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Jaguar"]);

        Assert.NotEmpty(Assert.IsType<ClassType>(model.TypesByFQN["Probe.Tiger"]).ConformancesList);
        Assert.Empty(jaguar.ConformancesList);
        Assert.False(ConversionRelation.IsWithin(jaguar, model.TypesByFQN["Probe.INamed"]));
    }

    [Fact]
    public void ASourceMemberThatFailsOnlyForTheClone_IsReportedAtTheNewtypeAndTheSourcesOwnMistakeOnce()
    {
        var findings = Analyse("""
            public class Zoo
            {
                public static void Admit(Lion lion) { }
            }

            public class Lion
            {
                public Lion() { }

                public void Enroll() { Zoo.Admit(this); }

                public void Broken() { int count = "many"; }
            }

            type Puma = new Lion;
            """);

        var cloneFailure = Assert.Single(findings, finding => finding.Number.Value == "MIST0418");
        Assert.Contains("'Puma' cannot clone 'Lion', whose member 'Enroll'", cloneFailure.Message, StringComparison.Ordinal);
        _ = Assert.Single(findings, finding => finding.Number.Value == "MIST0157");
    }

    [Fact]
    public void ANewtypeOverAConstruction_IsANonGenericCloneWithItsArgumentsInItsMembers()
    {
        var intBox = Assert.IsType<ClassType>(GenericNamed("Probe.IntBox"));

        Assert.Empty(intBox.TypeParametersList);
        Assert.Equal("Probe.Box<Int32>", intBox.CloneSourceType?.FullyQualifiedName);
        Assert.Equal("Standard.Int32", intBox.MethodsList.Single(method => method.Name == "Get").ReturnType.FullyQualifiedName);
    }

    [Theory]
    [InlineData("Again", "Probe.IntBox")]
    [InlineData("Named", "Probe.Box<String>")]
    public void TheSourceNamedInTheClonesBody_IsTheCloneOnlyWithTheClonesArguments(string method, string expected)
    {
        var intBox = Assert.IsType<ClassType>(GenericNamed("Probe.IntBox"));

        Assert.Equal(expected, intBox.MethodsList.Single(member => member.Name == method).ReturnType.FullyQualifiedName);
    }

    [Theory]
    [InlineData("var named = new Boxes<string>(\"s\").Named();", "Probe.Boxes<String>")]
    [InlineData("var flipped = new Swap<int, string>(\"s\", 1).Flipped();", "Probe.Swap<String, Int32>")]
    public void AGenericNewtype_NamesEachInstanceOfItsSourceAsTheInstanceOfItselfThatClonesIt(string statement, string expected)
    {
        Assert.Equal(expected, SingleLocal(statement, Generics).Type.FullyQualifiedName);
    }

    [Fact]
    public void ASourcesConstraint_ConstrainsTheCloneParameterItIsBoundTo()
    {
        var ranked = Assert.IsType<ClassType>(GenericNamed("Probe.Ranked<>"));
        var constraint = Assert.IsType<TypeConstraint>(Assert.Single(Assert.Single(ranked.TypeParametersList).ConstraintsList));

        Assert.Equal("Probe.IRank", constraint.ConstraintType.FullyQualifiedName);
    }

    [Theory]
    [InlineData("Probe.Spot", "Probe.Pair<Int32, String>")]
    [InlineData("Probe.BoxAgain", "Probe.IntBox")]
    [InlineData("Probe.Two", "Probe.Bundle<[Int32, String]>")]
    [InlineData("Probe.Holding", "Probe.Outer<String>.Inner")]
    [InlineData("Probe.Parenthesized", "Probe.Point")]
    public void ASourceReachedThroughAnAliasANewtypeAPackAnEnclosingInstanceOrParentheses_IsCloned(string newtype, string source)
    {
        Assert.Equal(source, Assert.IsAssignableFrom<ConcreteType>(GenericNamed(newtype)).CloneSourceType?.FullyQualifiedName);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Probe.BoxAgain", "Probe.IntBox", "Probe.Box<>")]
    [InlineData("Probe.BoxesAgain<>", "Probe.Boxes<W>", "Probe.Box<>")]
    [InlineData("Probe.SwapBack<,>", "Probe.Swap<Y, X>", "Probe.Pair<,>")]
    public void ACloneOfAClone_NamesItsImmediateSourceAndIsBoundFromTheRootDeclaration(string newtype, string source, string root)
    {
        var clone = Assert.IsAssignableFrom<ConcreteType>(GenericNamed(newtype));

        Assert.Equal(source, clone.CloneSourceType?.FullyQualifiedName);
        Assert.Equal(root, clone.CloneSource?.FullyQualifiedName);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("var named = new BoxesAgain<string>(\"s\").Named();", "Probe.BoxesAgain<String>")]
    [InlineData("var flipped = new SwapBack<int, string>(1, \"s\").Flipped();", "Probe.SwapBack<String, Int32>")]
    [InlineData("var again = new BoxAgain(1).Again();", "Probe.BoxAgain")]
    public void ACloneOfACloneRenamesTheRootDeclarationForItselfThroughEveryArgument(string statement, string expected)
    {
        Assert.Equal(expected, SingleLocal(statement, Generics).Type.FullyQualifiedName);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Probe.SpotAgain", "Probe.Spot")]
    [InlineData("Probe.SwapBack<,>", "Probe.Swap<Y, X>")]
    public void ACloneOfACopyableClone_ConvertsWithItsImmediateSourceOnly(string newtype, string source)
    {
        var clone = Assert.IsAssignableFrom<ConcreteType>(GenericNamed(newtype));

        Assert.Equal(2, clone.CastOperatorsList.Count(castOperator => castOperator.CopiesClone));
        Assert.All(
            clone.CastOperatorsList.Where(castOperator => castOperator.CopiesClone),
            castOperator => Assert.Contains(source, new[] { castOperator.TargetType.FullyQualifiedName, castOperator.SourceParameter.Type.FullyQualifiedName }));
    }

    [Theory]
    [InlineData("Probe.Two", "Again", "Probe.Two")]
    [InlineData("Probe.Id<>", "Moved", "Probe.Id<T>")]
    public void TheSourceNamedInsideAPackOrAGenericCloneOfAPlainSource_IsTheClone(string newtype, string method, string expected)
    {
        var clone = Assert.IsAssignableFrom<ConcreteType>(GenericNamed(newtype));

        Assert.Equal(expected, clone.MethodsList.Single(member => member.Name == method).ReturnType.FullyQualifiedName);
    }

    [Fact]
    public void AnEnclosingInstancesArgument_ReachesTheClonesMembers()
    {
        var holding = Assert.IsType<ClassType>(GenericNamed("Probe.Holding"));

        Assert.Equal("Standard.String", holding.FieldsList.Single(field => field.Name == "Held").Type.FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AReadonlyNewtype_ClonesItsSourceAsAReadonlyTypeWithTheNewtypesAccessibilityAndTheSourcesOtherModifiers()
    {
        var frozen = Assert.IsType<ClassType>(FrozenNamed("Probe.FrozenGauge"));

        Assert.True(frozen.Modifiers.IsReadonly);
        Assert.True(frozen.Modifiers.IsSealed);
        Assert.Equal(Accessibility.Internal, frozen.Modifiers.Accessibility);
        Assert.True(Assert.Single(frozen.FieldsList).Modifiers.IsReadonly);
        Assert.True(frozen.MethodsList.Single(method => method.Name == "Doubled").Modifiers.IsReadonly);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("Probe.FrozenAgain", true)]
    [InlineData("Probe.LooseGauge", false)]
    [InlineData("Probe.Gauge", false)]
    public void ReadonlyReachesTheClonesOfAReadonlyNewtypeAndNeitherItsSourceNorAPlainNewtype(string type, bool expected)
    {
        var built = Assert.IsType<ClassType>(FrozenNamed(type));

        Assert.Equal(expected, built.Modifiers.IsReadonly);
        Assert.Equal(expected, Assert.Single(built.FieldsList).Modifiers.IsReadonly);
    }

    private MistType GenericNamed(string fullyQualifiedName)
    {
        var model = BuildModel("int ok = 1;", Generics, "", out var diagnostics);
        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Message).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        return Assert.IsAssignableFrom<MistType>(model.TypesByFQN[fullyQualifiedName]);
    }

    private MistType FrozenNamed(string fullyQualifiedName)
    {
        var model = BuildModel("int ok = 1;", Frozen, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return Assert.IsAssignableFrom<MistType>(model.TypesByFQN[fullyQualifiedName]);
    }

    private MistType TypeNamed(string fullyQualifiedName)
    {
        var model = BuildModel("int ok = 1;", "", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return Assert.IsAssignableFrom<MistType>(model.TypesByFQN[fullyQualifiedName]);
    }

    private List<Diagnostic> Analyse(string extraTypes)
    {
        var model = BuildModel("int ok = 1;", extraTypes, "", out var buildDiagnostics);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. buildDiagnostics, .. findings.ToImmutableArray()];
    }
}