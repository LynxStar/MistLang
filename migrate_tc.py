#!/usr/bin/env python3
"""
Migration script for TypeCheckerAnalyzer.cs
Replaces MakeDiagnostic/MakeDiagnosticWarning/MakeDiagnosticWithSeverity calls
with diagnostics.ReportAtNode(TypeCheckDiagnostics.*, node, "TypeChecker")
"""
import re, sys

FILE = "Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs"
with open(FILE, "r") as f:
    content = f.read()


# Map diagnostic ID strings to TypeCheckDiagnostics field names
ID_TO_DESCRIPTOR = {
    "MIST_TC_0031": "TypeCheckDiagnostics.NoApplicableOverload",
    "MIST_TC_0032": "TypeCheckDiagnostics.AmbiguousOverload",
    "MIST_TC_0040": "TypeCheckDiagnostics.CastOperatorEnclosingTypeViolation",
    "MIST_TC_0041": "TypeCheckDiagnostics.DuplicateCastOperator",
    "MIST_TC_0042": "TypeCheckDiagnostics.NoCastOperator",
    "MIST_TC_0043": "TypeCheckDiagnostics.AmbiguousCast",
    "MIST_TC_0044": "TypeCheckDiagnostics.AmbiguousImplicitCast",
    "MIST_TC_0050": "TypeCheckDiagnostics.PatternUndefinedType",
    "MIST_TC_0051": "TypeCheckDiagnostics.PatternConstantTypeMismatch",
    "MIST_TC_0052": "TypeCheckDiagnostics.PatternRelationalNonComparable",
    "MIST_TC_0053": "TypeCheckDiagnostics.PatternUndefinedProperty",
    "MIST_TC_0054": "TypeCheckDiagnostics.PatternListRequiresArray",
    "MIST_TC_0055": "TypeCheckDiagnostics.PatternListTooManyElements",
    "MIST_TC_0056": "TypeCheckDiagnostics.PatternOrInconsistentVariables",
    "MIST_TC_0057": "TypeCheckDiagnostics.PatternNotCannotBindVariable",
    "MIST_TC_0058": "TypeCheckDiagnostics.StructGenericUnknownWidening",
    "MIST_TC_0060": "TypeCheckDiagnostics.UnknownToNonUnknown",
    "MIST_TC_0061": "TypeCheckDiagnostics.VoidToUnknown",
    "MIST_TC_0063": "TypeCheckDiagnostics.PatternNarrowToUnknown",
    "MIST_TC_0064": "TypeCheckDiagnostics.PatternNarrowToClosedGenericWithUnknown",
    "MIST_TC_0065": "TypeCheckDiagnostics.BareUnknownMemberAccess",
    "MIST_TC_0066": "TypeCheckDiagnostics.BareUnknownOperator",
    "MIST_TC_0067": "TypeCheckDiagnostics.NoCastOperatorFromUnknown",
    "MIST_TC_0070": "TypeCheckDiagnostics.UnionNonConstituentAssignment",
    "MIST_TC_0071": "TypeCheckDiagnostics.UnionToNonUnionAssignment",
    "MIST_TC_0072": "TypeCheckDiagnostics.UnionIncompatibleConstituents",
    "MIST_TC_0073": "TypeCheckDiagnostics.UnionMemberNotOnAllConstituents",
    "MIST_TC_0074": "TypeCheckDiagnostics.UnionPatternNonConstituent",
    "MIST_TC_0075": "TypeCheckDiagnostics.UnionDegenerateType",
    "MIST_TC_0077": "TypeCheckDiagnostics.ConstraintReadonlyViolation",
    "MIST_TC_0078": "TypeCheckDiagnostics.ConstraintMutableViolation",
    "MIST_TC_0079": "TypeCheckDiagnostics.ReadonlyContractViolation",
    "MIST_TC_0090": "TypeCheckDiagnostics.SwitchNotExhaustive",
    "MIST_TC_0091": "TypeCheckDiagnostics.SwitchIncompatibleArmTypes",
    "MIST_TC_0092": "TypeCheckDiagnostics.SwitchGuardNotBool",
    "MIST_TC_0093": "TypeCheckDiagnostics.SwitchUnreachableArm",
    "MIST_TC_0094": "TypeCheckDiagnostics.DelegateOverloadAmbiguity",
    "MIST_TC_0095": "TypeCheckDiagnostics.ArrayLiteralIncompatibleTypes",
    "MIST_TC_0097": "TypeCheckDiagnostics.RefinementLiteralViolation",
    "MIST_TC_0098": "TypeCheckDiagnostics.RefinementNonConstantAssignment",
    "MIST_TC_0099": "TypeCheckDiagnostics.RefinementSubsetNotProvable",
    "MIST_TC_0100": "TypeCheckDiagnostics.RefinementMutableMember",
    "MIST_TC_0101": "TypeCheckDiagnostics.PatternIncompatibleRefinement",
    "MIST_TC_0102": "TypeCheckDiagnostics.EnumBaseTypeNotReadonly",
    "MIST_TC_0103": "TypeCheckDiagnostics.EnumUnionConstituentNotReadonly",
    "MIST_TC_0104": "TypeCheckDiagnostics.AsyncIntrinsicIncompatible",
    "MIST_TC_0105": "TypeCheckDiagnostics.AwaitNonTaskOperand",
    "MIST_TC_0106": "TypeCheckDiagnostics.AsyncExplicitTaskReturnType",
    "MIST_TC_0107": "TypeCheckDiagnostics.AsyncFunctorNonInvocationOperand",
    "MIST_TC_0108": "TypeCheckDiagnostics.AsyncOnConstructorOrOperator",
    "MIST_TC_0109": "TypeCheckDiagnostics.AsyncFunctorDoubleWrap",
    "MIST_TC_0111": "TypeCheckDiagnostics.OptionalChainingRequiresOptionalType",
    "MIST_TC_0113": "TypeCheckDiagnostics.TuplePositionalPatternRequiresTupleType",
    "MIST_TC_0114": "TypeCheckDiagnostics.TuplePositionalPatternArityMismatch",
    "MIST_TC_0115": "TypeCheckDiagnostics.MixedLambdaParameterTypes",
    "MIST_TC_0116": "TypeCheckDiagnostics.CannotInferLambdaParameterTypes",
    "MIST_TC_0117": "TypeCheckDiagnostics.InconsistentLambdaReturnTypes",
    "MIST_TC_0118": "TypeCheckDiagnostics.MissingReturnInNonVoidLambda",
    "MIST_TC_0119": "TypeCheckDiagnostics.FuncInvocationArgumentCountMismatch",
    "MIST_TC_0120": "TypeCheckDiagnostics.FuncInvocationArgumentTypeMismatch",
    "MIST_TC_0121": "TypeCheckDiagnostics.AmbiguousMethodGroupConversion",
    "MIST_TC_0122": "TypeCheckDiagnostics.StaticLambdaCaptureViolation",
    "MIST_TC_0123": "TypeCheckDiagnostics.CaptureRefStruct",
    "MIST_TC_0124": "TypeCheckDiagnostics.CaptureOwned",
    "MIST_TC_0125": "TypeCheckDiagnostics.CaptureBorrowedOrFrozen",
    "MIST_TC_0126": "TypeCheckDiagnostics.AwaitInNonAsyncLambda",
    "MIST_TC_0127": "TypeCheckDiagnostics.ConstraintOwnedViolation",
    "MIST_TC_0128": "TypeCheckDiagnostics.BaseDelegateNotInvocable",
    "MIST_TC_0129": "TypeCheckDiagnostics.DelegateSignatureMismatch",
    "MIST_TC_0130": "TypeCheckDiagnostics.DelegateNominalMismatch",
    "MIST_TC_0131": "TypeCheckDiagnostics.DelegateCannotNarrowBaseDelegate",
    "MIST_TC_0132": "TypeCheckDiagnostics.MethodGroupNoMatchingOverload",
    "MIST_TC_0133": "TypeCheckDiagnostics.MethodGroupMultipleMatchingOverloads",
    "MIST_TC_0160": "TypeCheckDiagnostics.AsOperatorNoRelationship",
    "MIST_TC_0161": "TypeCheckDiagnostics.AsOperatorVoidTarget",
    "MIST_TC_0162": "TypeCheckDiagnostics.AsOperatorVoidSource",
    "MIST_TC_REGEX_INVALID": "TypeCheckDiagnostics.RegexPatternInvalid",
    "MIST_TC_REGEX_SCRUTINEE": "TypeCheckDiagnostics.RegexScrutineeNotString",
    "MIST_TC_REGEX_REFINEMENT": "TypeCheckDiagnostics.RegexRefinementOnNonString",
    "MIST_CONSTRAINT_0009": "TypeCheckDiagnostics.ConstraintAbstractNotImplemented",
    "MIST_CONSTRAINT_0011": "TypeCheckDiagnostics.ConstraintBaseClassNotDerived",
    "MIST_CONSTRAINT_0012": "TypeCheckDiagnostics.ConstraintClassViolation",
    "MIST_CONSTRAINT_0013": "TypeCheckDiagnostics.ConstraintStructViolation",
    "MIST_CONSTRAINT_0014": "TypeCheckDiagnostics.ConstraintParameterlessCtorViolation",
    "MIST_CONSTRAINT_0015": "TypeCheckDiagnostics.ConstraintParameterizedCtorViolation",
    "MIST_CONSTRAINT_0016": "TypeCheckDiagnostics.ConstraintNewTNoMatch",
    "MIST_CONSTRAINT_0017": "TypeCheckDiagnostics.ConstraintPropagationInsufficient",
    "MIST_CONSTRAINT_0018": "TypeCheckDiagnostics.ConstraintSubstructuralViolation",
    "MIST_PI_0001": "TypeCheckDiagnostics.PropertyInitializerMemberNotFound",
    "MIST_PI_0002": "TypeCheckDiagnostics.PropertyInitializerTypeMismatch",
    "MIST_PI_0003": "TypeCheckDiagnostics.PropertyInitializerNoSetOrInit",
    "MIST_PI_0004": "TypeCheckDiagnostics.PropertyInitializerReadonlyField",
    "MIST_PI_0005": "TypeCheckDiagnostics.PropertyInitializerInaccessible",
    "MIST_PI_0006": "TypeCheckDiagnostics.PropertyInitializerDuplicate",
    # New descriptors added in this migration
    "MIST_TC_0001": "TypeCheckDiagnostics.ImplicitConversionNotExists",
    "MIST_TC_0002": "TypeCheckDiagnostics.ExpressionBodyTypeMismatch",
    "MIST_TC_0007": "TypeCheckDiagnostics.ConditionNotBool",
    "MIST_TC_0008": "TypeCheckDiagnostics.ReturnValueInVoidMethod",
    "MIST_TC_0009": "TypeCheckDiagnostics.VarRequiresInitializer",
    "MIST_TC_0010": "TypeCheckDiagnostics.IndexerParameterTypeMismatch",
    "MIST_TC_0011": "TypeCheckDiagnostics.ArrayCtorRequiresOneArg",
    "MIST_TC_0012": "TypeCheckDiagnostics.ArraySizeNotInt",
    "MIST_TC_0013": "TypeCheckDiagnostics.NoMatchingConstructor",
    "MIST_TC_0014": "TypeCheckDiagnostics.MemberInaccessible",
    "MIST_TC_0015": "TypeCheckDiagnostics.StaticMemberViaInstance",
    "MIST_TC_0016": "TypeCheckDiagnostics.InstanceMemberViaType",
    "MIST_TC_0018": "TypeCheckDiagnostics.MultipleEntryPoints",
    "MIST_TC_0019": "TypeCheckDiagnostics.PropertyNoGetAccessor",
    "MIST_TC_0020": "TypeCheckDiagnostics.PropertyNoSetAccessor",
    "MIST_TC_0021": "TypeCheckDiagnostics.PropertyAssignmentTypeMismatch",
    "MIST_TC_0022": "TypeCheckDiagnostics.AccessorModifierNotRestrictive",
    "MIST_TC_0023": "TypeCheckDiagnostics.FieldKeywordOutsideAccessor",
    "MIST_TC_0024": "TypeCheckDiagnostics.FieldKeywordInComputedProperty",
    "MIST_TC_0025": "TypeCheckDiagnostics.InitOnlyPropertyOutsideCtor",
    "MIST_TC_0026": "TypeCheckDiagnostics.ThisInStaticAccessor",
    "MIST_TC_0027": "TypeCheckDiagnostics.StaticPropertyViaInstance",
    "MIST_TC_0028": "TypeCheckDiagnostics.PropertyBodyTypeMismatch",
    "MIST_TC_0029": "TypeCheckDiagnostics.DuplicateIndexer",
    "MIST_TC_0030": "TypeCheckDiagnostics.MissingAbstractPropertyImpl",
    "MIST_TC_0034": "CommonDiagnostics.VoidArrayNotPermitted",
    "MIST_TC_0047": "TypeCheckDiagnostics.VoidCoalescingNotStatement",
    "MIST_TC_0062": "TypeCheckDiagnostics.TupleDeconstructionTypeMismatch",
    "MIST_TC_0080": "TypeCheckDiagnostics.TypeInferenceFailed",
    "MIST_TC_0081": "TypeCheckDiagnostics.TypeInferenceConflict",
    "MIST_TC_0096": "TypeCheckDiagnostics.EmptyArrayLiteralNoType",
    "MIST_TC_0150": "TypeCheckDiagnostics.StackallocAssignmentMismatch",
    "MIST_TC_0151": "TypeCheckDiagnostics.StackallocSizeNotInt",
    "MIST_TC_0154": "TypeCheckDiagnostics.StackallocRefStructElement",
    "MIST_TC_0156": "TypeCheckDiagnostics.ReadOnlySpanIndexerAssignment",
    "MIST_TC_0157": "TypeCheckDiagnostics.SpanKindMismatch",
    "MIST_TC_FOREACH_NOT_ENUMERABLE": "TypeCheckDiagnostics.ForeachNotEnumerable",
    "MIST_TC_FOREACH_TYPE_MISMATCH": "TypeCheckDiagnostics.ForeachTypeMismatch",
    "MIST2002": "TypeCheckDiagnostics.RefStructFieldRestriction",
    "MIST2003": "TypeCheckDiagnostics.RefStructPropertyRestriction",
    "MIST2004": "TypeCheckDiagnostics.RefStructGenericArgRestriction",
    "MIST2007": "TypeCheckDiagnostics.RefStructToAbstract",
    "MIST_ABSTRACT_INSTANTIATION": "TypeCheckDiagnostics.AbstractInstantiation",
    "MIST_ANON_EQUALITY_MISMATCH": "TypeCheckDiagnostics.AnonEqualityMismatch",
    "MIST_ANON_ESCAPE_FIELD": "TypeCheckDiagnostics.AnonEscapeField",
    "MIST_ANON_ESCAPE_GENERIC": "TypeCheckDiagnostics.AnonEscapeGeneric",
    "MIST_ANON_ESCAPE_SIGNATURE": "TypeCheckDiagnostics.AnonEscapeSignature",
    "MIST_ANON_READONLY": "TypeCheckDiagnostics.AnonReadonly",
    "MIST_READONLY_LOCAL_0001": "TypeCheckDiagnostics.ReadonlyLocalReassignment",
    "MIST_REF_0001": "TypeCheckDiagnostics.RefArgNotStorageLocation",
    "MIST_REF_0002": "TypeCheckDiagnostics.RefKeywordMissing",
    "MIST_REF_0003": "TypeCheckDiagnostics.RefKeywordOnNonRefParam",
    "MIST_REF_0005": "TypeCheckDiagnostics.RefReadonlyAssignment",
    "MIST_REF_0006": "TypeCheckDiagnostics.RefReturnLocalEscape",
    "MIST_REF_0007": "TypeCheckDiagnostics.RefArgTypeMismatch",
    "MIST_REF_0008": "TypeCheckDiagnostics.RefStoredInField",
    "MIST_REF_0009": "TypeCheckDiagnostics.RefReadonlyReturnAssignment",
    "MIST_REF_0010": "TypeCheckDiagnostics.RefLocalMustInit",
    "MIST_STRUCT_BASE_CTOR_MISMATCH": "TypeCheckDiagnostics.StructBaseCtorMismatch",
    "MIST_UNDEFINED_MEMBER": "TypeCheckDiagnostics.UndefinedMember",
    "MIST_SB_0083": "TypeCheckDiagnostics.CannotAssignUnderlyingToEnum",
    "MIST_REFINEMENT_0004": "TypeCheckDiagnostics.RefinementConstraintViolation",
    "MIST_REFINEMENT_0005": "TypeCheckDiagnostics.ParameterRefinementViolation",
    "MIST_REFINEMENT_0006": "TypeCheckDiagnostics.RefinementImplicationFailure",
    "MIST_REFINEMENT_0007": "TypeCheckDiagnostics.RefinementPredicateIncompatible",
    "MIST_REFINEMENT_0008": "TypeCheckDiagnostics.RefinementUnconstrainedForwarding",
    "MIST_DT_0002": "TypeCheckDiagnostics.DependentArgNotConstant",
    "MIST_DT_0003": "TypeCheckDiagnostics.DependentArgTypeMismatch",
    "MIST_DT_0004": "TypeCheckDiagnostics.DependentNarrowingViolation",
    "MIST_DT_0007": "TypeCheckDiagnostics.DependentOpenTypePattern",
}

# Map local field names to their IDs (for FieldName.Value patterns)
LOCAL_FIELD_TO_ID = {
    "NoApplicableOverload": "MIST_TC_0031",
    "AmbiguousOverload": "MIST_TC_0032",
    "CastOperatorEnclosingTypeViolation": "MIST_TC_0040",
    "DuplicateCastOperator": "MIST_TC_0041",
    "NoCastOperator": "MIST_TC_0042",
    "AmbiguousCast": "MIST_TC_0043",
    "AmbiguousImplicitCast": "MIST_TC_0044",
    "PatternUndefinedType": "MIST_TC_0050",
    "PatternConstantTypeMismatch": "MIST_TC_0051",
    "PatternRelationalNonComparable": "MIST_TC_0052",
    "PatternUndefinedProperty": "MIST_TC_0053",
    "PatternListRequiresArray": "MIST_TC_0054",
    "PatternListTooManyElements": "MIST_TC_0055",
    "PatternOrInconsistentVariables": "MIST_TC_0056",
    "PatternNotCannotBindVariable": "MIST_TC_0057",
    "StructGenericUnknownWidening": "MIST_TC_0058",
    "UnknownToNonUnknown": "MIST_TC_0060",
    "VoidToUnknown": "MIST_TC_0061",
    "PatternNarrowToUnknown": "MIST_TC_0063",
    "PatternNarrowToClosedGenericWithUnknown": "MIST_TC_0064",
    "BareUnknownMemberAccess": "MIST_TC_0065",
    "BareUnknownOperator": "MIST_TC_0066",
    "NoCastOperatorFromUnknown": "MIST_TC_0067",
    "UnionNonConstituentAssignment": "MIST_TC_0070",
    "UnionToNonUnionAssignment": "MIST_TC_0071",
    "UnionIncompatibleConstituents": "MIST_TC_0072",
    "UnionMemberNotOnAllConstituents": "MIST_TC_0073",
    "UnionPatternNonConstituent": "MIST_TC_0074",
    "UnionDegenerateType": "MIST_TC_0075",
    "ConstraintReadonlyViolation": "MIST_TC_0077",
    "ConstraintMutableViolation": "MIST_TC_0078",
    "ReadonlyContractViolation": "MIST_TC_0079",
    "SwitchNotExhaustive": "MIST_TC_0090",
    "SwitchIncompatibleArmTypes": "MIST_TC_0091",
    "SwitchGuardNotBool": "MIST_TC_0092",
    "SwitchUnreachableArm": "MIST_TC_0093",
    "DelegateOverloadAmbiguity": "MIST_TC_0094",
    "ArrayLiteralIncompatibleTypes": "MIST_TC_0095",
    "RefinementLiteralViolation": "MIST_TC_0097",
    "RefinementNonConstantAssignment": "MIST_TC_0098",
    "RefinementSubsetNotProvable": "MIST_TC_0099",
    "RefinementMutableMember": "MIST_TC_0100",
    "PatternIncompatibleRefinement": "MIST_TC_0101",
    "EnumBaseTypeNotReadonly": "MIST_TC_0102",
    "EnumUnionConstituentNotReadonly": "MIST_TC_0103",
    "AsyncIntrinsicIncompatible": "MIST_TC_0104",
    "AwaitNonTaskOperand": "MIST_TC_0105",
    "AsyncExplicitTaskReturnType": "MIST_TC_0106",
    "AsyncFunctorNonInvocationOperand": "MIST_TC_0107",
    "AsyncOnConstructorOrOperator": "MIST_TC_0108",
    "AsyncFunctorDoubleWrap": "MIST_TC_0109",
    "OptionalChainingRequiresOptionalType": "MIST_TC_0111",
    "TuplePositionalPatternRequiresTupleType": "MIST_TC_0113",
    "TuplePositionalPatternArityMismatch": "MIST_TC_0114",
    "MixedLambdaParameterTypes": "MIST_TC_0115",
    "CannotInferLambdaParameterTypes": "MIST_TC_0116",
    "InconsistentLambdaReturnTypes": "MIST_TC_0117",
    "MissingReturnInNonVoidLambda": "MIST_TC_0118",
    "FuncInvocationArgumentCountMismatch": "MIST_TC_0119",
    "FuncInvocationArgumentTypeMismatch": "MIST_TC_0120",
    "AmbiguousMethodGroupConversion": "MIST_TC_0121",
    "StaticLambdaCaptureViolation": "MIST_TC_0122",
    "CaptureRefStruct": "MIST_TC_0123",
    "CaptureOwned": "MIST_TC_0124",
    "CaptureBorrowedOrFrozen": "MIST_TC_0125",
    "AwaitInNonAsyncLambda": "MIST_TC_0126",
    "ConstraintOwnedViolation": "MIST_TC_0127",
    "BaseDelegateNotInvocable": "MIST_TC_0128",
    "DelegateSignatureMismatch": "MIST_TC_0129",
    "DelegateNominalMismatch": "MIST_TC_0130",
    "DelegateCannotNarrowBaseDelegate": "MIST_TC_0131",
    "MethodGroupNoMatchingOverload": "MIST_TC_0132",
    "MethodGroupMultipleMatchingOverloads": "MIST_TC_0133",
    "AsOperatorNoRelationship": "MIST_TC_0160",
    "AsOperatorVoidTarget": "MIST_TC_0161",
    "AsOperatorVoidSource": "MIST_TC_0162",
    "RegexPatternInvalid": "MIST_TC_REGEX_INVALID",
    "RegexScrutineeNotString": "MIST_TC_REGEX_SCRUTINEE",
    "RegexRefinementOnNonString": "MIST_TC_REGEX_REFINEMENT",
    "ConstraintAbstractNotImplemented": "MIST_CONSTRAINT_0009",
    "ConstraintBaseClassNotDerived": "MIST_CONSTRAINT_0011",
    "ConstraintClassViolation": "MIST_CONSTRAINT_0012",
    "ConstraintStructViolation": "MIST_CONSTRAINT_0013",
    "ConstraintParameterlessCtorViolation": "MIST_CONSTRAINT_0014",
    "ConstraintParameterizedCtorViolation": "MIST_CONSTRAINT_0015",
    "ConstraintNewTNoMatch": "MIST_CONSTRAINT_0016",
    "ConstraintPropagationInsufficient": "MIST_CONSTRAINT_0017",
    "ConstraintSubstructuralViolation": "MIST_CONSTRAINT_0018",
    "PropertyInitializerMemberNotFound": "MIST_PI_0001",
    "PropertyInitializerTypeMismatch": "MIST_PI_0002",
    "PropertyInitializerNoSetOrInit": "MIST_PI_0003",
    "PropertyInitializerReadonlyField": "MIST_PI_0004",
    "PropertyInitializerInaccessible": "MIST_PI_0005",
    "PropertyInitializerDuplicate": "MIST_PI_0006",
    "DependentArgNotConstant": "MIST_DT_0002",
    "DependentArgTypeMismatch": "MIST_DT_0003",
    "DependentNarrowingViolation": "MIST_DT_0004",
    "DependentOpenTypePattern": "MIST_DT_0007",
    "RefinementConstraintViolation": "MIST_REFINEMENT_0004",
    "ParameterRefinementViolation": "MIST_REFINEMENT_0005",
    "RefinementImplicationFailure": "MIST_REFINEMENT_0006",
    "RefinementPredicateIncompatible": "MIST_REFINEMENT_0007",
    "RefinementUnconstrainedForwarding": "MIST_REFINEMENT_0008",
    "CannotAssignUnderlyingToEnum": "MIST_SB_0083",
}


# Map from DiagnosticId.X static field references to CommonDiagnostics descriptors
DIAG_ID_FIELD_TO_DESCRIPTOR = {
    "DiagnosticId.DefaultNotSupported": "CommonDiagnostics.DefaultNotSupported",
    "DiagnosticId.DefaultFieldNotVisible": "CommonDiagnostics.DefaultFieldNotVisible",
    "DiagnosticId.DefaultNotConstant": "CommonDiagnostics.DefaultNotConstant",
    "DiagnosticId.DefaultUndefinedType": "CommonDiagnostics.DefaultUndefinedType",
    "DiagnosticId.DefaultOnAbstract": "CommonDiagnostics.DefaultOnAbstract",
    "DiagnosticId.DefaultOnTypeParameter": "CommonDiagnostics.DefaultOnTypeParameter",
    "DiagnosticId.DefaultCannotInferType": "CommonDiagnostics.DefaultCannotInferType",
    "DiagnosticId.DefaultRedundantBlock": "CommonDiagnostics.DefaultRedundantBlock",
    "DiagnosticId.FieldNotDefinitelyAssigned": "CommonDiagnostics.FieldNotDefinitelyAssigned",
    "DiagnosticId.FieldReadBeforeAssignment": "CommonDiagnostics.FieldReadBeforeAssignment",
    "DiagnosticId.LocalReadBeforeAssignment": "CommonDiagnostics.LocalReadBeforeAssignment",
    "DiagnosticId.ArrayRequiresInitializer": "CommonDiagnostics.ArrayRequiresInitializer",
    "DiagnosticId.DefaultConstraintNotSatisfied": "CommonDiagnostics.DefaultConstraintNotSatisfied",
    "DiagnosticId.TargetNewWithVar": "CommonDiagnostics.TargetNewWithVar",
    "DiagnosticId.TargetNewNoContext": "CommonDiagnostics.TargetNewNoContext",
    "DiagnosticId.TargetNewAbstract": "CommonDiagnostics.TargetNewAbstract",
}

def resolve_descriptor(id_or_field):
    """Resolve a diagnostic ID string or field.Value reference to a descriptor."""
    # Check if it's a DiagnosticId.X.Value reference
    if id_or_field.startswith("DiagnosticId."):
        key = id_or_field.replace(".Value", "")
        return DIAG_ID_FIELD_TO_DESCRIPTOR.get(key)
    # Check if it's a local field.Value reference
    field_name = id_or_field.replace(".Value", "")
    if field_name in LOCAL_FIELD_TO_ID:
        diag_id = LOCAL_FIELD_TO_ID[field_name]
        return ID_TO_DESCRIPTOR.get(diag_id)
    # Check if it's a raw ID string
    return ID_TO_DESCRIPTOR.get(id_or_field)


def find_matching_paren(text, start):
    """Find the position of the matching closing paren for an opening paren at start."""
    depth = 0
    i = start
    in_string = False
    string_char = None
    while i < len(text):
        c = text[i]
        if in_string:
            if c == '\\' and i + 1 < len(text):
                i += 2
                continue
            if c == string_char:
                in_string = False
        else:
            if c == '"':
                in_string = True
                string_char = '"'
            elif c == '(':
                depth += 1
            elif c == ')':
                depth -= 1
                if depth == 0:
                    return i
        i += 1
    return -1


def process_file(content):
    """Process the file content and replace all emission patterns."""
    # We'll work line by line but track multi-line patterns
    result = content

    # Pattern 1: diagnostics.Add(MakeDiagnostic(...));
    # Pattern 2: diagnostics.Add(MakeDiagnosticWarning(...));
    # Pattern 3: diagnostics.Add(MakeDiagnosticWithSeverity(...));
    #
    # All three share the shape:
    #   diagnostics.Add(MakeXXX(
    #       ID_EXPR,
    #       MESSAGE_EXPR,
    #       NODE_EXPR));
    # (MakeDiagnosticWithSeverity also has a severity arg at the end)

    # Find all occurrences and replace them
    replacements = []

    # Find MakeDiagnostic( occurrences (including Warning and WithSeverity variants)
    pattern = re.compile(r'(\s*)diagnostics\.Add\((MakeDiagnostic(?:Warning|WithSeverity)?)\(')

    pos = 0
    while True:
        m = pattern.search(result, pos)
        if not m:
            break

        indent = m.group(1)
        method_name = m.group(2)
        # Find the opening paren of MakeXXX(
        make_paren_start = m.end() - 1  # position of '(' after MakeDiagnostic
        # Find matching close paren for Add(
        add_paren_start = m.start() + len(m.group(1)) + len("diagnostics.Add(") - 1
        add_close = find_matching_paren(result, add_paren_start)
        if add_close == -1:
            pos = m.end()
            continue

        # The full expression is from m.start()+indent to add_close+2 (includes ");")
        full_start = m.start() + len(indent.replace('\n',''))
        # Actually let's get the whole statement including leading whitespace
        stmt_start = m.start()
        # Find the end of the statement (the ");")
        stmt_end = add_close + 1  # after the outer ")"
        # Look for the semicolon
        semi_pos = result.find(';', stmt_end)
        if semi_pos == -1 or semi_pos > stmt_end + 5:
            pos = m.end()
            continue
        stmt_end = semi_pos + 1

        # Extract the arguments to MakeDiagnostic
        make_close = find_matching_paren(result, make_paren_start)
        if make_close == -1:
            pos = m.end()
            continue

        args_str = result[make_paren_start+1:make_close]

        # Parse arguments: split by commas (respecting strings, parens)
        args = split_args(args_str)

        if len(args) < 3:
            pos = m.end()
            continue

        id_arg = args[0].strip()
        message_arg = args[1].strip()
        node_arg = args[2].strip()

        # For MakeDiagnosticWithSeverity, there's a 4th arg (severity)
        # but we don't need it since the descriptor handles severity

        # Resolve the descriptor
        descriptor = None
        if id_arg.startswith('"'):
            # Raw string literal ID
            raw_id = id_arg.strip('"')
            descriptor = resolve_descriptor(raw_id)
        elif '.Value' in id_arg:
            descriptor = resolve_descriptor(id_arg)

        if descriptor is None:
            print(f"WARNING: Could not resolve descriptor for {id_arg} at pos {m.start()}", file=sys.stderr)
            pos = m.end()
            continue

        # Build the replacement
        # Get the base indent (the whitespace before 'diagnostics')
        base_indent = indent.lstrip('\n')

        replacement = f"{base_indent}diagnostics.ReportAtNode({descriptor}, {node_arg}, \"TypeChecker\");"

        replacements.append((stmt_start, stmt_end, replacement))
        pos = stmt_end

    # Apply replacements in reverse order
    for start, end, replacement in reversed(replacements):
        result = result[:start] + replacement + result[end:]

    return result


def split_args(text):
    """Split comma-separated args respecting parens, strings, and nested structures."""
    args = []
    depth = 0
    current = []
    in_string = False
    in_verbatim = False
    i = 0
    while i < len(text):
        c = text[i]
        if in_string:
            current.append(c)
            if c == '\\' and not in_verbatim and i + 1 < len(text):
                current.append(text[i+1])
                i += 2
                continue
            if c == '"':
                if in_verbatim and i + 1 < len(text) and text[i+1] == '"':
                    current.append(text[i+1])
                    i += 2
                    continue
                in_string = False
                in_verbatim = False
        else:
            if c == '"':
                in_string = True
                if i > 0 and text[i-1] == '@':
                    in_verbatim = True
                current.append(c)
            elif c == '(' or c == '[' or c == '{':
                depth += 1
                current.append(c)
            elif c == ')' or c == ']' or c == '}':
                depth -= 1
                current.append(c)
            elif c == ',' and depth == 0:
                args.append(''.join(current))
                current = []
            else:
                current.append(c)
        i += 1
    if current:
        args.append(''.join(current))
    return args


result = process_file(content)

with open(FILE, "w") as f:
    f.write(result)

print(f"Done. File updated.", file=sys.stderr)
