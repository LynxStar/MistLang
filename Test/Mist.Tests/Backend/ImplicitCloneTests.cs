// Tier 3 (Component): where a clone is inserted and where it must not be. Asserted on the MODEL,
// because the question is whether a copy happens at all -- and an omitted clone reads as correct
// output until something mutates the source and reads the binding.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ImplicitCloneTests : ProgramModelProbe
{
    public ImplicitCloneTests()
        : base("MistImplicitClone_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Config : IClonable<Config>
        {
            public int Timeout;

            public owned Config Clone() { return new Config(); }
        }

        public class Plain
        {
            public int Value;
        }

        public class Bare
        {
            public int Value;

            public Bare Clone() { return new Bare(); }
        }

        public class Sink
        {
            public static void TakesOwned(owned Config c) { }

            public static void TakesPlain(Config c) { }

            public static owned Config Produce() { return new Config(); }
        }

        public class Vault
        {
            public Vault(owned Config c) { }
        }

        public class Keeper
        {
            public owned Config Held;
            public Config Plain;

            public Keeper()
            {
                this.Held = new Config();
                this.Plain = new Config();
            }
        }

        public class Seeded
        {
            public static Config Source = new Config();
            public owned Config Seed = Source;
        }
        """;

    protected override string Preamble { get; } = "var shared = new Config();";

    /// <summary>
    /// An owned binding promises an INDEPENDENT value, so a source something else still holds is
    /// copied. Without this the two names alias and mutating either is visible through the other.
    /// </summary>
    [Fact]
    public void ANonOwnedSourceIsClonedIntoAnOwnedBinding()
    {
        _ = Assert.IsType<CloneExpression>(InitializerOf("owned Config c = shared;"));
    }

    [Fact]
    public void APlainBindingIsNotCloned()
    {
        _ = Assert.IsType<LocalReferenceExpression>(InitializerOf("Config c = shared;"));
    }

    /// <summary>
    /// Four sources already hand over a value nothing else holds, so copying them buys nothing.
    /// </summary>
    [Theory]
    [InlineData("owned Config c = new Config();")]
    [InlineData("owned Config c = shared.Clone();")]
    [InlineData("Bare b = new Bare();\nowned Bare c = b.Clone();")]
    [InlineData("owned Config c = Sink.Produce();")]
    [InlineData("owned Config a = shared;\nowned Config c = a;")]
    public void AnOwnedSourceIsNotClonedAgain(string statements)
    {
        var declaration = DeclarationIn(statements, extraTypes: "");

        Assert.IsNotType<CloneExpression>(declaration.Initializer);
    }

    /// <summary>
    /// An owned PARAMETER is the same promise made across a call. The parameter's ownership was never
    /// read at all, so a call handed over the caller's own instance and every later mutation of it was
    /// visible through the callee.
    /// </summary>
    [Theory]
    [InlineData("Sink.TakesOwned(shared);")]
    [InlineData("var v = new Vault(shared);")]
    public void AnOwnedParameterClonesItsArgument(string statement)
    {
        Assert.Contains(ArgumentsIn(statement), argument => argument is CloneExpression);
    }

    [Fact]
    public void APlainParameterDoesNot()
    {
        Assert.DoesNotContain(
            ArgumentsIn("Sink.TakesPlain(shared);"), argument => argument is CloneExpression);
    }

    // Intrinsic immutable values are deliberately excluded from implicit cloning. samples/Ownership
    // exercises the Standard string clone contract; this class checks clone insertion for mutable types.

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("var k = new Keeper();\nk.Held = shared;")]
    [InlineData("owned Config c = new Config();\nc = shared;")]
    public void ANonOwnedSourceIsClonedWhenAssignedToAnOwnedSlot(string statements)
    {
        _ = Assert.IsType<CloneExpression>(LastAssignmentIn(statements).Value);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("var k = new Keeper();\nk.Plain = shared;")]
    [InlineData("var k = new Keeper();\nk.Held = shared.Clone();")]
    [InlineData("var k = new Keeper();\nk.Held = Sink.Produce();")]
    [InlineData("var k = new Keeper();\nk.Held = new Config();")]
    public void AnAssignmentNeedingNoCloneIsLeftAlone(string statements)
    {
        Assert.IsNotType<CloneExpression>(LastAssignmentIn(statements).Value);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AnOwnedInitializerEntryClonesItsValue()
    {
        var creation = Assert.IsType<NewExpression>(InitializerOf("var k = new Keeper { Held = shared };"));

        _ = Assert.IsType<CloneExpression>(Assert.Single(creation.Initializer).Value);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AnOwnedFieldInitializerClonesASharedSource()
    {
        var model = BuildModel("", extraTypes: "", extraPreamble: "", out _);
        var seeded = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Seeded"]);

        _ = Assert.IsType<CloneExpression>(seeded.FieldsList.Single(field => field.Name == "Seed").Initializer);
    }

    [Theory]
    [Trait("Tier", "3")]
    [InlineData("var k = new Keeper();\nConfig read = k.Held;", true)]
    [InlineData("owned var k = new Keeper();\nConfig read = k.Held;", false)]
    public void AnOwnedFieldReadThroughAHolder_IsClonedOnlyWhereTheHolderIsShared(string statements, bool cloned)
    {
        Assert.Equal(cloned, InitializerOf(statements) is CloneExpression);
    }

    private AssignmentExpression LastAssignmentIn(string statements)
    {
        return BodyIn(statements).Statements.OfType<ExpressionStatement>().Select(statement => statement.Expression).OfType<AssignmentExpression>().Last();
    }

    private Expression? InitializerOf(string statements)
    {
        return DeclarationIn(statements, extraTypes: "").Initializer;
    }

    /// <summary>
    /// The LAST statement's call. The preamble constructs the shared value, which is itself a call
    /// with no arguments, so taking the first one finds that instead of the statement under test.
    /// </summary>
    private IReadOnlyList<Expression> ArgumentsIn(string statement)
    {
        var last = BodyIn(statement).Statements[^1];

        var expression = last switch
        {
            ExpressionStatement e => e.Expression,
            LocalDeclarationStatement { Initializer: { } initializer } => initializer,
            _ => null,
        };

        switch(expression)
        {
            case InvocationExpression invocation:
                return invocation.Arguments;

            case NewExpression creation:
                return creation.Arguments;

            default:
                Assert.Fail($"no call in the last statement of:\n{statement}");
                return [];
        }
    }
}