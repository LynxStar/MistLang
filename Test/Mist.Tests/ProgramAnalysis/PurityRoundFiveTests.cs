using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PurityRoundFiveTests
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string StoresUnowned = "MIST_PA_PURE_SCOPE_STORES_UNOWNED";
    private const string PureFunctionRequired = "MIST_PA_PURE_FUNCTION_REQUIRED";
    private const string OwnedSourceNotOwned = "MIST_PA_OWNED_SOURCE_NOT_OWNED";
    private const string OwnedReturnNotOwned = "MIST_PA_OWNED_RETURN_NOT_OWNED";
    private const string ReadonlyCallsMutable = "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE";
    private const string ReadonlyAllocatesMutable = "MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE";
    private const string ReadonlyWritesThroughRef = "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_REF";
    private const string FieldAssignedInReadonlyMember = "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER";

    [Theory]
    [InlineData("clone-returning-this-into-an-owned-binding", "owned binding 'c'")]
    [InlineData("clone-returning-this-into-an-owned-parameter", "owned parameter 't'")]
    [InlineData("clone-taking-this-owned-of-a-frozen-source", "owned binding 'c'")]
    [InlineData("clone-returning-this-inside-a-readonly-member", "owned binding 'c'")]
    public void InsertedCloneIsTrustedToBeFresh(string fragment, string named)
    {
        Refused(fragment, OwnedSourceNotOwned, named);
    }

    [Fact]
    public void InsertedCloneOfAnOwnedReturningClone_IsFresh()
    {
        Admitted("clone-returning-a-fresh-owned-copy");
    }

    [Theory]
    [InlineData("ref-local-aliasing-a-field-of-a-captured-struct")]
    [InlineData("ref-argument-aliasing-a-field-of-a-captured-struct")]
    public void CapturedStructMutatedThroughARefAliasToOneOfItsFieldsStaysReadable(string fragment)
    {
        Refused(fragment, ReadsMutable, "field 'In'");
    }

    [Theory]
    [InlineData("write-through-a-ref-local-into-an-owned-field")]
    [InlineData("write-through-a-ref-return-into-an-owned-field")]
    public void WriteThroughARefLocalOrARefReturnIntoAnOwnedSlotSkipsTheStoreRule(string fragment)
    {
        Refused(fragment, StoresUnowned, "'ext'");
    }

    [Fact]
    public void WriteThroughARefLocalIntoAnOwnedSlotFromAnUnmarkedScope()
    {
        Refused("unmarked-write-through-a-ref-local-into-an-owned-field", OwnedSourceNotOwned, "the owned slot 'r' refers to");
    }

    [Fact]
    public void ReadonlyClassWithAMutableSubclassCountsAsUnchangeable()
    {
        Refused("readonly-base-narrowed-to-a-mutable-subclass", Writes, "field 'V'");
    }

    [Fact]
    public void WithOwnedMethodReturningThisOnAnUnrestrictedAliasGivesAnExternalObjectAsOwn()
    {
        Refused("with-owned-return-of-this-through-an-unrestricted-alias", Writes, "field 'N'");
    }

    [Fact]
    public void OwnedFieldReadIntoAnUnrestrictedLocalSurvivesTheOwnersMove()
    {
        Refused("owned-field-read-into-a-local-then-parent-moved", OwnedSourceNotOwned, "its owned field 'Items' is also held by 'leak'");
    }

    [Fact]
    public void OwnedFieldReadIntoALocal_RestoredBeforeTheMove_IsAdmitted()
    {
        Admitted("owned-field-read-into-a-local-restored-before-the-move");
    }

    [Theory]
    [InlineData("owned-binding-stored-into-a-field-then-moved", "'shelf.Plain'")]
    [InlineData("owned-binding-stored-into-a-tuple-then-moved", "a tuple")]
    public void OwnedBindingStoredIntoAFieldOrATupleIsNotMoved(string fragment, string named)
    {
        Refused(fragment, OwnedSourceNotOwned, named);
    }

    [Fact]
    public void OwnedReturnOfAFreshLocalThatAlsoEscapedIntoAField()
    {
        Refused("owned-return-of-a-fresh-local-kept-by-a-field", OwnedReturnNotOwned, "local 'b'");
    }

    [Fact]
    public void StructTypedRefReturnTreatedAsAFreshCopy()
    {
        Refused("struct-ref-return-written-in-a-pure-method", Writes, "field 'X'");
    }

    [Fact]
    public void StructTypedRefReturnWrittenFromAReadonlyMember()
    {
        Refused("struct-ref-return-written-in-a-readonly-member", FieldAssignedInReadonlyMember, "'X'");
    }

    [Fact]
    public void EnumVariantAnsweredOwnAlthoughASharedStaticInstance()
    {
        Refused("enum-variant-of-a-class-holding-mutable-state", CallsImpure, "'Add'");
    }

    [Theory]
    [InlineData("frozen-local-reassigned", "field 'N'")]
    [InlineData("frozen-parameter-reassigned", "field 'N'")]
    [InlineData("params-array-reassigned", "an element of 'xs'")]
    public void FrozenBindingKeepsItsFrozenOriginAfterReassignment(string fragment, string named)
    {
        Refused(fragment, ReadsMutable, named);
    }

    [Fact]
    public void ConstructionTrustedToBeFreshEvenWhenItsConstructorLeaksThis()
    {
        Refused("construction-whose-constructor-hands-out-this", OwnedSourceNotOwned, "owned binding 'x'");
    }

    [Fact]
    public void ConstructionWhoseConstructorKeepsThisToItself_IsFresh()
    {
        Admitted("construction-whose-constructor-keeps-this-to-itself");
    }

    [Theory]
    [InlineData("CopyTo")]
    [InlineData("ToLower")]
    public void ExtensionOnAnIntrinsicTypeShadowedByAHiddenClrInstanceMember(string name)
    {
        Assert.Contains($"Probe.ListExtensions_Probe_0.{name}(l)", new PurityFragments().EmittedExtensionCall(name), StringComparison.Ordinal);
    }

    [Fact]
    public void ExtensionTheObjectsOwnMemberAnswers_KeepsTheInstanceSpelling()
    {
        Assert.Contains("l.Equals(1)", new PurityFragments().EmittedExtensionCall("Equals"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("constructor", "((Shape)this)")]
    [InlineData("indexer", "((Shape)this)")]
    [InlineData("operator", "((Shape)this)")]
    public void RestatedAbstractDefaultReResolvesThisInConstructorIndexerAndOperatorPositions(string position, string pinned)
    {
        Assert.Contains(pinned, new PurityFragments().EmittedPinnedThis(position), StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenedStructBodyReResolvesThisInAUserImplicitConversion()
    {
        Assert.Contains("(Point)(this)", new PurityFragments().EmittedPinnedThis("conversion"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("static-computed-property-of-a-noisy-type")]
    [InlineData("user-operator-of-a-noisy-type")]
    [InlineData("user-conversion-of-a-noisy-type")]
    public void StaticConstructorRunUnjudgedThroughAComputedPropertyAnOperatorAndAConversion(string fragment)
    {
        Refused(fragment, CallsImpure, "static initializers of 'Noisy'");
    }

    [Fact]
    public void StaticConstructorRunUnjudgedThroughAMethodGroup()
    {
        Refused("static-method-group-of-a-noisy-type-to-a-pure-function-type", PureFunctionRequired, "static initializers of 'Noisy'");
    }

    [Fact]
    public void ReadingAStaticComputedPropertyOfATypeWithAStaticConstructor()
    {
        Refused("static-computed-property-of-a-noisy-type", CallsImpure, "read 'Seed'");
    }

    [Fact]
    public void DefaultOfATypeRunsItsStaticConstructorUnjudged()
    {
        Refused("default-of-a-type-with-a-noisy-static-constructor", CallsImpure, "take the default of 'Noisy'");
    }

    [Fact]
    public void DefaultOfADefaultConstrainedTypeParameterRunsTheArgumentsConstructorUnjudged()
    {
        Refused("default-of-a-default-constrained-type-parameter", CallsImpure, "default of 'T'");
    }

    [Fact]
    public void ReadonlyDefaultOfADefaultConstrainedTypeParameter()
    {
        Refused("readonly-default-of-a-default-constrained-type-parameter", ReadonlyAllocatesMutable, "'T'");
    }

    [Fact]
    public void PureStaticMemberOfAGenericTypeRunsAnotherInstantiationsStaticConstructor()
    {
        Refused("static-call-to-another-instantiation-of-the-scopes-generic-type", CallsImpure, "static initializers of 'Noisy'");
    }

    [Fact]
    public void StaticCallToTheScopesOwnInstantiation_HasAlreadyRunItsInitializers()
    {
        Admitted("static-call-to-the-scopes-own-instantiation");
    }

    [Fact]
    public void EnumStaticInitializersRunByAConversionFromTheUnderlyingValue()
    {
        Refused("enum-looked-up-from-its-underlying-value", CallsImpure, "static initializers of 'Level'");
    }

    [Fact]
    public void EnumVariantInAPatternCarriesTheVariantItNames()
    {
        var bound = Assert.IsType<MemberAccessExpression>(PurityFragments.BoundVariantPattern());

        _ = Assert.IsType<EnumMemberResult>(bound.Member);
    }

    [Fact]
    public void UserImplicitConversionToBoolInAConditionIsRecorded()
    {
        var condition = Assert.IsType<ImplicitConversionExpression>(PurityFragments.BoundCondition());

        Assert.NotNull(condition.Operator);
    }

    [Fact]
    public void UserImplicitConversionToIntInAnArrayCreationSizeIsRecorded()
    {
        var size = Assert.IsType<ImplicitConversionExpression>(PurityFragments.BoundArraySize());

        Assert.NotNull(size.Operator);
    }

    [Fact]
    public void PureMethodGroupWithAContextClauseConvertsToAPureFunctionType()
    {
        Refused("method-group-with-a-context-clause-to-a-pure-function-type", PureFunctionRequired, "the ambient");
    }

    [Fact]
    public void ClrInterfaceSlotWithNoMistImplementerIsInferredPure()
    {
        Refused("clr-interface-slot-with-no-mist-implementer", CallsImpure, "'Add'");
    }

    [Fact]
    public void ListOfAnOptionalStructContainsRunsItsIEquatableUnjudged()
    {
        Refused("list-of-optional-struct-compared-through-its-iequatable", CallsImpure, "'Equals'");
    }

    [Fact]
    public void DeconstructionIntoAFreshLocalAliasesAnExternalArgument()
    {
        Refused("deconstruction-into-a-fresh-local-from-an-input", Writes, "field 'N'");
    }

    [Fact]
    public void PureMethodWritesAFrozenInputThroughADeconstructionTarget()
    {
        Refused("deconstruction-into-a-field-of-a-frozen-input", Writes, "field 'N'");
    }

    [Fact]
    public void PureLambdaWritesACapturedVariableThroughDeconstruction()
    {
        Refused("deconstruction-into-a-captured-variable-of-a-pure-lambda", Writes, "captured variable 'n'");
    }

    [Fact]
    public void CapturedVariableReadByAPureFunctionValueIsReassignedThroughDeconstruction()
    {
        Refused("deconstruction-reassigning-a-captured-variable-after-the-capture", ReadsMutable, "captured variable 'n'");
    }

    [Fact]
    public void PureMethodRunsAHeldLambdaSwappedByDeconstruction()
    {
        Refused("deconstruction-swapping-a-held-lambda", CallsImpure, "function value");
    }

    [Fact]
    public void ReadonlyMemberWritesItsOwnFieldThroughDeconstruction()
    {
        Refused("deconstruction-into-a-field-from-a-readonly-member", FieldAssignedInReadonlyMember, "'X'");
    }

    [Fact]
    public void ReadonlyInferenceGrantsConstructorPrivilegeForAnotherInstancesField()
    {
        Refused("constructor-writing-another-instances-field-called-from-a-readonly-member", ReadonlyAllocatesMutable, "'Cell'");
    }

    [Fact]
    public void ReadonlyMemberInterpolatesAMistIFormattableThatWrites()
    {
        Refused("readonly-member-interpolating-a-mist-iformattable", ReadonlyCallsMutable, "'ToString'");
    }

    [Fact]
    public void ReadonlyMemberReadingAStaticFieldRunsAnImpureStaticInitializer()
    {
        Refused("readonly-member-reading-a-static-with-a-noisy-initializer", ReadonlyCallsMutable, "static initializers of Noisy");
    }

    [Fact]
    public void DeclaredReadonlyMemberInvokesANonPureFunctionValue()
    {
        Refused("readonly-member-calling-a-plain-function-value", ReadonlyCallsMutable, "'f'");
    }

    [Fact]
    public void DeclaredReadonlyMemberInvokingAPureFunctionValue_IsAdmitted()
    {
        Admitted("readonly-member-calling-a-pure-function-value");
    }

    [Fact]
    public void ReadonlyEmissionOracleSpellsReadonlyOnAStructMethodAssigningThis()
    {
        Assert.False(new PurityFragments().UnmarkedStructMemberAssigningThisIsInferredReadonly());
    }

    [Theory]
    [InlineData("readonly-struct-member-assigning-this", ReadonlyWritesThroughRef)]
    [InlineData("pure-struct-member-assigning-this", Writes)]
    public void DeclaredStructMemberAssigningThis(string fragment, string expected)
    {
        Refused(fragment, expected, "'this'");
    }

    [Fact]
    public void LocalStructMemberAssigningThis_WritesItsOwnInstance()
    {
        Admitted("local-struct-member-assigning-this");
    }

    private static void Refused(string fragment, string expected, string named)
    {
        var finding = Assert.Single(new PurityFragments().RoundFive(fragment));

        Assert.True(finding.Id.Value == expected, $"{fragment}: expected {expected}, got {finding.Id.Value}: {finding.Message}");
        Assert.Contains(named, finding.Message, StringComparison.Ordinal);
    }

    private static void Admitted(string fragment)
    {
        var findings = new PurityFragments().RoundFive(fragment);

        Assert.True(findings.Count == 0, $"{fragment}: {string.Join("; ", findings.Select(finding => finding.Message))}");
    }
}