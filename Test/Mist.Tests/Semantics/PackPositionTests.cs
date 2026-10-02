using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 3: a pack named where one type is required is refused by the binder (variadics Requirement
// 6.3), on declarations no test here instantiates or calls. Every row holds the build to that one error.
[Trait("Tier", "3")]
public sealed class PackPositionTests : ProgramModelProbe
{
    public PackPositionTests()
        : base("MistPackPosition_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract IRank<T> { }
        public class One<T> { }
        public class Tup<params T> { }
        public class Two<Type[]:A, Type[]:B> { }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public TArgs Item;")]
    [InlineData("public TArgs Value { get; }")]
    [InlineData("public void Use(TArgs single) { }")]
    [InlineData("public TArgs Get() { return Get(); }")]
    [InlineData("public Bare(TArgs single) { }")]
    [InlineData("public (TArgs, int) Pair() { return Pair(); }")]
    [InlineData("public void Use(TArgs[] items) { }")]
    [InlineData("public void Use(params TArgs[] items) { }")]
    [InlineData("public void Use(TArgs? maybe) { }")]
    [InlineData("public void Use(TArgs | int either) { }")]
    [InlineData("public void Use(func<TArgs, int> f) { }")]
    [InlineData("public One<TArgs> Held() { return Held(); }")]
    [InlineData("public Tup<TArgs> Held() { return Held(); }")]
    [InlineData("public Two<TArgs, [int]> Held() { return Held(); }")]
    [InlineData("public int Use() { TArgs copy; return 0; }")]
    [InlineData("public void Use() { int Inner(TArgs single) { return 1; } }")]
    [InlineData("public void Use() { var f = (TArgs x) => 1; }")]
    [InlineData("public bool Use(unknown value) { return value is TArgs; }")]
    [InlineData("public bool Use(unknown value) { return value is TArgs { Length: 1 }; }")]
    [InlineData("public void Use() { var made = new TArgs(); }")]
    [InlineData("public void Use() { var made = new TArgs[3]; }")]
    public void APackNamedAloneInAVariadicType_IsRefused(string member)
    {
        AssertRefusedOnce($"public class Bare<params TArgs> {{ {member} }}");
    }

    [Theory]
    [InlineData("public abstract ISource<params TArgs> { TArgs Get(); }")]
    [InlineData("public struct Bare<Type[]:TArgs> { public void Use(TArgs single) { } }")]
    [InlineData("public struct Bare<Type[2]:TArgs> { public void Use(TArgs single) { } }")]
    [InlineData("public type Bag<params T> = One<T>;")]
    [InlineData("public class Holder { public static int Describe<params TArgs>(TArgs single) { return 0; } }")]
    [InlineData("public class Keyed<T, params TArgs> where T : IRank<TArgs> { }")]
    public void APackNamedAloneInAnyDeclarationKind_IsRefused(string declaration)
    {
        AssertRefusedOnce(declaration);
    }

    [Theory]
    [InlineData("public TArgs... Item;")]
    [InlineData("public void Use(params TArgs args) { }")]
    [InlineData("public int Width() { return TArgs.Length; }")]
    [InlineData("public Tup<TArgs...> Held() { return Held(); }")]
    [InlineData("public Two<[TArgs...], [int]> Held() { return Held(); }")]
    [InlineData("public void Use(func<TArgs..., int> f) { }")]
    [InlineData("public void Use((int, TArgs...) led) { }")]
    public void APackSpreadOrReadAsAValue_IsNot(string member)
    {
        Assert.Empty(Errors($"public class Fine<params TArgs> {{ {member} }}"));
    }

    [Fact]
    public void APacksOwnConstraint_ReadsItsNameAsTheElement()
    {
        Assert.Empty(Errors("public class Row<params Ts> where Ts : IRank<Ts> { }"));
    }

    [Fact]
    public void AnIndexedPack_IsNotThisRulesToRefuse()
    {
        Assert.DoesNotContain(
            Errors("public struct Pair<Type[2]:TArgs> { public void Use(TArgs[1] second) { } }"),
            error => error.Id.Value == "MIST_VAR_0002");
    }

    [Fact]
    public void TheRefusedReference_TakesNoTypeIntoTheModel()
    {
        var model = BuildModel(
            statement: "", extraTypes: "public class Bare<params TArgs> { public TArgs Item; }", extraPreamble: "", out _);
        var bare = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Probe.Bare<>"]);

        _ = Assert.IsType<ErrorType>(bare.FieldsList.Single().Type);
    }

    private void AssertRefusedOnce(string declaration)
    {
        var error = Assert.Single(Errors(declaration));

        Assert.Equal("MIST_VAR_0002", error.Id.Value);
    }

    private List<Diagnostic> Errors(string declaration)
    {
        return [.. BuildRun("", declaration).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}