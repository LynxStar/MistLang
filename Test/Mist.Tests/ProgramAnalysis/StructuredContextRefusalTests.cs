// Tier 2 (Functional): the structured-context rule judges hand-built bodies and the calls between them.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class StructuredContextRefusalTests
{
    [Theory]
    [InlineData("call-with-no-context")]
    [InlineData("call-string-under-an-optional-string-context")]
    [InlineData("call-optional-string-under-a-string-context")]
    [InlineData("entry-instance-extension-with-no-context")]
    [InlineData("entry-static-extension-with-no-context")]
    [InlineData("entry-default-with-no-context")]
    [InlineData("call-after-the-with-block-ends")]
    [InlineData("call-inside-the-value-a-with-pushes")]
    [InlineData("call-under-a-context-of-another-type")]
    [InlineData("call-under-a-context-of-a-derived-type")]
    [InlineData("call-under-a-union-holding-the-key")]
    [InlineData("constructor-with-no-context")]
    [InlineData("slot-call-with-no-context")]
    [InlineData("value-lambda-with-no-context")]
    [InlineData("value-method-named-with-no-context")]
    [InlineData("generic-key-closed-by-the-call-with-no-context")]
    [InlineData("generic-key-in-a-transparent-method")]
    [InlineData("initializer-of-a-static-field")]
    [InlineData("slot-of-the-runtime-answered-transparently")]
    [InlineData("implicit-enumerator-with-no-context")]
    [InlineData("implicit-move-next-with-no-context")]
    [InlineData("implicit-add-with-no-context")]
    [InlineData("drop-owed-a-key-at-a-locals-exit")]
    public void ACallOwedAContextNothingHoldsIsRefusedWhereItIsWritten(string caseName)
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze(caseName));

        Assert.True(diagnostic.Number.Value == "MIST_CTX_0001", $"{caseName}: got {diagnostic.Number.Value}");
        Assert.Equal(StructuredContextFragments.Site + 1, diagnostic.Location.Start.Column);
        Assert.EndsWith($"hand-built.mist:1:{StructuredContextFragments.Site + 1}.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("transparent-method-reached-bare")]
    [InlineData("transparent-method-with-one-bare-caller")]
    [InlineData("cycle-reached-bare")]
    [InlineData("slot-answered-transparently-reached-bare")]
    [InlineData("value-local-function-called-bare")]
    [InlineData("constructor-chain-reached-bare")]
    [InlineData("scope-judged-alone-with-a-bare-caller-outside")]
    [InlineData("accessor-read-bare")]
    [InlineData("accessor-read-back-by-a-coalescing-assignment-bare")]
    [InlineData("accessor-written-bare")]
    [InlineData("accessor-written-by-an-object-initializer-bare")]
    [InlineData("accessor-read-by-a-property-pattern-bare")]
    [InlineData("accessor-through-a-slot-reached-bare")]
    [InlineData("accessor-of-an-extension-block-reached-bare")]
    [InlineData("indexer-read-bare")]
    [InlineData("indexer-read-by-a-list-pattern-bare")]
    [InlineData("slice-read-bare")]
    [InlineData("slice-read-by-a-list-pattern-bare")]
    [InlineData("indexer-written-bare")]
    [InlineData("operator-applied-bare")]
    [InlineData("operator-unary-applied-bare")]
    [InlineData("conversion-cast-bare")]
    [InlineData("conversion-implicit-bare")]
    [InlineData("drop-of-a-local-reached-bare")]
    [InlineData("drop-of-a-local-after-a-later-with")]
    [InlineData("drop-of-a-with-blocks-value-is-after-its-pop")]
    [InlineData("drop-of-a-parameter-reached-bare")]
    [InlineData("drop-of-a-field-in-a-drop-body-reached-bare")]
    [InlineData("dispose-called-bare")]
    [InlineData("loop-current-read-bare")]
    [InlineData("loop-enumerator-disposed-bare")]
    public void ACallReachedWithNoContextIsRefusedWhereItIsWrittenAndNamesTheCallThatReachesIt(string caseName)
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze(caseName));

        Assert.True(diagnostic.Number.Value == "MIST_CTX_0001", $"{caseName}: got {diagnostic.Number.Value}");
        Assert.Equal(StructuredContextFragments.Site + 1, diagnostic.Location.Start.Column);
        Assert.EndsWith($"hand-built.mist:1:{StructuredContextFragments.Outer + 1}.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("call-under-a-with-block")]
    [InlineData("call-optional-string-under-its-context")]
    [InlineData("entry-instance-extension-under-a-context")]
    [InlineData("entry-static-extension-under-a-context")]
    [InlineData("entry-default-under-a-context")]
    [InlineData("call-under-an-alias-of-the-key")]
    [InlineData("call-under-a-refinement-of-the-key")]
    [InlineData("call-of-a-refined-clause-under-its-base")]
    [InlineData("call-owed-two-keys-under-both")]
    [InlineData("call-from-a-member-with-the-clause")]
    [InlineData("transparent-method-under-a-context")]
    [InlineData("transparent-method-nothing-calls")]
    [InlineData("cycle-under-a-context")]
    [InlineData("cycle-nothing-reaches")]
    [InlineData("slot-answered-transparently-under-a-context")]
    [InlineData("value-lambda-under-a-context")]
    [InlineData("value-method-named-under-a-context")]
    [InlineData("value-local-function-called-under-a-later-context")]
    [InlineData("value-local-function-declared-under-a-context")]
    [InlineData("constructor-under-a-with-block")]
    [InlineData("constructor-chain-under-a-context")]
    [InlineData("constructor-chain-from-a-constructor-with-the-clause")]
    [InlineData("generic-key-closed-by-the-call-under-a-context")]
    [InlineData("generic-key-in-a-method-with-the-clause")]
    [InlineData("scope-judged-alone-with-a-holding-caller-outside")]
    [InlineData("runtime-called-Describe")]
    [InlineData("runtime-called-ToString")]
    [InlineData("implicit-enumerator-under-a-context")]
    [InlineData("implicit-add-under-a-context")]
    [InlineData("accessor-read-under-a-context")]
    [InlineData("accessor-nothing-reads")]
    [InlineData("accessor-only-written-leaves-the-getter-unread")]
    [InlineData("accessor-written-under-a-context")]
    [InlineData("accessor-written-by-an-object-initializer-under-a-context")]
    [InlineData("accessor-read-by-a-property-pattern-under-a-context")]
    [InlineData("accessor-through-a-slot-under-a-context")]
    [InlineData("accessor-of-an-extension-block-under-a-context")]
    [InlineData("indexer-read-under-a-context")]
    [InlineData("indexer-read-by-a-list-pattern-under-a-context")]
    [InlineData("slice-read-under-a-context")]
    [InlineData("slice-read-by-a-list-pattern-under-a-context")]
    [InlineData("indexer-written-under-a-context")]
    [InlineData("operator-applied-under-a-context")]
    [InlineData("operator-unary-applied-under-a-context")]
    [InlineData("operator-nothing-applies")]
    [InlineData("conversion-cast-under-a-context")]
    [InlineData("conversion-implicit-under-a-context")]
    [InlineData("drop-of-a-local-under-a-context")]
    [InlineData("drop-of-a-with-blocks-value-under-an-outer-context")]
    [InlineData("drop-of-a-parameter-under-a-context")]
    [InlineData("drop-of-a-field-in-a-drop-body-under-a-context")]
    [InlineData("drop-called-explicitly-under-a-context")]
    [InlineData("dispose-called-under-a-context")]
    [InlineData("loop-current-read-under-a-context")]
    [InlineData("loop-enumerator-disposed-under-a-context")]
    public void ACallWhoseContextIsHeldWhereverItRunsIsAdmitted(string caseName)
    {
        Assert.Empty(new StructuredContextFragments().Analyze(caseName));
    }

    [Fact]
    public void ACloneTheLanguageRunsWithNoCallWrittenHasNoCallerToHoldAContextForIt()
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze("runtime-called-Clone"));

        Assert.Equal("MIST_CTX_0001", diagnostic.Number.Value);
        Assert.Equal(StructuredContextFragments.Site + 1, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void AnInstanceInitializerIsLeftToTheRuleThatJudgesDefaults()
    {
        Assert.Empty(new StructuredContextFragments().Analyze("initializer-of-an-instance-field"));
    }

    [Fact]
    public void ARefusalSaysWhetherAMethodOrAConstructorIsOwedTheContext()
    {
        var owed = Assert.Single(new StructuredContextFragments().Analyze("call-with-no-context"));
        var built = Assert.Single(new StructuredContextFragments().Analyze("constructor-with-no-context"));

        Assert.StartsWith("Method 'Read' requires context of type 'string', but no matching context declaration is in scope at ", owed.Message, StringComparison.Ordinal);
        Assert.StartsWith("Constructor 'Greeter' requires context of type 'string',", built.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallOwedTwoKeysIsRefusedOnceForTheOneNotHeld()
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze("call-owed-two-keys-under-one"));

        Assert.Equal("MIST_CTX_0001", diagnostic.Number.Value);
        Assert.Contains("'Both' requires context of type 'int'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name-of-a-clause-assigned", "is not reassignable.")]
    [InlineData("name-of-a-clause-passed-by-ref", "is not reassignable, so it cannot be passed as a ref argument.")]
    [InlineData("name-of-a-clause-aliased-by-a-ref-local", "is not reassignable, so it cannot be bound to ref local 'alias'.")]
    public void ANameStandingForAnAmbientValueIsNeverGivenAnother(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze(caseName));

        Assert.Equal("MIST_CTX_0005", diagnostic.Number.Value);
        Assert.Equal(StructuredContextFragments.Site + 1, diagnostic.Location.Start.Column);
        Assert.Equal($"Context-bound variable 's' {expected}", diagnostic.Message);
    }

    [Theory]
    [InlineData("name-of-an-ordinary-local-assigned")]
    [InlineData("name-of-a-clause-passed-by-value")]
    [InlineData("name-of-a-clause-aliased-by-a-readonly-ref-local")]
    public void ReadingAContextBoundNameOrAssigningAnOrdinaryOneIsAdmitted(string caseName)
    {
        Assert.Empty(new StructuredContextFragments().Analyze(caseName));
    }

    [Theory]
    [InlineData("clause-key-bound-twice")]
    [InlineData("clause-key-bound-twice-through-an-alias")]
    [InlineData("clause-key-bound-twice-on-a-constructor")]
    public void AKeyBoundTwiceByOneMemberIsRefusedAtTheLaterClause(string caseName)
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze(caseName));

        Assert.Equal("MIST_CTX_0002", diagnostic.Number.Value);
        Assert.Equal(StructuredContextFragments.Site + 1, diagnostic.Location.Start.Column);
        Assert.EndsWith("'c0' already binds it at hand-built.mist:1:1.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACalleeBindingOneKeyTwiceIsOwedItOnce()
    {
        var diagnostics = new StructuredContextFragments().Analyze("clause-key-bound-twice-and-called-with-no-context");

        Assert.Equal(["MIST_CTX_0002", "MIST_CTX_0001"], diagnostics.Select(diagnostic => diagnostic.Number.Value));
    }

    [Fact]
    public void TwoClausesOverDifferentKeysAreAdmitted()
    {
        Assert.Empty(new StructuredContextFragments().Analyze("clause-keys-differ"));
    }

    [Theory]
    [InlineData("implementation-omits-the-slots-clause", "`with string` is missing.")]
    [InlineData("implementation-adds-a-clause", "`with string` is not declared by the slot.")]
    [InlineData("implementation-binds-another-type", "`with string` is missing; `with int` is not declared by the slot.")]
    public void AMemberFillingASlotDeclaresTheSlotsClausesExactly(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze(caseName));

        Assert.Equal("MIST_CTX_0006", diagnostic.Number.Value);
        Assert.Equal(StructuredContextFragments.Site + 1, diagnostic.Location.Start.Column);
        Assert.Equal(
            $"Method 'Pull' on type 'Tap' must declare the same `with` clauses as the slot it fills on abstract 'Source': {expected}",
            diagnostic.Message);
    }

    [Fact]
    public void AMemberRepeatingItsSlotsClauseIsAdmitted()
    {
        Assert.Empty(new StructuredContextFragments().Analyze("implementation-repeats-the-slots-clause"));
    }

    [Fact]
    public void AnAnonymousObjectKeysNothingAClauseCouldName()
    {
        var diagnostic = Assert.Single(new StructuredContextFragments().Analyze("anonymous-key"));

        Assert.Equal("MIST_CTX_0004", diagnostic.Number.Value);
        Assert.Equal(StructuredContextFragments.Site + 1, diagnostic.Location.Start.Column);
    }
}