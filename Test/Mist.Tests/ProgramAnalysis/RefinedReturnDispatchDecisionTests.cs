// Tier 3: closed return promises follow the selected inherited, mapped or replacement member and both accessor directions.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RefinedReturnDispatchDecisionTests
{
    private const string Types = """
        type PositiveInt = int > 0;
        type NonNegativeInt = int >= 0;
        type LargeInt = int >= 2;
        type PositiveReal = double > 0.0;
        type NonNegativeReal = double >= 0.0;
        public class Outer<T> { public class Token { public T Value; } }
        public class Box<T>
        {
            public T Value { get; set; }
            public Box(T value) { Value = value; }
        }
        """;

    [Theory]
    [InlineData("public abstract IFactory<T> { T Make(); } public class Factory : IFactory<PositiveInt> { public PositiveInt Make() { return 1; } }")]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Factory : IFactory<NonNegativeInt, PositiveInt> { public PositiveInt Make(PositiveInt value) { return value; } }")]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Base<T> { public T Make(T value) { return value; } } public class Factory : Base<PositiveInt>, IFactory<NonNegativeInt, PositiveInt> { }")]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Base<T> { public T Make(T value) { return value; } } public class Middle<T> : Base<T> { } public class Factory : Middle<PositiveInt>, IFactory<NonNegativeInt, PositiveInt> { }")]
    [InlineData("public abstract IFactory { PositiveInt Make<U>(PositiveInt value); } public class Factory : IFactory { public PositiveInt Make<V>(PositiveInt value) { return value; } }")]
    [InlineData("public abstract IFactory { PositiveInt Make(); } public abstract ILeft : IFactory { } public abstract IRight : IFactory { } public class Factory : ILeft, IRight { public PositiveInt Make() { return 1; } }")]
    [InlineData("public abstract IFactory { PositiveInt Make(); } public class Base { public PositiveInt Make() { return 1; } } public class Factory : Base, IFactory { }")]
    [InlineData("public abstract IFactory { NonNegativeReal Make(PositiveReal value); } public class Base { public PositiveReal Make(PositiveReal value) { return value; } } public class Factory : Base, IFactory { }")]
    [InlineData("public abstract IFactory { PositiveInt Make(); } public class Factory { public PositiveInt Text() { return 1; } } with Factory : IFactory { Text as Make; }")]
    [InlineData("public abstract IFactory { PositiveInt Make(); int Text(); } public class Factory { public PositiveInt Make() { return 1; } public int Text() { return 0; } } with Factory : IFactory { Text as Text; }")]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Base<T> { public T Text(T value) { return value; } } public class Factory : Base<PositiveInt> { } with Factory : IFactory<NonNegativeInt, PositiveInt> { Text as Make; }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } } public class Factory : IFactory { }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } } public class Factory : IFactory { public PositiveInt Make() { return 2; } }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } } public class Base { public PositiveInt Make() { return 2; } } public class Factory : Base, IFactory { }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } } public class Factory { public PositiveInt Text() { return 2; } } with Factory : IFactory { Text as Make; }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } int Text(); } public class Factory { public int Text() { return 0; } } with Factory : IFactory { Text as Text; }")]
    public void ASelectedMethodPreservingItsClosedReturnContract_IsAdmitted(string declarations)
    {
        Assert.Empty(RefinedReturnContractDecisionTests.Analyse(Types + declarations));
    }

    [Theory]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Factory : IFactory<PositiveInt, NonNegativeInt> { public NonNegativeInt Make(NonNegativeInt value) { return value; } }")]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Base<T> { public T Make(T value) { return value; } } public class Factory : Base<NonNegativeInt>, IFactory<PositiveInt, NonNegativeInt> { }")]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Base<T> { public T Make(T value) { return value; } } public class Middle<T> : Base<T> { } public class Factory : Middle<NonNegativeInt>, IFactory<PositiveInt, NonNegativeInt> { }")]
    [InlineData("public abstract IFactory { PositiveInt Make<U>(); } public class Factory : IFactory { public int Make<V>() { return -1; } }")]
    [InlineData("public abstract IFactory { PositiveReal Make(); } public class Base { public double Make() { return -1.0; } } public class Factory : Base, IFactory { }")]
    [InlineData("public abstract IFactory { PositiveInt Make(); } public class Factory { public int Text() { return -1; } } with Factory : IFactory { Text as Make; }")]
    [InlineData("public abstract IFactory { PositiveInt Make(); int Text(); } public class Factory { public int Make() { return -1; } public int Text() { return 0; } } with Factory : IFactory { Text as Text; }")]
    [InlineData("public abstract IFactory<TOut, TIn> { TOut Make(TIn value); } public class Base<T> { public T Text(T value) { return value; } } public class Factory : Base<NonNegativeInt> { } with Factory : IFactory<PositiveInt, NonNegativeInt> { Text as Make; }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } } public class Factory : IFactory { public int Make() { return -1; } }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } } public class Base { public int Make() { return -1; } } public class Factory : Base, IFactory { }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } } public class Factory { public int Text() { return -1; } } with Factory : IFactory { Text as Make; }")]
    [InlineData("public abstract IFactory { PositiveInt Make() { return 1; } int Text(); } public class Factory { public int Make() { return -1; } public int Text() { return 0; } } with Factory : IFactory { Text as Text; }")]
    public void ASelectedMethodLosingItsClosedReturnContract_IsRefused(string declarations)
    {
        Assert.Equal("MIST0284", Assert.Single(RefinedReturnContractDecisionTests.Analyse(Types + declarations)).Number.Value);
    }

    [Theory]
    [InlineData("public abstract IFactory { NonNegativeInt Value { get; } } public class Factory : IFactory { public PositiveInt Held = 1;public PositiveInt Value { get { return Held; } } }")]
    [InlineData("public abstract IFactory { PositiveInt Value { get; } } public class Factory : IFactory { public int Value { get { return 1; } } }")]
    [InlineData("public abstract IFactory { PositiveInt Value { get; set; } } public class Factory : IFactory { public PositiveInt Held = 1;public PositiveInt Value { get { return Held; } set { Held = value; } } }")]
    [InlineData("public abstract IFactory { PositiveInt Value { set; } } public class Factory : IFactory { public int Value { set { } } }")]
    [InlineData("public abstract IFactory { NonNegativeInt Value { get; } } public class Base<T> { public T Value { get; } } public class Factory : Base<PositiveInt>, IFactory { }")]
    [InlineData("public abstract IFactory<T> { T Value { get; } } public class Factory { public PositiveInt Value { get { return 1; } } } with Factory : IFactory<NonNegativeInt> { Value as Value; }")]
    public void APropertyMeetingBothAccessorDirections_IsAdmitted(string declarations)
    {
        Assert.Empty(RefinedReturnContractDecisionTests.Analyse(Types + declarations));
    }

    [Theory]
    [InlineData("public abstract IFactory { PositiveInt Value { get; } } public class Factory : IFactory { public NonNegativeInt Held = 1;public NonNegativeInt Value { get { return Held; } } }", "MIST0284")]
    [InlineData("public abstract IFactory { PositiveReal Value { get; } } public class Factory : IFactory { public double Value { get { return 1.0; } } }", "MIST0284")]
    [InlineData("public abstract IFactory { PositiveInt Value { get; set; } } public class Factory : IFactory { public NonNegativeInt Held = 1;public NonNegativeInt Value { get { return Held; } set { Held = value; } } }", "MIST0284")]
    [InlineData("public abstract IFactory { NonNegativeInt Value { get; set; } } public class Factory : IFactory { public PositiveInt Held = 1;public PositiveInt Value { get { return Held; } set { Held = value; } } }", "MIST0283")]
    [InlineData("public abstract IFactory { PositiveInt Value { set; } } public class Factory : IFactory { public LargeInt Value { set { } } }", "MIST0283")]
    [InlineData("public abstract IFactory { PositiveInt Value { get; } } public class Base<T> { public T Value { get; } } public class Factory : Base<NonNegativeInt>, IFactory { }", "MIST0284")]
    [InlineData("public abstract IFactory<T> { T Value { get; } } public class Factory { public int Value { get { return -1; } } } with Factory : IFactory<PositiveInt> { Value as Value; }", "MIST0284")]
    public void APropertyLosingEitherAccessorContract_IsRefused(string declarations, string diagnostic)
    {
        Assert.Equal(diagnostic, Assert.Single(RefinedReturnContractDecisionTests.Analyse(Types + declarations)).Number.Value);
    }

    [Theory]
    [InlineData("Box<PositiveInt>", "Box<PositiveInt>")]
    [InlineData("Box<Box<PositiveInt>>", "Box<Box<PositiveInt>>")]
    [InlineData("(PositiveInt, int)", "(PositiveInt, int)")]
    [InlineData("func<PositiveInt>", "func<PositiveInt>")]
    [InlineData("PositiveInt | string", "PositiveInt | string")]
    [InlineData("Outer<PositiveInt>.Token", "Outer<PositiveInt>.Token")]
    public void AnEmbeddedRefinedReturnWithTheSameClosedType_IsAdmitted(string promised, string produced)
    {
        Assert.Empty(Embedded(promised, produced));
    }

    [Theory]
    [InlineData("Box<PositiveInt>", "Box<int>")]
    [InlineData("Box<NonNegativeInt>", "Box<PositiveInt>")]
    [InlineData("Box<Box<PositiveInt>>", "Box<Box<int>>")]
    [InlineData("Outer<PositiveInt>.Token", "Outer<int>.Token")]
    public void AnEmbeddedRefinedReturnWithADifferentClosedType_IsRefused(string promised, string produced)
    {
        Assert.Equal("MIST0284", Assert.Single(Embedded(promised, produced)).Number.Value);
    }

    [Theory]
    [InlineData("(PositiveInt, int)", "(int, int)")]
    [InlineData("(NonNegativeInt, int)", "(PositiveInt, int)")]
    [InlineData("func<PositiveInt>", "func<int>")]
    [InlineData("PositiveInt | string", "int | string")]
    public void AStructurallyDifferentEmbeddedReturn_RemainsRefusedBySignatureMatching(string promised, string produced)
    {
        Assert.Equal("MIST0184", Assert.Single(Embedded(promised, produced)).Number.Value);
    }

    [Fact]
    public void AStrongerSymbolicParameterBound_IsNotDiscarded()
    {
        var findings = RefinedReturnContractDecisionTests.Analyse(Types + """
            public abstract IConsumer { void Take(PositiveInt value); }
            public class Consumer : IConsumer
            {
                public readonly int Bound = 10;
                public void Take(PositiveInt value) where value < Bound { }
            }
            """);
        var finding = Assert.Single(findings);
        Assert.Equal("MIST0283", finding.Number.Value);
        Assert.Contains("< Bound", finding.Message, StringComparison.Ordinal);
    }

    private static ImmutableArray<Diagnostic> Embedded(string promised, string produced)
    {
        return RefinedReturnContractDecisionTests.Analyse(Types + $$"""
            public abstract IFactory { {{promised}} Make({{produced}} value); }
            public class Factory : IFactory { public {{produced}} Make({{produced}} value) { return value; } }
            """);
    }
}