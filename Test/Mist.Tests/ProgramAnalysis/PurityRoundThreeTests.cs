// Tier 2 (Functional): purity soundness over hand-built fragments -- an owned slot filled from what something else
// holds, a readonly type laundering mutable state, a ref, an enumerator and a params array read as the scope's own,
// the constructor chain, static initializer, drop and context a call runs without writing, and the promises a
// conversion, a universal member or an interpolation hole claims without earning -- each refused, with the admitted
// rows that pin where each rule stops.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PurityRoundThreeTests
{
    private const string OwnedSourceNotOwned = "MIST0264";
    private const string OwnedReturnNotOwned = "MIST0263";
    private const string Writes = "MIST0137";
    private const string ReadsMutable = "MIST0138";
    private const string CallsImpure = "MIST0139";
    private const string StoresUnowned = "MIST0141";
    private const string AllocatesMutable = "MIST0260";
    private const string NoImplicitConversion = "MIST0157";

    [Theory]
    [InlineData("owned-binding-initialized-from-an-unrestricted-reference")]
    [InlineData("owned-binding-initialized-from-a-frozen-reference")]
    public void OwnedBindingInitializedFromAnUnrestrictedReference(string fragment)
    {
        Refused(fragment, OwnedSourceNotOwned, "owned binding 'b'");
    }

    [Fact]
    public void OwnedBindingReassignedFromAnExternal()
    {
        Refused("owned-binding-reassigned-from-an-external", OwnedSourceNotOwned, "parameter 'ext'");
    }

    [Fact]
    public void OwnedMethodParameterFedAnUnrestrictedReference()
    {
        Refused("owned-method-parameter-fed-an-unrestricted-reference", OwnedSourceNotOwned, "owned parameter 'b'");
    }

    [Theory]
    [InlineData("owned-constructor-parameter-fed-an-unrestricted-reference")]
    [InlineData("owned-constructor-parameter-fed-a-frozen-reference")]
    [InlineData("owned-constructor-parameter-fed-a-plain-local")]
    public void OwnedConstructorParameterFedAnUnrestrictedOrFrozenReference(string fragment)
    {
        Refused(fragment, OwnedSourceNotOwned, "owned parameter 'inner'");
    }

    [Theory]
    [InlineData("owned-field-initializer-aliases-a-static")]
    [InlineData("owned-field-initializer-aliases-an-unowned-return")]
    public void OwnedFieldInitializerAliasesASharedReference(string fragment)
    {
        Refused(fragment, OwnedSourceNotOwned, "owned field 'Inner'");
    }

    [Fact]
    public void DefaultEntryAliasesASharedReferenceIntoAnOwnedField()
    {
        Refused("default-entry-aliases-a-static-into-an-owned-field", OwnedSourceNotOwned, "field 'Shared'");
    }

    [Fact]
    public void OwnedSlotProvenanceIsEnforcedOutsidePureScopes()
    {
        Refused("unmarked-method-stores-an-unrestricted-parameter-in-an-owned-field", OwnedSourceNotOwned, "owned field 'B'");
    }

    [Theory]
    [InlineData("owned-binding-moved-from-an-owned-binding")]
    [InlineData("owned-constructor-parameter-fed-an-owned-local")]
    public void AnOwnedDestination_TakesWhatNothingElseHolds(string fragment)
    {
        Admitted(fragment);
    }

    [Theory]
    [InlineData("ref-local-over-a-ref-returning-call-on-a-frozen-receiver")]
    [InlineData("write-through-a-ref-returning-call-on-a-frozen-receiver")]
    public void RefLocalOverARefReturningCallOnAFrozenReceiver(string fragment)
    {
        Refused(fragment, Writes, "a frozen input");
    }

    [Fact]
    public void ARefReturnedByALocalMemberOfAnOwnedReceiver_IsTheScopesOwn()
    {
        Admitted("ref-local-over-a-ref-returning-call-on-an-owned-receiver");
    }

    [Fact]
    public void ListEnumeratorStructCopyObservesTheList()
    {
        Refused("pure-method-advances-an-intrinsic-enumerator-copy", CallsImpure, "'MoveNext'");
    }

    [Fact]
    public void ParamsArrayPassedDirectlyIsTreatedAsTheCalleesOwn()
    {
        Refused("pure-method-writes-its-params-array", Writes, "'xs'");
    }

    [Theory]
    [InlineData("pure-method-stores-a-readonly-class-holding-an-owned-mutable-field")]
    [InlineData("pure-method-stores-a-readonly-class-with-a-settable-property")]
    public void ReadonlyClassWithAnOwnedMutableFieldCountsAsUnchangeable(string fragment)
    {
        Refused(fragment, StoresUnowned, "'ext'");
    }

    [Fact]
    public void AReadonlyClassHoldingOnlyStableValues_StillCannotChange()
    {
        Admitted("pure-method-stores-a-readonly-class-of-stable-values");
    }

    [Fact]
    public void OwnedReturnOfASharedReadonlyClassInstanceHoldingAMutableOwnedField()
    {
        Refused("owned-return-of-a-static-readonly-class-holding-an-owned-mutable-field", OwnedReturnNotOwned, "'Get'");
    }

    [Theory]
    [InlineData("pure-method-allocates-a-defaultable-type-chaining-to-an-impure-base", CallsImpure)]
    [InlineData("readonly-method-allocates-a-defaultable-type-chaining-to-an-impure-base", AllocatesMutable)]
    public void DefaultableDerivedTypesImplicitConstructorChainsToAnImpureBaseConstructor(string fragment, string expected)
    {
        Refused(fragment, expected, "Derived");
    }

    [Fact]
    public void StaticReadonlyFieldInitializerPerformsIoWhenFirstReadFromAPureMethod()
    {
        Refused("pure-method-reads-a-static-whose-initializer-is-impure", CallsImpure, "'Seed'");
    }

    [Fact]
    public void AStaticWhoseInitializersArePure_IsReadable()
    {
        Admitted("pure-method-reads-a-static-whose-initializer-is-pure");
    }

    [Fact]
    public void TransparentContextPropagationMakesAParameterlessPureMethodNondeterministic()
    {
        Refused("pure-method-calls-a-member-reading-an-undeclared-context", ReadsMutable, "'Inner'");
    }

    [Fact]
    public void AContextTheScopeDeclares_IsOneOfItsInputs()
    {
        Admitted("pure-method-declaring-the-context-calls-a-member-reading-it");
    }

    [Fact]
    public void ANonPureFuncValueConvertsToAPureFuncContextBindingUnchecked()
    {
        Refused("plain-function-made-ambient-under-a-pure-function-key", NoImplicitConversion, "pure func<");
    }

    [Theory]
    [InlineData(true, "MistContext.Push<MistContext.Pure<System.Func<")]
    [InlineData(false, "MistContext.Push<System.Func<")]
    public void ContextKeyingErasesPure_NoLongerFilesAPlainFunctionWhereAPureOneIsRead(bool pure, string key)
    {
        Assert.Contains(key, new PurityFragments().EmittedContextKey(pure), StringComparison.Ordinal);
    }

    [Fact]
    public void ImplicitUserDefinedConversionsOfExpandedFormParamsElementsRunUnjudged()
    {
        var findings = new PurityFragments().RoundThree("pure-method-passes-params-elements-through-an-impure-conversion");

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(CallsImpure, finding.Number.Value));
    }

    [Fact]
    public void TheEmitterChainsTheDropOfWhatADropReturns()
    {
        Refused("pure-method-holds-a-linear-whose-drop-hands-back-an-impure-linear", CallsImpure, "drop 'b'");
    }

    [Theory]
    [InlineData("declared-pure-drop-owes-an-impure-field-drop")]
    [InlineData("declared-pure-drop-owes-an-impure-owned-field-drop")]
    public void ADeclaredDropsEmittedBodyAlsoReleasesTheFieldsItDidNotHandOn(string fragment)
    {
        Refused(fragment, CallsImpure, "drop 'H'");
    }

    [Fact]
    public void ALinearContainersSynthesizedDropOfATypeParameterFieldIsJudged()
    {
        Refused("pure-method-drops-a-generic-container-of-an-impure-linear", CallsImpure, "drop 'g'");
    }

    [Fact]
    public void APureTypesSettableAutoPropertyIsWrittenFromOutside()
    {
        Refused("pure-type-settable-auto-property", Writes, "'X'");
    }

    [Fact]
    public void APureTypesInitOnlyAutoProperty_IsWrittenOnlyWhileConstructed()
    {
        Admitted("pure-type-init-only-auto-property");
    }

    [Fact]
    public void AUniversalExtensionsPlaceholderBody_IsNotWhatRuns()
    {
        Refused("pure-method-calls-a-universal-tostring", CallsImpure, "'ToString'");
    }

    [Fact]
    public void AnInterpolationHoleOfAReferenceTheProgramCannotSee_IsFormattedByUnknownCode()
    {
        Refused("pure-method-interpolates-a-clr-reference", CallsImpure, "'value'");
    }

    [Fact]
    public void AnInterpolationHoleOfATypeDeclaredInMist_PrintsItsTypeName()
    {
        Admitted("pure-method-interpolates-a-type-declared-in-mist");
    }

    [Theory]
    [InlineData("pure-generic-allocates-its-type-parameter", CallsImpure)]
    [InlineData("readonly-generic-allocates-its-type-parameter", AllocatesMutable)]
    public void NewTUnderAConstructorConstraintRunsAnUnmarkedConstructor(string fragment, string expected)
    {
        Refused(fragment, expected, "'T'");
    }

    private static void Refused(string fragment, string expected, string named)
    {
        var finding = Assert.Single(new PurityFragments().RoundThree(fragment));

        Assert.True(finding.Number.Value == expected, $"{fragment}: expected {expected}, got {finding.Number.Value}: {finding.Message}");
        Assert.Contains(named, finding.Message, StringComparison.Ordinal);
    }

    private static void Admitted(string fragment)
    {
        var findings = new PurityFragments().RoundThree(fragment);

        Assert.True(findings.Count == 0, $"{fragment}: {string.Join("; ", findings.Select(finding => finding.Message))}");
    }
}