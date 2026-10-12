using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PurityRoundFourTests
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string StoresUnowned = "MIST_PA_PURE_SCOPE_STORES_UNOWNED";
    private const string PureFunctionRequired = "MIST_PA_PURE_FUNCTION_REQUIRED";
    private const string ReadonlyContractNotMet = "MIST_PA_READONLY_CONTRACT_NOT_MET";

    [Fact]
    public void FrozenLocalDeclaredOverAnUnrestrictedInput()
    {
        Refused("frozen-local-declared-over-an-unrestricted-input", ReadsMutable, "field 'N'");
    }

    [Fact]
    public void FrozenLocalDeclaredOverAnOwnedBinding_ReadsWhatTheScopeOwns()
    {
        Admitted("frozen-local-declared-over-an-owned-binding");
    }

    [Theory]
    [InlineData("frozen-parameter-lent-a-field-of-an-unrestricted-input", "'h.Items'")]
    [InlineData("frozen-parameter-lent-a-call-result", "lent to 'Size'")]
    public void FrozenParameterLentANonLocalArgument(string fragment, string named)
    {
        Refused(fragment, ReadsMutable, named);
    }

    [Fact]
    public void FrozenParameterLentAnOwnedBinding_IsALoanTheScopeCanMake()
    {
        Admitted("frozen-parameter-lent-an-owned-binding");
    }

    [Fact]
    public void WithFrozenReceiverThroughAPlainLocalOrField()
    {
        Refused("with-frozen-receiver-through-a-plain-local", CallsImpure, "only while its receiver is frozen");
    }

    [Fact]
    public void WithFrozenReceiverThroughAFrozenParameter_IsAdmitted()
    {
        Admitted("with-frozen-receiver-through-a-frozen-parameter");
    }

    [Fact]
    public void FrozenParameterErasedByPureFunctionTypeConversion()
    {
        Refused("frozen-parameter-erased-by-pure-function-type-conversion", PureFunctionRequired, "lent frozen");
    }

    [Fact]
    public void OwnedParameterErasedByPureFunctionTypeConversion()
    {
        Refused("owned-parameter-erased-by-pure-function-type-conversion", PureFunctionRequired, "moved in");
    }

    [Fact]
    public void ParamsArrayPassedDirectlyReadAsFrozenWithoutAFrozenLend()
    {
        Refused("params-array-passed-directly-read-as-frozen-without-a-frozen-lend", ReadsMutable, "lent to 'Sum'");
    }

    [Fact]
    public void ParamsArrayFilledFromExpandedArguments_IsTheCalleesOwn()
    {
        Admitted("params-array-filled-from-expanded-arguments");
    }

    [Fact]
    public void PatternBindingReassignedKeepsItsScrutineeOrigin()
    {
        Refused("pattern-binding-reassigned-keeps-its-scrutinee-origin", Writes, "field 'N'");
    }

    [Fact]
    public void RefReturnOnAnOwnedReceiverPointsIntoAFrozenArgument()
    {
        Refused("ref-return-on-an-owned-receiver-points-into-a-frozen-argument", Writes, "'r'");
    }

    [Fact]
    public void OwnedFieldOfAnOwnedHolderMovedIntoAnotherOwnedSlot_IsAPartialMove()
    {
        Admitted("owned-field-of-an-owned-holder-moved-into-another-owned-slot");
    }

    [Fact]
    public void OwnedFieldOfAnOwnedHolderExtractedIntoAnOwnedBinding_IsAPartialMove()
    {
        Admitted("owned-field-of-an-owned-holder-extracted-into-an-owned-binding");
    }

    [Fact]
    public void NewtypeCopyConversionReadsMutableStorageUnjudged()
    {
        Refused("newtype-copy-conversion-reads-mutable-storage-unjudged", ReadsMutable, "a copy of 'r'");
    }

    [Fact]
    public void LocalReadonlyObjectInitializerStoresUnownedIntoOwnedField()
    {
        Refused("local-readonly-object-initializer-stores-unowned-into-owned-field", StoresUnowned, "'ext'");
    }

    [Fact]
    public void LocalReadonlyObjectInitializerMovesItsOwnOwnedField()
    {
        Refused("local-readonly-object-initializer-moves-its-own-owned-field", StoresUnowned, "held by the owned field 'Items'");
    }

    [Fact]
    public void TypePatternUnboxedStructCopiedIntoAnOwnLocal()
    {
        Refused("type-pattern-unboxed-struct-copied-into-an-own-local", ReadsMutable, "boxed in 'b'");
    }

    [Fact]
    public void ReadonlyStructLocalCapturedByAPureLambdaMutatedThroughALocalMethod()
    {
        Refused("readonly-struct-local-captured-by-a-pure-lambda-mutated-through-a-local-method", CallsImpure, "'Bump'");
    }

    [Fact]
    public void ReadonlyStructLocalCapturedByAPureLambda_MayBeReadThroughALocalReadonlyMethod()
    {
        Admitted("readonly-struct-local-captured-by-a-pure-lambda-read-through-a-local-readonly-method");
    }

    [Fact]
    public void StaticConstructorBodyRunsUnjudged()
    {
        Refused("static-constructor-body-runs-unjudged", CallsImpure, "static initializers of 'Util'");
    }

    [Fact]
    public void StaticConstructorWritingItsOwnStatics_InitializesThem()
    {
        Admitted("static-constructor-writing-its-own-statics");
    }

    [Fact]
    public void ImplicitChainToParamsOnlyBaseConstructor()
    {
        Refused("implicit-chain-to-params-only-base-constructor", CallsImpure, "chain to the constructor of 'Base'");
    }

    [Fact]
    public void IndexFromEndRunsCountGetterUnjudged()
    {
        Refused("index-from-end-runs-count-getter-unjudged", CallsImpure, "'Count'");
    }

    [Fact]
    public void ListPatternRunsCountAndIndexerGettersUnjudged()
    {
        Refused("list-pattern-runs-count-and-indexer-getters-unjudged", CallsImpure, "'Count'");
    }

    [Fact]
    public void ForeachCallsEnumeratorDisposeUnjudged()
    {
        Refused("foreach-calls-enumerator-dispose-unjudged", CallsImpure, "'Dispose'");
    }

    [Fact]
    public void LinearLocalMovedOnlyInASwitchArmIsDroppedByTheEmitter()
    {
        Assert.False(new PurityFragments().LinearLocalMovedIntoAnAssignedCallIsDropped());
    }

    [Fact]
    public void ListComparisonsDispatchToUnjudgedMistCompareToEquals()
    {
        Refused("list-sort-dispatches-to-an-impure-icomparable-compareto", CallsImpure, "'CompareTo'");
    }

    [Fact]
    public void ListPromisesDispatchIntoElementClrInterfaces()
    {
        Refused("list-contains-dispatches-to-an-impure-iequatable-equals", CallsImpure, "'Equals'");
    }

    [Fact]
    public void StandardListContainsDeclaredLocalReadonlyReadsArgumentAndElementStateThroughEquals()
    {
        Refused("list-contains-compares-clr-elements-through-unseen-equals", CallsImpure, "cannot see");
    }

    [Fact]
    public void ListContainsOverIntrinsicElements_ComparesNothingItCannotSee()
    {
        Admitted("list-contains-compares-intrinsic-elements");
    }

    [Fact]
    public void InterpolationDispatchesToIFormattableToString()
    {
        Refused("interpolation-dispatches-to-iformattable-tostring", CallsImpure, "'value'");
    }

    [Fact]
    public void StringConcatenationFormatsATypeParameterThroughRuntimeToString()
    {
        Refused("string-concatenation-formats-a-type-parameter-through-runtime-tostring", CallsImpure, "'o'");
    }

    [Theory]
    [InlineData("constructor-context-clause-read-without-declaring-it")]
    [InlineData("chained-constructor-context-clause-read-without-declaring-it")]
    public void ConstructorContextClauseReadWithoutDeclaringIt(string fragment)
    {
        Refused(fragment, ReadsMutable, "through the constructor of 'Seeded'");
    }

    [Fact]
    public void EnumVariantReadRunsMemberValueInitializers()
    {
        Refused("enum-variant-read-runs-member-value-initializers", CallsImpure, "static initializers of 'Level'");
    }

    [Fact]
    public void AnonymousTypeEqualityDispatchesToIEquatable()
    {
        Refused("anonymous-type-equality-dispatches-to-iequatable", CallsImpure, "'Equals'");
    }

    [Fact]
    public void RestatedAbstractDefaultBodyReResolvesOverloads()
    {
        Assert.Contains("((Shape)this)", new PurityFragments().EmittedOverloadedCall("this of an abstract"), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenUnionSpecializationReResolvesOverloads()
    {
        Assert.Contains("((object)x)", new PurityFragments().EmittedOverloadedCall("a type parameter"), StringComparison.Ordinal);
    }

    [Fact]
    public void StructFlatteningReResolvesOverloads()
    {
        Assert.Contains("((Point)this)", new PurityFragments().EmittedOverloadedCall("this of a struct"), StringComparison.Ordinal);
    }

    [Fact]
    public void SlotImplementationContextClauseHiddenByAbstractDispatch()
    {
        Refused("slot-implementation-context-clause-hidden-by-abstract-dispatch", CallsImpure, "readonly");
    }

    [Theory]
    [InlineData("default-of-a-derived-type-runs-no-base-constructor")]
    [InlineData("new-of-a-constructorless-derived-type-runs-no-base-constructor")]
    public void DerivedDefaultStartsFromTheBaseDefaultNotItsConstructor(string fragment)
    {
        Admitted(fragment);
    }

    [Fact]
    public void ForeachOverAnExtensionGetEnumeratorIsUnjudged()
    {
        Refused("foreach-over-an-extension-getenumerator-is-unjudged", CallsImpure, "GetEnumerator only emission resolves");
    }

    [Fact]
    public void IndexingATypeParameterReceiverBindsToErrorTypeSoEveryPurityFindingIsSuppressed()
    {
        _ = Assert.Single(new PurityFragments().IndexersOfAConstrainedTypeParameter());
    }

    [Fact]
    public void PureGetOnASettableAutoPropertyIsTrustedWithoutBeingJudged()
    {
        Refused("pure-get-on-a-settable-auto-property-is-trusted-without-being-judged", ReadsMutable, "storage of 'V'");
    }

    [Fact]
    public void AbstractSlotAttributedToTheFirstOverloadATypeParameterAgreesWith()
    {
        Refused("abstract-slot-attributed-to-the-first-overload-a-type-parameter-agrees-with", CallsImpure, "'Get'");
    }

    [Fact]
    public void AbstractSlotAttributedToAnotherConformancesWithMapping()
    {
        Refused("abstract-slot-attributed-to-another-conformances-with-mapping", CallsImpure, "'Get'");
    }

    [Fact]
    public void AbstractIndexerImplementedInAGenericBaseClassIsNeverFoundSoTheSlotMeetsToPure()
    {
        Refused("abstract-indexer-implemented-in-a-generic-base-class-is-never-found-so-the-slot-meets-to-pure", CallsImpure, "'this[]'");
    }

    [Fact]
    public void AbstractMethodImplementedInAGenericBaseClassIsReadThroughItsClosedBase()
    {
        Refused("abstract-method-implemented-in-a-generic-base-class-is-read-through-its-closed-base", CallsImpure, "'Get'");
    }

    [Fact]
    public void PureNonDefaultableTypesImplicitConstructorRunsUnjudgedFieldInitializers()
    {
        Refused("pure-non-defaultable-types-implicit-constructor-runs-unjudged-field-initializers", CallsImpure, "'Note'");
    }

    [Fact]
    public void GenericDefinitionsFieldInitializerIsHeldToADefaultsConsistency()
    {
        Refused("generic-definitions-field-initializer-is-a-default-of-every-instantiation", "MIST_PA_DEFAULT_NOT_CONSISTENT", "'Note'");
    }

    [Fact]
    public void GenericExtensionDeclaringPureToStringIsTrustedWhileTheRuntimeObjectsToStringRuns()
    {
        Refused("generic-extension-declaring-pure-tostring-is-trusted-while-the-runtime-objects-tostring-runs", CallsImpure, "'ToString'");
    }

    [Fact]
    public void AbstractDefaultMethodsDeclaredPromiseJoinsTheDefaultsInferenceAndSkipsTheImplementationsMeet()
    {
        Refused("abstract-default-methods-declared-promise-joins-the-defaults-inference-and-skips-the-implementations-meet", CallsImpure, "it is local");
    }

    [Fact]
    public void ReadonlyDefaultMethodsReplacingMemberIsNeverHeldToReadonly()
    {
        Refused("readonly-default-methods-replacing-member-is-never-held-to-readonly", ReadonlyContractNotMet, "'Bump'");
    }

    private static void Refused(string fragment, string expected, string named)
    {
        var finding = Assert.Single(new PurityFragments().RoundFour(fragment));

        Assert.True(finding.Id.Value == expected, $"{fragment}: expected {expected}, got {finding.Id.Value}: {finding.Message}");
        Assert.Contains(named, finding.Message, StringComparison.Ordinal);
    }

    private static void Admitted(string fragment)
    {
        var findings = new PurityFragments().RoundFour(fragment);

        Assert.True(findings.Count == 0, $"{fragment}: {string.Join("; ", findings.Select(finding => finding.Message))}");
    }
}