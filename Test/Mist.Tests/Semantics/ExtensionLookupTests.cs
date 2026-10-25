using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Finding a member on an extension block rather than on the receiver's own type, and choosing
/// between blocks when more than one offers the name.
/// </summary>
[Trait("Tier", "3")]
public sealed class ExtensionLookupTests : ProgramModelProbe
{
    public ExtensionLookupTests()
        : base("MistExtLookup_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Animal
        {
            public int Legs;

            public string Own()
            {
                return "own";
            }
        }

        public class Dog : Animal
        {
        }

        extension(Animal self)
        {
            public string Named()
            {
                return "animal";
            }

            public string OnlyAnimal()
            {
                return "animal-only";
            }

            public int Own()
            {
                return 0;
            }

            public bool Tall => self.Legs > 2;
        }

        extension(Dog self)
        {
            public int Named()
            {
                return 1;
            }
        }

        extension(unknown self)
        {
            public int Universal()
            {
                return 1;
            }
        }

        public class Duo<A, B>
        {
        }

        <T> extension(Duo<T, T> self)
        {
            public int Same()
            {
                return 1;
            }
        }
        """;

    protected override string Preamble { get; } = "Dog dog = new Dog();";

    private string ReturnTypeOf(string call)
    {
        var invocation = InitializerOf<InvocationExpression>($"var chosen = {call};");
        Assert.NotNull(invocation.Method);
        return invocation.Method!.ReturnType.Name;
    }

    [Fact]
    public void AnExtensionMemberIsFoundWhenTheTypeHasNone()
    {
        Assert.Equal("String", ReturnTypeOf("dog.OnlyAnimal()"));
    }

    /// <summary>
    /// Own members win, and nothing states that rule: extension lookup runs on the MISS path, so a
    /// name the type answers never reaches it. Pinned because moving the hook earlier would silently
    /// change which method a call means.
    /// </summary>
    [Fact]
    public void AnOwnMemberBeatsAnExtensionOfTheSameName()
    {
        // The two Own members differ only in return type, which is what makes the choice visible.
        Assert.Equal("String", ReturnTypeOf("dog.Own()"));
    }

    /// <summary>
    /// THE ORDERING TEST. Filtering by member name has to happen BEFORE ranking blocks by
    /// specificity. Rank first and the two Animal/Dog blocks win the tier for this receiver, the
    /// universal block is discarded before its members are looked at, and every universal member
    /// becomes unresolvable -- which is how a prototype of this left the sample with four errors,
    /// all of them universal calls.
    /// </summary>
    [Fact]
    public void AUniversalMemberSurvivesAMoreSpecificBlock()
    {
        Assert.Equal("Int32", ReturnTypeOf("dog.Universal()"));
    }

    /// <summary>
    /// Within the concrete tier the ranking is pairwise and load-bearing. With both blocks offering
    /// Named and neither ranked, the binder records one and C# resolves the other; they agree only
    /// because this picks the same one C# would.
    /// </summary>
    [Fact]
    public void TheMoreSpecificReceiverWins()
    {
        // Both blocks declare Named; only the return type says which one was taken.
        Assert.Equal("Int32", ReturnTypeOf("dog.Named()"));
    }

    /// <summary>
    /// An extension PROPERTY is not an access in the output. C# has no such member, so the site is
    /// reclassified into a call to the carrier's generated getter -- named explicitly, because
    /// `get_Tall` is a name the user never wrote and C#'s own extension lookup has nothing to find.
    /// </summary>
    [Fact]
    public void AnExtensionPropertyBecomesACarrierCall()
    {
        var access = InitializerOf<ExtensionPropertyAccessExpression>("var chosen = dog.Tall;");

        Assert.Equal("Tall", access.Property.Name);
        Assert.Equal("Boolean", access.Type.Name);
        Assert.EndsWith("Extensions_Program_0", access.CarrierName, StringComparison.Ordinal);
    }

    [Fact]
    public void AGenericReceiverRepeatingAParameterMatchesOnlyWhereItsArgumentsAgree()
    {
        Assert.Equal("Int32", ReturnTypeOf("new Duo<int, int>().Same()"));

        var errors = BuildRun("var chosen = new Duo<int, long>().Same();");

        Assert.Contains(errors, d => d.Number.Value == "MIST0301");
    }

    [Fact]
    public void AnExtensionMemberIsFoundThroughOptionalChaining()
    {
        var found = BuildRun("var chosen = maybe?.OnlyAnimal();", extraPreamble: "Dog? maybe = dog;");
        var missing = BuildRun("var chosen = maybe?.NoSuchMember();", extraPreamble: "Dog? maybe = dog;");

        Assert.DoesNotContain(found, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(missing, d => d.Number.Value == "MIST0301");
    }

    [Fact]
    public void AnUnknownMemberIsStillUnresolved()
    {
        var errors = BuildRun("var chosen = dog.NoSuchMember();");

        Assert.Contains(errors, d => d.Number.Value == "MIST0301");
    }
}