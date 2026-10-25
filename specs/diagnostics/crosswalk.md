# Crosswalk: the reissue of every public diagnostic code

This table is the record of the one-time reissue required by `requirements.md` Requirement 15. The reissue commit is `08b951691c0a14cc252b8734c86a1a780e2f0f6b`; it was computed from the registry at the reissue revision `97569de18582fa91b2dafdfec0e3c8173ee93235` (its parent `efdaccc6582d311818599c151b5e567956996ea2` changes only `requirements.md`, so the registry is the same) and is the first revision carrying the new numbering, so no revision carries two schemes. The table lists every descriptor field present at the reissue revision (the last registry-bearing checkpoint before the reissue commit; the fields present at `af6a1f70` are all here, and a field added after it says so in its disposition), its old code, its disposition and, for a numbered field, its Number, Title and Category.

Numbers were minted in one pass, registry file by registry file in path order, in declaration order within each file, starting at `MIST0001`: `Descriptors/BackendDiagnostics.cs`, `CallableSignatureDiagnostics.cs`, `CommonDiagnostics.cs`, `DeclarationModifierDiagnostics.cs`, `DriverDiagnostics.cs`, `EntryPointDiagnostics.cs`, `InternalDiagnostics.cs`, `LexerDiagnostics.cs`, `ParserDiagnostics.cs`, the `ProgramAnalysisDiagnostics` partials (`.Context`, `.Extensions`, `.Properties`, `.Purity`, `.Struct`, then the main file), `ProgramModelDiagnostics.cs` and `ProgramModelDiagnostics.VariadicPositions.cs`. A Number carries no meaning. Fields that shared a code received one Number per template and declare each other as related Numbers.

Rows: 430 fields; 421 numbered, 7 retired, 1 held back, 1 merged. Frozen history (`git show 6d862f63:backlog/issues.yaml`, test-ledger output under `TestResults/`, Carbonek findings) keeps the old spellings; this table is how a reader translates them. A specification-only historical code with no live twin is marked `(historical)` where it is cited and never appears here.

| Field | Old code | Disposition | Number | Title | Category |
|---|---|---|---|---|---|
| `BackendDiagnostics.MissingIntrinsicEmissionTemplate` | `MIST_BE_0002` | numbered | `MIST0001` | Missing intrinsic emission template | BCK |
| `BackendDiagnostics.BuildError` | `MIST_BUILD_ERROR` | numbered | `MIST0002` | No C# sources to compile | BCK |
| `BackendDiagnostics.CSharpError` | `MIST_CS_ERROR` | numbered | `MIST0003` | C# compilation error | BCK |
| `BackendDiagnostics.ReferenceAssemblyConflict` | `MIST_REFERENCE_ASSEMBLY_CONFLICT` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0004` | Conflicting reference assembly versions | BCK |
| `BackendDiagnostics.ReferenceFrameworkMissing` | `MIST_REFERENCE_FRAMEWORK_MISSING` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0005` | Undeclared shared framework reference | BCK |
| `BackendDiagnostics.AssemblyLoadFailed` | `MIST_ASM_LOAD_FAILED` | numbered | `MIST0006` | Assembly load failed | DEP |
| `BackendDiagnostics.UnresolvedAmbientType` | `MIST_AMBIENT_UNRESOLVED` | retired, see nothing: the ambient provider reports no such condition | none |  |  |
| `BackendDiagnostics.UnsupportedStatement` | `MIST_BE_UNSUPPORTED_STATEMENT` | numbered | `MIST0007` | Unsupported statement | BCK |
| `BackendDiagnostics.UnsupportedExpression` | `MIST_BE_UNSUPPORTED_EXPRESSION` | numbered | `MIST0008` | Unsupported expression | BCK |
| `BackendDiagnostics.ReadonlyRefReturn` | `MIST_BE_READONLY_REF_RETURN` | numbered | `MIST0009` | Ambiguous readonly ref return | BCK |
| `BackendDiagnostics.UnsupportedUnionPattern` | `MIST_BE_UNSUPPORTED_UNION_PATTERN` | numbered | `MIST0010` | Unsupported union pattern | BCK |
| `BackendDiagnostics.UnsupportedEnumPattern` | `MIST_BE_UNSUPPORTED_ENUM_PATTERN` | numbered | `MIST0011` | Unsupported enum pattern combinator | BCK |
| `BackendDiagnostics.UnsupportedUnknownPropertyPattern` | `MIST_BE_UNSUPPORTED_UNKNOWN_PROPERTY_PATTERN` | numbered | `MIST0012` | Unsupported unknown property pattern | BCK |
| `BackendDiagnostics.UnsupportedSizedElementTest` | `MIST_BE_UNSUPPORTED_SIZED_ELEMENT_TEST` | numbered | `MIST0013` | Unsupported sized element test | BCK |
| `BackendDiagnostics.UnsupportedImplicitSlice` | `MIST_BE_UNSUPPORTED_IMPLICIT_SLICE` | numbered | `MIST0014` | Unsupported implicit slice | BCK |
| `BackendDiagnostics.StructInheritanceFieldCollision` | `MIST_BE_STRUCT_FIELD_COLLISION` | numbered | `MIST0015` | Inherited struct field collision | BCK |
| `BackendDiagnostics.ForeignConstructorConstraintClosure` | `MIST_BE_FOREIGN_CTOR_CONSTRAINT` | numbered | `MIST0016` | Foreign constructor constraint closure | BCK |
| `BackendDiagnostics.MethodGroupReachedEmission` | `MIST_INTERNAL_METHOD_GROUP_AT_EMISSION` | numbered | `MIST0017` | Method group reached emission | ICE |
| `BackendDiagnostics.IntrinsicStructBase` | `MIST_BE_INTRINSIC_STRUCT_BASE` | numbered | `MIST0018` | Intrinsic struct base | BCK |
| `BackendDiagnostics.ForeignConformance` | `MIST_BE_FOREIGN_CONFORMANCE` | numbered | `MIST0019` | Foreign type conformance | BCK |
| `BackendDiagnostics.UnboundedParameterizedTypeSlot` | `MIST_BE_UNBOUNDED_PARAMETERIZED_SLOT` | numbered | `MIST0020` | Unbounded parameterized type slot | BCK |
| `BackendDiagnostics.ParameterizedTypeSlotInParameter` | `MIST_BE_PARAMETERIZED_SLOT_PARAMETER` | numbered | `MIST0021` | Parameterized type slot in parameter | BCK |
| `BackendDiagnostics.UnboundRefinementPredicate` | `MIST_BE_REFINED_PREDICATE` | numbered | `MIST0022` | Unbound refinement predicate | BCK |
| `BackendDiagnostics.DependentTypeParameter` | `MIST_BE_DEPENDENT_TYPE_PARAMETER` | numbered | `MIST0023` | Dependent type parameter | BCK |
| `BackendDiagnostics.UnhoistedUnitCall` | `MIST_BE_UNHOISTED_UNIT_CALL` | numbered | `MIST0024` | Unhoisted unit call | BCK |
| `BackendDiagnostics.UnspecializedPack` | `MIST_BE_UNSPECIALIZED_PACK` | numbered | `MIST0025` | Unspecialized pack in signature | BCK |
| `BackendDiagnostics.UnionOverPackElement` | `MIST_BE_UNION_OVER_PACK_ELEMENT` | numbered | `MIST0026` | Union over pack element | BCK |
| `BackendDiagnostics.UnboundedSpecialization` | `MIST_BE_UNBOUNDED_SPECIALIZATION` | numbered | `MIST0027` | Unbounded union specialization | BCK |
| `BackendDiagnostics.DelegateCeilingExceeded` | `MIST_VAR_0010` | numbered | `MIST0028` | Delegate parameter ceiling exceeded | BCK |
| `CallableSignatureDiagnostics.DuplicateOperatorSignature` | `MIST_PA_DUPLICATE_OPERATOR_SIGNATURE` | numbered | `MIST0029` | Duplicate operator signature | DEC |
| `CallableSignatureDiagnostics.DuplicateCastSignature` | `MIST_PA_DUPLICATE_CAST_OPERATOR` | numbered | `MIST0030` | Duplicate cast operator | DEC |
| `CommonDiagnostics.StdlibParse` | `MIST_STDLIB_PARSE` | numbered | `MIST0031` | Standard library load failed | IOX |
| `CommonDiagnostics.SdkNotFound` | `MIST_SDK_NOT_FOUND` | numbered | `MIST0032` | SDK installation not found | DEP |
| `CommonDiagnostics.SdkNoFramework` | `MIST_SDK_NO_FRAMEWORK` | numbered | `MIST0033` | Target framework not in SDK | DEP |
| `CommonDiagnostics.NamespaceNotFound` | `MIST_NS_NOT_FOUND` | retired, see `UndefinedNamespace` | none |  |  |
| `CommonDiagnostics.TypeAmbiguous` | `MIST_TYPE_AMBIGUOUS` | retired, see `AmbiguousType` | none |  |  |
| `CommonDiagnostics.FrameworkReferenceNotInstalled` | `MIST_FWREF_001` | numbered | `MIST0034` | Framework reference not installed | DEP |
| `CommonDiagnostics.FrameworkReferenceUnknownName` | `MIST_FWREF_002` | numbered | `MIST0035` | Unknown shared framework | DEP |
| `CommonDiagnostics.PackageNoCompatibleTfm` | `MIST_PKG_001` | held back, no producer (csharp-interop-v2 Requirement 1 lead) | none |  |  |
| `CommonDiagnostics.PackageNotFound` | `MIST_PKG_002` | numbered | `MIST0036` | Package not found | DEP |
| `CommonDiagnostics.PackageNetworkError` | `MIST_PKG_003` | numbered | `MIST0037` | Package restore network failure | DEP |
| `CommonDiagnostics.DuplicateAssociatedTypeSlot` | `MIST_AT_0001` | numbered | `MIST0038` | Duplicate associated type slot | DEC |
| `CommonDiagnostics.ModifiersOnAssociatedTypeSlot` | `MIST_AT_0005` | numbered | `MIST0039` | Modifiers on associated type slot | DEC |
| `DeclarationModifierDiagnostics.RefOnOperator` | `MIST_PM_REF_ON_OPERATOR` | numbered | `MIST0040` | Ref modifier on operator | DEC |
| `DeclarationModifierDiagnostics.RefOnOperatorParameter` | `MIST_PM_REF_ON_OPERATOR_PARAMETER` | numbered | `MIST0041` | Ref parameter on operator | DEC |
| `DeclarationModifierDiagnostics.ReadonlyRefOnOperatorParameter` | `MIST_PM_READONLY_REF_ON_OPERATOR_PARAMETER` | numbered | `MIST0042` | Readonly ref parameter on operator | DEC |
| `DeclarationModifierDiagnostics.SealedOnOperator` | `MIST_PM_SEALED_ON_OPERATOR` | numbered | `MIST0043` | Sealed modifier on operator | DEC |
| `DeclarationModifierDiagnostics.ReadonlyOnOperator` | `MIST_PM_READONLY_ON_OPERATOR` | numbered | `MIST0044` | Readonly modifier on operator | DEC |
| `DeclarationModifierDiagnostics.OwnedOnOperator` | `MIST_PM_OWNED_ON_OPERATOR` | numbered | `MIST0045` | Owned modifier on operator | DEC |
| `DeclarationModifierDiagnostics.AffineOnOperator` | `MIST_PM_AFFINE_ON_OPERATOR` | numbered | `MIST0046` | Affine modifier on operator | DEC |
| `DeclarationModifierDiagnostics.RefOnField` | `MIST_PM_REF_ON_FIELD` | numbered | `MIST0047` | Ref modifier on field | DEC |
| `DeclarationModifierDiagnostics.StaticOnAbstractMember` | `MIST_PM_STATIC_ON_ABSTRACT_MEMBER` | numbered | `MIST0048` | Static member on abstract type | DEC |
| `DriverDiagnostics.ProjectFileNotFound` | `MIST_PROJECT_FILE_NOT_FOUND` | numbered | `MIST0049` | Project file not found | CFG |
| `DriverDiagnostics.ProjectFileReadError` | `MIST_PROJECT_FILE_READ_ERROR` | numbered | `MIST0050` | Project file unreadable | IOX |
| `DriverDiagnostics.ProjectTomlError` | `MIST_PROJECT_TOML_ERROR` | numbered | `MIST0051` | Project file is not valid TOML | CFG |
| `DriverDiagnostics.ProjectMissingName` | `MIST_PROJECT_MISSING_NAME` | numbered | `MIST0052` | Project name missing | CFG |
| `DriverDiagnostics.ProjectEmptyName` | `MIST_PROJECT_EMPTY_NAME` | numbered | `MIST0053` | Project name empty | CFG |
| `DriverDiagnostics.ProjectInvalidName` | `MIST_PROJECT_INVALID_NAME` | numbered | `MIST0054` | Project name has invalid characters | CFG |
| `DriverDiagnostics.ProjectNameTooLong` | `MIST_PROJECT_NAME_TOO_LONG` | numbered | `MIST0055` | Project name too long | CFG |
| `DriverDiagnostics.ProjectEmptyNamespaceRoot` | `MIST_PROJECT_EMPTY_NAMESPACE_ROOT` | numbered | `MIST0056` | Namespace root empty | CFG |
| `DriverDiagnostics.ProjectInvalidNamespaceRoot` | `MIST_PROJECT_INVALID_NAMESPACE_ROOT` | numbered | `MIST0057` | Namespace root has invalid characters | CFG |
| `DriverDiagnostics.ProjectEmptyEntryPoint` | `MIST_PROJECT_EMPTY_ENTRY_POINT` | numbered | `MIST0058` | Entry point setting empty | CFG |
| `DriverDiagnostics.ProjectInvalidOutputType` | `MIST_PROJECT_INVALID_OUTPUT_TYPE` | numbered | `MIST0059` | Unrecognized output type | CFG |
| `DriverDiagnostics.ProjectReferenceMissingPath` | `MIST_PROJECT_REFERENCE_MISSING_PATH` | numbered | `MIST0060` | Reference has no path | CFG |
| `DriverDiagnostics.ProjectReferenceEmptyPath` | `MIST_PROJECT_REFERENCE_EMPTY_PATH` | numbered | `MIST0061` | Reference path empty | CFG |
| `DriverDiagnostics.ProjectReferenceNotFound` | `MIST_PROJECT_REFERENCE_NOT_FOUND` | numbered | `MIST0062` | Referenced project path missing | DEP |
| `DriverDiagnostics.ProjectReferenceNoProjectFile` | `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE` | numbered | `MIST0063` | Referenced directory has no project | DEP |
| `DriverDiagnostics.ProjectCircularReference` | `MIST_PROJECT_CIRCULAR_REFERENCE` | numbered | `MIST0064` | Circular project reference | DEP |
| `DriverDiagnostics.ProjectReferenceInvalidFormat` | `MIST_PROJECT_REFERENCE_INVALID_FORMAT` | numbered | `MIST0065` | Reference entry not a table | CFG |
| `DriverDiagnostics.ProjectReferencesNotTable` | `MIST_PROJECT_REFERENCE_INVALID_FORMAT` | numbered | `MIST0066` | References section not a table | CFG |
| `DriverDiagnostics.ProjectReferenceInvalidName` | `MIST_PROJECT_REFERENCE_INVALID_NAME` | numbered | `MIST0067` | Reference key has invalid characters | CFG |
| `DriverDiagnostics.ProjectReferenceEmptyName` | `MIST_PROJECT_REFERENCE_INVALID_NAME` | numbered | `MIST0068` | Reference key empty | CFG |
| `DriverDiagnostics.ProjectReferenceNameTooLong` | `MIST_PROJECT_REFERENCE_INVALID_NAME` | numbered | `MIST0069` | Reference key too long | CFG |
| `DriverDiagnostics.ProjectReferenceNameMismatch` | `MIST_PROJECT_REFERENCE_NAME_MISMATCH` | numbered | `MIST0070` | Reference key mismatch | DEP |
| `DriverDiagnostics.ReferenceUnsupportedPath` | `MIST_REFERENCE_UNSUPPORTED_PATH` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0071` | Unsupported reference file kind | CFG |
| `DriverDiagnostics.ReferenceFileNotFound` | `MIST_REFERENCE_FILE_NOT_FOUND` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0072` | Referenced file not found | DEP |
| `DriverDiagnostics.ReferenceAmbiguous` | `MIST_REFERENCE_AMBIGUOUS` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0073` | Reference directory ambiguous | DEP |
| `DriverDiagnostics.CSharpProjectBuildFailed` | `MIST_CSHARP_PROJECT_BUILD_FAILED` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0074` | C# project build failed | DEP |
| `DriverDiagnostics.ReferenceCycle` | `MIST_REFERENCE_CYCLE` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0075` | Reference cycle | DEP |
| `DriverDiagnostics.GlobalUsingInvalidFormat` | `MIST_GLOBAL_USING_INVALID_FORMAT` | numbered | `MIST0076` | Global usings section not a table | CFG |
| `DriverDiagnostics.GlobalUsingNamespacesNotArray` | `MIST_GLOBAL_USING_INVALID_FORMAT` | numbered | `MIST0077` | Global usings namespaces not array | CFG |
| `DriverDiagnostics.GlobalUsingElementNotString` | `MIST_GLOBAL_USING_INVALID_FORMAT` | numbered | `MIST0078` | Global using entry not string | CFG |
| `DriverDiagnostics.GlobalUsingMissingNamespaces` | `MIST_GLOBAL_USING_MISSING_NAMESPACES` | numbered | `MIST0079` | Global usings missing namespaces key | CFG |
| `DriverDiagnostics.GlobalUsingEmptyNamespace` | `MIST_GLOBAL_USING_EMPTY_NAMESPACE` | numbered | `MIST0080` | Global using entry empty | CFG |
| `DriverDiagnostics.GlobalUsingInvalidNamespace` | `MIST_GLOBAL_USING_INVALID_NAMESPACE` | numbered | `MIST0081` | Global using has invalid characters | CFG |
| `DriverDiagnostics.GlobalUsingDuplicateNamespace` | `MIST_GLOBAL_USING_DUPLICATE_NAMESPACE` | numbered | `MIST0082` | Duplicate global using | CFG |
| `DriverDiagnostics.PackageTableExpected` | `MIST_PKG_TABLE_EXPECTED` | numbered | `MIST0083` | Packages section not a table | CFG |
| `DriverDiagnostics.PackageInvalidName` | `MIST_PKG_INVALID_NAME` | numbered | `MIST0084` | Package name has invalid characters | CFG |
| `DriverDiagnostics.PackageNameTooLong` | `MIST_PKG_INVALID_NAME` | numbered | `MIST0085` | Package name too long | CFG |
| `DriverDiagnostics.PackageInvalidVersion` | `MIST_PKG_INVALID_VERSION` | numbered | `MIST0086` | Package version not a string | CFG |
| `DriverDiagnostics.PackageEmptyVersion` | `MIST_PKG_INVALID_VERSION` | numbered | `MIST0087` | Package version empty | CFG |
| `DriverDiagnostics.FrameworkReferenceTableExpected` | `MIST_FWREF_TABLE_EXPECTED` | numbered | `MIST0088` | Framework references not a table | CFG |
| `DriverDiagnostics.FrameworkReferenceInvalidName` | `MIST_FWREF_INVALID_NAME` | numbered | `MIST0089` | Framework reference name empty | CFG |
| `DriverDiagnostics.FrameworkReferenceNameTooLong` | `MIST_FWREF_INVALID_NAME` | numbered | `MIST0090` | Framework reference name too long | CFG |
| `DriverDiagnostics.FrameworkReferenceInvalidValue` | `MIST_FWREF_INVALID_VALUE` | numbered | `MIST0091` | Framework reference value not boolean | CFG |
| `DriverDiagnostics.CsprojPropertyTableExpected` | `MIST_CSPROJ_PROP_TABLE_EXPECTED` | numbered | `MIST0092` | Csproj properties not a table | CFG |
| `DriverDiagnostics.CsprojPropertyEmptyKey` | `MIST_CSPROJ_PROP_EMPTY_KEY` | numbered | `MIST0093` | Csproj property key empty | CFG |
| `DriverDiagnostics.CsprojPropertyInvalidKey` | `MIST_CSPROJ_PROP_INVALID_KEY` | numbered | `MIST0094` | Csproj property key has invalid characters | CFG |
| `DriverDiagnostics.CsprojPropertyKeyTooLong` | `MIST_CSPROJ_PROP_KEY_TOO_LONG` | numbered | `MIST0095` | Csproj property key too long | CFG |
| `DriverDiagnostics.CsprojPropertyInvalidValue` | `MIST_CSPROJ_PROP_INVALID_VALUE` | numbered | `MIST0096` | Csproj property value not string | CFG |
| `DriverDiagnostics.CsprojPropertyEmptyValue` | `MIST_CSPROJ_PROP_EMPTY_VALUE` | numbered | `MIST0097` | Csproj property value empty | CFG |
| `DriverDiagnostics.CsprojPropertyManagedOverride` | `MIST_CSPROJ_PROP_MANAGED_OVERRIDE` | numbered | `MIST0098` | Compiler-managed property overridden | CFG |
| `DriverDiagnostics.UnknownBackend` | `MIST_UNKNOWN_BACKEND` | numbered | `MIST0099` | Unknown backend | CFG |
| `DriverDiagnostics.RuntimeIdentifierUnknown` | `MIST_RID_UNKNOWN` | numbered (added after `af6a1f70` by `0df844d0`) | `MIST0100` | Unknown runtime identifier | CFG |
| `DriverDiagnostics.FileError` | `MIST_FILE_ERROR` | numbered | `MIST0101` | Source file unreadable | IOX |
| `DriverDiagnostics.TestFrameworkPackageMissing` | `MIST_TEST_0001` | numbered (added after `af6a1f70` by Phase 1) | `MIST0102` | Test framework package missing | CFG |
| `DriverDiagnostics.DiagnosticsTableExpected` | `MIST_DIAGNOSTICS_TABLE_EXPECTED` | numbered (added after `af6a1f70` by Phase 1) | `MIST0103` | Diagnostics field not a table | CFG |
| `DriverDiagnostics.DiagnosticsKeyUnknown` | `MIST_DIAGNOSTICS_KEY_UNKNOWN` | numbered (added after `af6a1f70` by Phase 1) | `MIST0104` | Unknown diagnostics key | CFG |
| `DriverDiagnostics.DiagnosticsValueInvalid` | `MIST_DIAGNOSTICS_VALUE_INVALID` | numbered (added after `af6a1f70` by Phase 1) | `MIST0105` | Invalid diagnostics value | CFG |
| `DriverDiagnostics.DiagnosticsNumberUnknown` | `MIST_DIAGNOSTICS_NUMBER_UNKNOWN` | numbered (added after `af6a1f70` by Phase 1) | `MIST0106` | Unknown diagnostic number | CFG |
| `DriverDiagnostics.DiagnosticsNumberNotOptional` | `MIST_DIAGNOSTICS_NUMBER_NOT_OPTIONAL` | numbered (added after `af6a1f70` by Phase 1) | `MIST0107` | Diagnostic cannot be switched | CFG |
| `EntryPointDiagnostics.MissingEntryPoint` | `MIST_MISSING_ENTRY_POINT` | numbered | `MIST0108` | Missing entry point | DEC |
| `EntryPointDiagnostics.MultipleTopLevelFiles` | `MIST_EP_0001` | numbered | `MIST0109` | Multiple top-level statement files | DEC |
| `EntryPointDiagnostics.TopLevelStatementsBesideMain` | `MIST_EP_0002` | numbered | `MIST0110` | Top-level statements beside Main | DEC |
| `EntryPointDiagnostics.ClassEntryPointBesideTopLevelStatements` | `MIST_EP_0003` | numbered | `MIST0111` | Class entry point with top-level statements | CFG |
| `EntryPointDiagnostics.MultipleMains` | `MIST_EP_0004` | numbered | `MIST0112` | Multiple Main methods | DEC |
| `EntryPointDiagnostics.TopLevelEntryPointWithoutStatements` | `MIST_EP_0005` | numbered | `MIST0113` | Top-level entry point without statements | CFG |
| `EntryPointDiagnostics.TopLevelStatementsWithBlockNamespace` | `MIST_EP_0006` | numbered | `MIST0114` | Top-level statements beside block namespace | DEC |
| `EntryPointDiagnostics.EntryPointClassNotFound` | `MIST_EP_0007` | numbered | `MIST0115` | Entry point class not found | CFG |
| `EntryPointDiagnostics.LibraryWithTopLevelStatements` | `MIST_EP_0008` | numbered | `MIST0116` | Top-level statements in library | DEC |
| `EntryPointDiagnostics.DuplicateEntryPoint` | `MIST_PM_DUPLICATE_ENTRY_POINT` | numbered | `MIST0117` | Multiple entry points in class | DEC |
| `InternalDiagnostics.ArgumentSchemaBreach` | `MIST_ICE_ARGUMENT_SCHEMA_BREACH` | numbered (added after `af6a1f70` by Phase 1) | `MIST0118` | Diagnostic arguments breach schema | ICE |
| `InternalDiagnostics.LocationOutOfBounds` | `MIST_ICE_LOCATION_OUT_OF_BOUNDS` | numbered (added after `af6a1f70` by Phase 1) | `MIST0119` | Diagnostic span outside its file | ICE |
| `LexerDiagnostics.LexError` | `MIST_LEX_ERROR` | numbered | `MIST0120` | Lexical error | SYN |
| `ParserDiagnostics.ParseStub` | `MIST_PARSE_STUB` | numbered | `MIST0121` | Parser not implemented | SYN |
| `ParserDiagnostics.ParseError` | `MIST_PARSE_ERROR` | numbered | `MIST0122` | Parse error | SYN |
| `ParserDiagnostics.ParseIncomplete` | `MIST_PARSE_INCOMPLETE` | numbered | `MIST0123` | Parser stopped before end of file | SYN |
| `ParserDiagnostics.StackallocSize` | `MIST_PARSE_STACKALLOC_SIZE` | numbered | `MIST0124` | Stackalloc without size | SYN |
| `ParserDiagnostics.RemovedInterfaceKeyword` | `MIST_REMOVED_INTERFACE_KEYWORD` | numbered | `MIST0125` | Removed interface keyword | SYN |
| `ParserDiagnostics.RemovedTraitKeyword` | `MIST_REMOVED_TRAIT_KEYWORD` | numbered | `MIST0126` | Removed trait keyword | SYN |
| `ParserDiagnostics.AbstractPropertyDefault` | `MIST_ABSTRACT_PROPERTY_DEFAULT` | numbered | `MIST0127` | Property slot with default body | SYN |
| `ParserDiagnostics.MultipleAccessModifiers` | `MIST_PARSE_MULTIPLE_ACCESS` | numbered | `MIST0128` | Multiple access modifiers | SYN |
| `ProgramAnalysisDiagnostics.MissingContext` | `MIST_CTX_0001` | numbered | `MIST0129` | Missing context | EFF |
| `ProgramAnalysisDiagnostics.DuplicateContextBinding` | `MIST_CTX_0002` | numbered | `MIST0130` | Duplicate context binding | EFF |
| `ProgramAnalysisDiagnostics.ContextTypeNotNameable` | `MIST_CTX_0004` | numbered | `MIST0131` | Context type not nameable | EFF |
| `ProgramAnalysisDiagnostics.ContextVariableNotReassignable` | `MIST_CTX_0005` | numbered | `MIST0132` | Context variable not reassignable | EFF |
| `ProgramAnalysisDiagnostics.ContextContractNotMet` | `MIST_CTX_0006` | numbered | `MIST0133` | Context contract not met | EFF |
| `ProgramAnalysisDiagnostics.DuplicateExtensionMethod` | `MIST_PA_DUPLICATE_EXTENSION_METHOD` | numbered | `MIST0134` | Duplicate extension method | DEC |
| `ProgramAnalysisDiagnostics.DuplicateExtensionProperty` | `MIST_PA_DUPLICATE_EXTENSION_PROPERTY` | numbered | `MIST0135` | Duplicate extension property | DEC |
| `ProgramAnalysisDiagnostics.FieldInComputedProperty` | `MIST_PA_FIELD_IN_COMPUTED_PROPERTY` | numbered | `MIST0136` | Field keyword in computed property | DEC |
| `ProgramAnalysisDiagnostics.PureScopeWrites` | `MIST_PA_PURE_SCOPE_WRITES` | numbered | `MIST0137` | Pure scope writes | EFF |
| `ProgramAnalysisDiagnostics.PureScopeReadsMutable` | `MIST_PA_PURE_SCOPE_READS_MUTABLE` | numbered | `MIST0138` | Pure scope reads mutable state | EFF |
| `ProgramAnalysisDiagnostics.PureScopeCallsImpure` | `MIST_PA_PURE_SCOPE_CALLS_IMPURE` | numbered | `MIST0139` | Pure scope calls impure member | EFF |
| `ProgramAnalysisDiagnostics.LocalScopeReadsArgument` | `MIST_PA_LOCAL_SCOPE_READS_ARGUMENT` | numbered | `MIST0140` | Local scope reads through argument | EFF |
| `ProgramAnalysisDiagnostics.PureScopeStoresUnowned` | `MIST_PA_PURE_SCOPE_STORES_UNOWNED` | numbered | `MIST0141` | Pure scope stores unowned value | EFF |
| `ProgramAnalysisDiagnostics.PureFunctionRequired` | `MIST_PA_PURE_FUNCTION_REQUIRED` | numbered | `MIST0142` | Pure function required | EFF |
| `ProgramAnalysisDiagnostics.DefaultNotConsistent` | `MIST_PA_DEFAULT_NOT_CONSISTENT` | numbered | `MIST0143` | Default value not consistent | EFF |
| `ProgramAnalysisDiagnostics.PurityContractNotMet` | `MIST_PA_PURITY_CONTRACT_NOT_MET` | numbered | `MIST0144` | Purity contract not met | EFF |
| `ProgramAnalysisDiagnostics.PureFunctionTypeUntestable` | `MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE` | numbered | `MIST0145` | Pure function type untestable | EFF |
| `ProgramAnalysisDiagnostics.PureFunctionTypeUntestableClosing` | `MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE` | numbered | `MIST0146` | Untestable pure function type argument | EFF |
| `ProgramAnalysisDiagnostics.StructBaseConstructorRequired` | `MIST_STRUCT_BASE_CTOR_REQUIRED` | numbered | `MIST0147` | Struct base constructor required | DEC |
| `ProgramAnalysisDiagnostics.SwitchNotExhaustive` | `MIST_PA_SWITCH_NOT_EXHAUSTIVE` | numbered | `MIST0148` | Switch not exhaustive | FLW |
| `ProgramAnalysisDiagnostics.CastHasNoConversion` | `MIST_PA_CAST_HAS_NO_CONVERSION` | numbered | `MIST0149` | Cast without conversion | TYP |
| `ProgramAnalysisDiagnostics.EnumCastArgumentMismatch` | `MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH` | numbered | `MIST0150` | Enum cast argument mismatch | TYP |
| `ProgramAnalysisDiagnostics.CastArgumentMismatch` | `MIST_PA_CAST_ARGUMENT_MISMATCH` | numbered | `MIST0151` | Cast operand mismatch | TYP |
| `ProgramAnalysisDiagnostics.NewtypeSourceDefinesNoCopy` | `MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY` | numbered | `MIST0152` | Newtype source has no copy | TYP |
| `ProgramAnalysisDiagnostics.CopyConversionDoesNotHold` | `MIST_PA_COPY_CONVERSION_DOES_NOT_HOLD` | numbered | `MIST0153` | Field-wise copy does not hold | TYP |
| `ProgramAnalysisDiagnostics.CloneConversionDoesNotHold` | `MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD` | numbered | `MIST0154` | Clone conversion does not hold | TYP |
| `ProgramAnalysisDiagnostics.AmbiguousCast` | `MIST_PA_AMBIGUOUS_CAST` | numbered | `MIST0155` | Ambiguous cast | TYP |
| `ProgramAnalysisDiagnostics.AmbiguousImplicitConversion` | `MIST_PA_AMBIGUOUS_IMPLICIT_CONVERSION` | numbered | `MIST0156` | Ambiguous implicit conversion | TYP |
| `ProgramAnalysisDiagnostics.NoImplicitConversion` | `MIST_PA_NO_IMPLICIT_CONVERSION` | numbered | `MIST0157` | No implicit conversion | TYP |
| `ProgramAnalysisDiagnostics.CastRequired` | `MIST_PA_CAST_REQUIRED` | numbered | `MIST0158` | Explicit cast required | TYP |
| `ProgramAnalysisDiagnostics.InaccessibleMember` | `MIST_PA_INACCESSIBLE_MEMBER` | numbered | `MIST0159` | Inaccessible member | DEC |
| `ProgramAnalysisDiagnostics.InaccessibleType` | `MIST_PA_INACCESSIBLE_TYPE` | numbered | `MIST0160` | Inaccessible type | DEC |
| `ProgramAnalysisDiagnostics.InconsistentAccessibility` | `MIST_PA_INCONSISTENT_ACCESSIBILITY` | numbered | `MIST0161` | Inconsistent accessibility | DEC |
| `ProgramAnalysisDiagnostics.SealedBaseType` | `MIST_PA_SEALED_BASE_TYPE` | numbered | `MIST0162` | Derivation from sealed type | DEC |
| `ProgramAnalysisDiagnostics.CyclicInheritance` | `MIST_PA_CYCLIC_INHERITANCE` | numbered | `MIST0163` | Inheritance cycle | DEC |
| `ProgramAnalysisDiagnostics.NewtypeOverSealedType` | `MIST_PA_NEWTYPE_OVER_SEALED_TYPE` | numbered | `MIST0164` | Newtype over sealed type | DEC |
| `ProgramAnalysisDiagnostics.SealedTypeAlias` | `MIST_PA_SEALED_TYPE_ALIAS` | numbered | `MIST0165` | Sealed type alias | DEC |
| `ProgramAnalysisDiagnostics.EnumBaseNotReadonly` | `MIST_PA_ENUM_BASE_NOT_READONLY` | numbered | `MIST0166` | Enum base not readonly | DEC |
| `ProgramAnalysisDiagnostics.EnumDuplicateValue` | `MIST_PA_ENUM_DUPLICATE_VALUE` | numbered | `MIST0167` | Duplicate enum value | DEC |
| `ProgramAnalysisDiagnostics.DefaultEntryNotAField` | `MIST_PA_DEFAULT_ENTRY_NOT_A_FIELD` | numbered | `MIST0168` | Default entry not a field | DEC |
| `ProgramAnalysisDiagnostics.OperatorNotPublic` | `MIST_PA_OPERATOR_NOT_PUBLIC` | numbered | `MIST0169` | Operator not public static | DEC |
| `ProgramAnalysisDiagnostics.OperatorUnpaired` | `MIST_PA_OPERATOR_UNPAIRED` | numbered | `MIST0170` | Unpaired operator | DEC |
| `ProgramAnalysisDiagnostics.LogicalOperatorDeclared` | `MIST_PA_LOGICAL_OPERATOR_DECLARED` | numbered | `MIST0171` | Logical operator on non-Boolean type | DEC |
| `ProgramAnalysisDiagnostics.OperatorNotOnDeclaringType` | `MIST_PA_OPERATOR_NOT_ON_DECLARING_TYPE` | numbered | `MIST0172` | Operator omits declaring type | DEC |
| `ProgramAnalysisDiagnostics.RefinementNotComparable` | `MIST_PA_REFINEMENT_NOT_COMPARABLE` | numbered | `MIST0173` | Refinement over incomparable type | TYP |
| `ProgramAnalysisDiagnostics.RefinementConstantMismatch` | `MIST_PA_REFINEMENT_CONSTANT_MISMATCH` | numbered | `MIST0174` | Refinement constant type mismatch | TYP |
| `ProgramAnalysisDiagnostics.RefinementOverMutableMember` | `MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER` | numbered | `MIST0175` | Refinement reads mutable member | TYP |
| `ProgramAnalysisDiagnostics.DuplicateSignature` | `MIST_PA_DUPLICATE_SIGNATURE` | numbered | `MIST0176` | Duplicate member signature | DEC |
| `ProgramAnalysisDiagnostics.MemberRedeclaration` | `MIST_PA_MEMBER_REDECLARATION` | numbered | `MIST0177` | Member redeclared | DEC |
| `ProgramAnalysisDiagnostics.ConstraintNotSatisfied` | `MIST_PA_CONSTRAINT_NOT_SATISFIED` | numbered | `MIST0178` | Constraint not satisfied | TYP |
| `ProgramAnalysisDiagnostics.ConstructorConstraintNotSatisfied` | `MIST_PA_CONSTRUCTOR_CONSTRAINT_NOT_SATISFIED` | numbered | `MIST0179` | Constructor constraint not satisfied | TYP |
| `ProgramAnalysisDiagnostics.NotDefaultable` | `MIST_PA_NOT_DEFAULTABLE` | numbered | `MIST0180` | Type has no default | TYP |
| `ProgramAnalysisDiagnostics.FieldHasNoDefault` | `MIST_PA_FIELD_HAS_NO_DEFAULT` | numbered | `MIST0181` | Field without default | DEC |
| `ProgramAnalysisDiagnostics.BaseNotDefaultable` | `MIST_PA_BASE_NOT_DEFAULTABLE` | numbered | `MIST0182` | Base type without default | DEC |
| `ProgramAnalysisDiagnostics.SlotNotFilled` | `MIST_PA_SLOT_NOT_FILLED` | numbered | `MIST0183` | Abstract slot not filled | DEC |
| `ProgramAnalysisDiagnostics.SlotSignatureMismatch` | `MIST_PA_SLOT_SIGNATURE_MISMATCH` | numbered | `MIST0184` | Slot signature mismatch | DEC |
| `ProgramAnalysisDiagnostics.SlotConstraintStrengthened` | `MIST_PA_SLOT_CONSTRAINT_STRENGTHENED` | numbered | `MIST0185` | Slot constraint strengthened | DEC |
| `ProgramAnalysisDiagnostics.SlotReturnNotOwned` | `MIST_PA_SLOT_RETURN_NOT_OWNED` | numbered | `MIST0186` | Slot filler not returning owned | DEC |
| `ProgramAnalysisDiagnostics.ReadonlyContractNotMet` | `MIST_PA_READONLY_CONTRACT_NOT_MET` | numbered | `MIST0187` | Readonly contract not met | EFF |
| `ProgramAnalysisDiagnostics.PropertySlotAccessorMissing` | `MIST_PA_PROPERTY_SLOT_ACCESSOR_MISSING` | numbered | `MIST0188` | Property slot accessor missing | DEC |
| `ProgramAnalysisDiagnostics.ConformanceSlotUnanswered` | `MIST_PA_CONFORMANCE_SLOT_UNANSWERED` | numbered | `MIST0189` | Conformance slot unanswered | DEC |
| `ProgramAnalysisDiagnostics.RedundantConformance` | `MIST_PA_REDUNDANT_CONFORMANCE` | numbered | `MIST0190` | Redundant conformance | DEC |
| `ProgramAnalysisDiagnostics.TypeSlotUnfilled` | `MIST_PA_TYPE_SLOT_UNFILLED` | numbered | `MIST0191` | Associated type slot unfilled | DEC |
| `ProgramAnalysisDiagnostics.TypeSlotConstraintNotSatisfied` | `MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED` | numbered | `MIST0192` | Type slot constraint not satisfied | DEC |
| `ProgramAnalysisDiagnostics.PatternNeverMatches` | `MIST_PA_PATTERN_NEVER_MATCHES` | numbered | `MIST0193` | Pattern never matches | TYP |
| `ProgramAnalysisDiagnostics.PatternNarrowsToUnknown` | `MIST_PA_PATTERN_NARROWS_TO_UNKNOWN` | numbered | `MIST0194` | Pattern narrows to unknown | TYP |
| `ProgramAnalysisDiagnostics.PatternNarrowsToUnknownClosure` | `MIST_PA_PATTERN_NARROWS_TO_UNKNOWN_CLOSURE` | numbered | `MIST0195` | Pattern narrows to unknown closure | TYP |
| `ProgramAnalysisDiagnostics.PatternNotComparable` | `MIST_PA_PATTERN_NOT_COMPARABLE` | numbered | `MIST0196` | Relational pattern over incomparable type | TYP |
| `ProgramAnalysisDiagnostics.PatternConstantMismatch` | `MIST_PA_PATTERN_CONSTANT_MISMATCH` | numbered | `MIST0197` | Pattern constant type mismatch | TYP |
| `ProgramAnalysisDiagnostics.PatternBoundNotConstant` | `MIST_PA_PATTERN_BOUND_NOT_CONSTANT` | numbered | `MIST0198` | Relational pattern bound not constant | TYP |
| `ProgramAnalysisDiagnostics.SwitchArmUnreachable` | `MIST_PA_SWITCH_ARM_UNREACHABLE` | numbered | `MIST0199` | Unreachable switch arm | FLW |
| `ProgramAnalysisDiagnostics.OperatorNotApplicable` | `MIST_PA_OPERATOR_NOT_APPLICABLE` | numbered | `MIST0200` | Operator not applicable | TYP |
| `ProgramAnalysisDiagnostics.UnknownHasNoOperators` | `MIST_PA_UNKNOWN_HAS_NO_OPERATORS` | numbered | `MIST0201` | Operator applied to unknown | TYP |
| `ProgramAnalysisDiagnostics.UnknownHasNoMembers` | `MIST_PA_UNKNOWN_HAS_NO_MEMBERS` | numbered | `MIST0202` | Member access on unknown | TYP |
| `ProgramAnalysisDiagnostics.UnknownClosureConstructed` | `MIST_PA_UNKNOWN_CLOSURE_CONSTRUCTED` | numbered | `MIST0203` | Construction of unknown closure | TYP |
| `ProgramAnalysisDiagnostics.UnknownHasNoDefault` | `MIST_PA_UNKNOWN_HAS_NO_DEFAULT` | numbered | `MIST0204` | Unknown has no default | TYP |
| `ProgramAnalysisDiagnostics.ConditionalAccessOnNonOptional` | `MIST_PA_CONDITIONAL_ACCESS_ON_NON_OPTIONAL` | numbered | `MIST0205` | Conditional access on non-optional | TYP |
| `ProgramAnalysisDiagnostics.CoalescingAssignmentNotAStatement` | `MIST_PA_COALESCING_ASSIGNMENT_NOT_A_STATEMENT` | numbered | `MIST0206` | Coalescing assignment as expression | SYN |
| `ProgramAnalysisDiagnostics.ReadonlyLocalReassigned` | `MIST_PA_READONLY_LOCAL_REASSIGNED` | numbered | `MIST0207` | Readonly local reassigned | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyReferenceWritten` | `MIST_PA_READONLY_REF_WRITTEN` | numbered | `MIST0208` | Write through readonly reference | EFF |
| `ProgramAnalysisDiagnostics.ReferenceToNonLocation` | `MIST_PA_REF_NOT_A_LOCATION` | numbered | `MIST0209` | Reference to non-location | TYP |
| `ProgramAnalysisDiagnostics.NotAssignable` | `MIST_PA_NOT_ASSIGNABLE` | numbered | `MIST0210` | Target not assignable | TYP |
| `ProgramAnalysisDiagnostics.StaticLambdaCapture` | `MIST_PA_STATIC_LAMBDA_CAPTURE` | numbered | `MIST0211` | Static lambda captures | OWN |
| `ProgramAnalysisDiagnostics.StaticLocalFunctionCapture` | `MIST_PA_STATIC_LOCAL_FUNCTION_CAPTURE` | numbered | `MIST0212` | Static local function captures | OWN |
| `ProgramAnalysisDiagnostics.OptionalOfOptional` | `MIST_PA_OPTIONAL_OF_OPTIONAL` | numbered | `MIST0213` | Optional of optional | TYP |
| `ProgramAnalysisDiagnostics.NoGetAccessor` | `MIST_PA_NO_GET_ACCESSOR` | numbered | `MIST0214` | No get accessor | DEC |
| `ProgramAnalysisDiagnostics.NoSetAccessor` | `MIST_PA_NO_SET_ACCESSOR` | numbered | `MIST0215` | No set accessor | DEC |
| `ProgramAnalysisDiagnostics.InaccessibleAccessor` | `MIST_PA_INACCESSIBLE_ACCESSOR` | numbered | `MIST0216` | Inaccessible accessor | DEC |
| `ProgramAnalysisDiagnostics.InitOnlyAssignment` | `MIST_PA_INIT_ONLY_ASSIGNMENT` | numbered | `MIST0217` | Init-only assignment | DEC |
| `ProgramAnalysisDiagnostics.InitializerMemberRepeated` | `MIST_PA_INITIALIZER_MEMBER_REPEATED` | numbered | `MIST0218` | Initializer member repeated | DEC |
| `ProgramAnalysisDiagnostics.InitializerKindsMixed` | `MIST_PA_INITIALIZER_KINDS_MIXED` | numbered | `MIST0219` | Initializer kinds mixed | DEC |
| `ProgramAnalysisDiagnostics.LocalReadBeforeAssignment` | `MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT` | numbered | `MIST0220` | Local read before assignment | FLW |
| `ProgramAnalysisDiagnostics.LocalFunctionReadsUnassigned` | `MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED` | numbered | `MIST0221` | Local function reads unassigned local | FLW |
| `ProgramAnalysisDiagnostics.FieldReadBeforeAssignment` | `MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT` | numbered | `MIST0222` | Field read before assignment | FLW |
| `ProgramAnalysisDiagnostics.FieldNotDefinitelyAssigned` | `MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED` | numbered | `MIST0223` | Field not definitely assigned | FLW |
| `ProgramAnalysisDiagnostics.StampableDeclarationNotSatisfied` | `MIST_PA_STAMPABLE_DECLARATION_NOT_SATISFIED` | numbered | `MIST0224` | Stampable declaration not satisfied | OWN |
| `ProgramAnalysisDiagnostics.ArrayRequiresInitializer` | `MIST_PA_ARRAY_REQUIRES_INITIALIZER` | numbered | `MIST0225` | Array requires initializer | TYP |
| `ProgramAnalysisDiagnostics.StackallocElementNotStampable` | `MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE` | numbered | `MIST0226` | Stackalloc element not stampable | OWN |
| `ProgramAnalysisDiagnostics.UseAfterMove` | `MIST_PA_USE_AFTER_MOVE` | numbered | `MIST0227` | Use after move | OWN |
| `ProgramAnalysisDiagnostics.LoopMove` | `MIST_PA_LOOP_MOVE` | numbered | `MIST0228` | Owned variable moved in loop | OWN |
| `ProgramAnalysisDiagnostics.LeaseViolation` | `MIST_PA_LEASE_VIOLATION` | numbered | `MIST0229` | Lease violation | OWN |
| `ProgramAnalysisDiagnostics.IncompatibleLend` | `MIST_PA_INCOMPATIBLE_LEND` | numbered | `MIST0230` | Incompatible lend | OWN |
| `ProgramAnalysisDiagnostics.ThisTierMismatch` | `MIST_PA_THIS_TIER_MISMATCH` | numbered | `MIST0231` | Insufficient this access for call | OWN |
| `ProgramAnalysisDiagnostics.ThisTierInsufficient` | `MIST_PA_THIS_TIER_INSUFFICIENT` | numbered | `MIST0232` | Declared this tier too low | OWN |
| `ProgramAnalysisDiagnostics.SharedExtract` | `MIST_PA_SHARED_EXTRACT` | numbered | `MIST0233` | Extract through shared binding | OWN |
| `ProgramAnalysisDiagnostics.CompositeState` | `MIST_PA_COMPOSITE_STATE` | numbered | `MIST0234` | Composite state conflict | OWN |
| `ProgramAnalysisDiagnostics.FieldStateCall` | `MIST_PA_FIELD_STATE_CALL` | numbered | `MIST0235` | Call touches unavailable field | OWN |
| `ProgramAnalysisDiagnostics.OwnedAbstract` | `MIST_PA_OWNED_ABSTRACT` | numbered | `MIST0236` | Owned abstract type | OWN |
| `ProgramAnalysisDiagnostics.OwnedFieldType` | `MIST_PA_OWNED_FIELD_TYPE` | numbered | `MIST0237` | Owned type field not ownable | OWN |
| `ProgramAnalysisDiagnostics.OwnedSubstructuralBinding` | `MIST_PA_OWNED_SUBSTRUCTURAL_BINDING` | numbered | `MIST0238` | Owned on substructural type | OWN |
| `ProgramAnalysisDiagnostics.ConflictingDiscipline` | `MIST_PA_CONFLICTING_DISCIPLINE` | numbered | `MIST0239` | Conflicting disciplines | OWN |
| `ProgramAnalysisDiagnostics.FieldDisciplineContainment` | `MIST_PA_FIELD_DISCIPLINE_CONTAINMENT` | numbered | `MIST0240` | Field discipline containment | OWN |
| `ProgramAnalysisDiagnostics.FieldTypeParameterUnconstrained` | `MIST_PA_FIELD_TYPE_PARAMETER_UNCONSTRAINED` | numbered | `MIST0241` | Unconstrained type parameter field | OWN |
| `ProgramAnalysisDiagnostics.RefOnClass` | `MIST_PA_REF_ON_CLASS` | numbered | `MIST0242` | Ref modifier on class | DEC |
| `ProgramAnalysisDiagnostics.RefStructField` | `MIST_PA_REF_STRUCT_FIELD` | numbered | `MIST0243` | Ref struct field in non-ref type | DEC |
| `ProgramAnalysisDiagnostics.RefStructTypeArgument` | `MIST_PA_REF_STRUCT_TYPE_ARGUMENT` | numbered | `MIST0244` | Ref struct as type argument | TYP |
| `ProgramAnalysisDiagnostics.RefStructProperty` | `MIST_PA_REF_STRUCT_PROPERTY` | numbered | `MIST0245` | Ref struct property in non-ref type | DEC |
| `ProgramAnalysisDiagnostics.RefStructStaticStorage` | `MIST_PA_REF_STRUCT_STATIC_STORAGE` | numbered | `MIST0246` | Ref struct in static storage | DEC |
| `ProgramAnalysisDiagnostics.RefStructUnionConstituent` | `MIST_PA_REF_STRUCT_UNION_CONSTITUENT` | numbered | `MIST0247` | Ref struct in union | TYP |
| `ProgramAnalysisDiagnostics.RefStructOptional` | `MIST_PA_REF_STRUCT_OPTIONAL` | numbered | `MIST0248` | Optional ref struct | TYP |
| `ProgramAnalysisDiagnostics.RefStructElement` | `MIST_PA_REF_STRUCT_ELEMENT` | numbered | `MIST0249` | Ref struct as element | TYP |
| `ProgramAnalysisDiagnostics.RefStructBoxed` | `MIST_PA_REF_STRUCT_BOXED` | numbered | `MIST0250` | Ref struct boxed by conversion | TYP |
| `ProgramAnalysisDiagnostics.FieldAssignedInReadonlyMember` | `MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER` | numbered | `MIST0251` | Field assigned in readonly member | EFF |
| `ProgramAnalysisDiagnostics.FieldAssignedOnReadonlyParameter` | `MIST_PA_FIELD_ASSIGNED_ON_READONLY_PARAMETER` | numbered | `MIST0252` | Field assigned on readonly parameter | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyParameterCallsMutable` | `MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE` | numbered | `MIST0253` | Mutating call on readonly parameter | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyParameterSetsProperty` | `MIST_PA_READONLY_PARAMETER_SETS_PROPERTY` | numbered | `MIST0254` | Property set on readonly parameter | EFF |
| `ProgramAnalysisDiagnostics.FieldAssignedInReadonlyBlock` | `MIST_PA_FIELD_ASSIGNED_IN_READONLY_BLOCK` | numbered | `MIST0255` | Field assigned in readonly block | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughIndex` | `MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX` | numbered | `MIST0256` | Index write in readonly context | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable` | `MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE` | numbered | `MIST0257` | Mutating call in readonly context | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyContextFormatsMutable` | `MIST_PA_READONLY_CONTEXT_FORMATS_MUTABLE` | numbered | `MIST0258` | Formatting mutable type in readonly context | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyContextSetsProperty` | `MIST_PA_READONLY_CONTEXT_SETS_PROPERTY` | numbered | `MIST0259` | Property set in readonly context | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyContextAllocatesMutable` | `MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE` | numbered | `MIST0260` | Allocation in readonly context | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughRef` | `MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_REF` | numbered | `MIST0261` | Ref write in readonly context | EFF |
| `ProgramAnalysisDiagnostics.ReadonlyFieldAssigned` | `MIST_PA_READONLY_FIELD_ASSIGNED` | numbered | `MIST0262` | Readonly field assigned | EFF |
| `ProgramAnalysisDiagnostics.OwnedReturnNotOwned` | `MIST_PA_OWNED_RETURN_NOT_OWNED` | numbered | `MIST0263` | Returned value not owned | OWN |
| `ProgramAnalysisDiagnostics.OwnedSourceNotOwned` | `MIST_PA_OWNED_SOURCE_NOT_OWNED` | numbered | `MIST0264` | Moved value not owned | OWN |
| `ProgramAnalysisDiagnostics.StackSpanReturned` | `MIST_PA_STACK_SPAN_RETURNED` | numbered | `MIST0265` | Stack span returned | OWN |
| `ProgramAnalysisDiagnostics.StackSpanStored` | `MIST_PA_STACK_SPAN_STORED` | numbered | `MIST0266` | Stack span stored | OWN |
| `ProgramAnalysisDiagnostics.StackSpanPassed` | `MIST_PA_STACK_SPAN_PASSED` | numbered | `MIST0267` | Stack span passed with sink | OWN |
| `ProgramAnalysisDiagnostics.RefReturnEscapes` | `MIST_PA_REF_RETURN_ESCAPES` | numbered | `MIST0268` | Ref return escapes frame | OWN |
| `ProgramAnalysisDiagnostics.CapturedBinding` | `MIST_PA_CAPTURED_BINDING` | numbered | `MIST0269` | Disciplined binding captured | OWN |
| `ProgramAnalysisDiagnostics.RegexInvalid` | `MIST_PA_REGEX_INVALID` | numbered | `MIST0270` | Invalid regex pattern | TYP |
| `ProgramAnalysisDiagnostics.RefinementViolated` | `MIST_PA_REFINEMENT_VIOLATED` | numbered | `MIST0271` | Refinement violated | TYP |
| `ProgramAnalysisDiagnostics.RefinementUnproven` | `MIST_PA_REFINEMENT_UNPROVEN` | numbered | `MIST0272` | Refinement unproven | TYP |
| `ProgramAnalysisDiagnostics.RefinementNotImplied` | `MIST_PA_REFINEMENT_NOT_IMPLIED` | numbered | `MIST0273` | Refinement not implied | TYP |
| `ProgramAnalysisDiagnostics.IndexOutOfBounds` | `MIST_PA_INDEX_OUT_OF_BOUNDS` | numbered | `MIST0274` | Index out of bounds | TYP |
| `ProgramAnalysisDiagnostics.IndexUnproven` | `MIST_PA_INDEX_UNPROVEN` | numbered | `MIST0275` | Index not proven in bounds | TYP |
| `ProgramAnalysisDiagnostics.ConstraintViolated` | `MIST_PA_CONSTRAINT_VIOLATED` | numbered | `MIST0276` | Refinement constraint violated | TYP |
| `ProgramAnalysisDiagnostics.ConstraintUnproven` | `MIST_PA_CONSTRAINT_UNPROVEN` | numbered | `MIST0277` | Refinement constraint unproven | TYP |
| `ProgramAnalysisDiagnostics.DependentArgumentViolated` | `MIST_PA_DEPENDENT_ARGUMENT_VIOLATED` | numbered | `MIST0278` | Dependent argument violated | TYP |
| `ProgramAnalysisDiagnostics.DependentForwardUnproven` | `MIST_PA_DEPENDENT_FORWARD_UNPROVEN` | numbered | `MIST0279` | Dependent forwarding unproven | TYP |
| `ProgramAnalysisDiagnostics.DependentArgumentNotAssignable` | `MIST_DT_0003` | numbered | `MIST0280` | Dependent argument not assignable | TYP |
| `ProgramAnalysisDiagnostics.DependentForwardNotConvertible` | `MIST_DT_0004` | numbered | `MIST0281` | Dependent forwarding not convertible | TYP |
| `ProgramAnalysisDiagnostics.DependentParameterConflictsWithMember` | `MIST_DT_0005` | numbered | `MIST0282` | Dependent parameter name conflict | NAM |
| `ProgramAnalysisDiagnostics.ConstraintStrengthened` | `MIST_PA_CONSTRAINT_STRENGTHENED` | numbered | `MIST0283` | Implementation strengthens constraint | DEC |
| `ProgramAnalysisDiagnostics.RefinementReturnUnproven` | `MIST_PA_REFINEMENT_RETURN_UNPROVEN` | numbered | `MIST0284` | Refinement return unproven | TYP |
| `ProgramAnalysisDiagnostics.BaseConstructorUnsatisfied` | `MIST_PA_BASE_CTOR_UNSATISFIED` | numbered | `MIST0285` | Base constructor arguments missing | DEC |
| `ProgramAnalysisDiagnostics.PropertyHasNoDefault` | `MIST_PA_PROPERTY_HAS_NO_DEFAULT` | numbered | `MIST0286` | Property without default | DEC |
| `ProgramAnalysisDiagnostics.RefinementZeroNotAdmitted` | `MIST_PA_REFINEMENT_ZERO_NOT_ADMITTED` | numbered | `MIST0287` | Refined default rejects zero | DEC |
| `ProgramModelDiagnostics.UnexpectedNamespaceNode` | `MIST_INTERNAL_UNEXPECTED_NS_NODE` | numbered | `MIST0288` | Unexpected namespace node | ICE |
| `ProgramModelDiagnostics.UnknownTypeDeclaration` | `MIST_INTERNAL_UNKNOWN_TYPE_DECL` | numbered | `MIST0289` | Unrecognized type declaration node | ICE |
| `ProgramModelDiagnostics.DuplicateFileScopedNamespace` | `MIST0301` | numbered | `MIST0290` | Multiple file-scoped namespaces | SYN |
| `ProgramModelDiagnostics.MixedNamespaceStyles` | `MIST0302` | numbered | `MIST0291` | Mixed namespace styles | SYN |
| `ProgramModelDiagnostics.FileScopedInsideBlock` | `MIST0305` | numbered | `MIST0292` | File-scoped namespace inside block | SYN |
| `ProgramModelDiagnostics.UndefinedNamespace` | `MIST_UNDEFINED_NAMESPACE` | numbered | `MIST0293` | Unknown namespace | NAM |
| `ProgramModelDiagnostics.GlobalUsingUndefinedNamespace` | `MIST_GLOBAL_USING_UNDEFINED_NAMESPACE` | numbered | `MIST0294` | Global using names unknown namespace | NAM |
| `ProgramModelDiagnostics.DuplicateTypeName` | `MIST_PM_DUPLICATE_TYPE` | numbered | `MIST0295` | Duplicate type name | NAM |
| `ProgramModelDiagnostics.UnresolvedIdentifier` | `MIST_PM_UNRESOLVED_IDENTIFIER` | numbered | `MIST0296` | Unresolved identifier | NAM |
| `ProgramModelDiagnostics.AwaitOfNonTask` | `MIST_PM_AWAIT_OF_NON_TASK` | numbered | `MIST0297` | Await of non-task | TYP |
| `ProgramModelDiagnostics.UnresolvedType` | `MIST_PM_UNRESOLVED_TYPE` | numbered | `MIST0298` | Unresolved type | NAM |
| `ProgramModelDiagnostics.AmbiguousType` | `MIST_PM_AMBIGUOUS_TYPE` | numbered | `MIST0299` | Ambiguous type name | NAM |
| `ProgramModelDiagnostics.CyclicTypeAlias` | `MIST_PM_CYCLIC_TYPE_ALIAS` | numbered | `MIST0300` | Cyclic type alias | TYP |
| `ProgramModelDiagnostics.UnresolvedMember` | `MIST_PM_UNRESOLVED_MEMBER` | numbered | `MIST0301` | Unresolved member | NAM |
| `ProgramModelDiagnostics.AmbiguousMember` | `MIST_PM_AMBIGUOUS_MEMBER` | numbered | `MIST0302` | Ambiguous member | NAM |
| `ProgramModelDiagnostics.AmbiguousIdentifier` | `MIST_PM_AMBIGUOUS_IDENTIFIER` | numbered | `MIST0303` | Ambiguous identifier | NAM |
| `ProgramModelDiagnostics.UnknownTypeRefNode` | `MIST_PM_UNKNOWN_TYPE_REF_NODE` | numbered | `MIST0304` | Unrecognized type reference node | ICE |
| `ProgramModelDiagnostics.ReservedTupleElementName` | `MIST_PM_RESERVED_TUPLE_ELEMENT_NAME` | numbered | `MIST0305` | Reserved tuple element name | NAM |
| `ProgramModelDiagnostics.BaseTypeKindMismatch` | `MIST_PM_BASE_TYPE_KIND_MISMATCH` | numbered | `MIST0306` | Base type kind mismatch | DEC |
| `ProgramModelDiagnostics.ConcreteInAbstractList` | `MIST_PM_CONCRETE_IN_ABSTRACT_LIST` | numbered | `MIST0307` | Concrete type in abstract list | DEC |
| `ProgramModelDiagnostics.InvalidBaseType` | `MIST_PM_INVALID_BASE_TYPE` | numbered | `MIST0308` | Invalid base type | DEC |
| `ProgramModelDiagnostics.UnknownAbstractMember` | `MIST_PM_UNKNOWN_ABSTRACT_MEMBER` | retired, unreachable internal fallback | none |  |  |
| `ProgramModelDiagnostics.UnknownMember` | `MIST_PM_UNKNOWN_MEMBER` | retired, unreachable internal fallback | none |  |  |
| `ProgramModelDiagnostics.GetterCannotHaveModifier` | `MIST_PM_GETTER_MODIFIER` | numbered | `MIST0309` | Modifier on getter | DEC |
| `ProgramModelDiagnostics.SetterModifierMustBePrivate` | `MIST_PM_SETTER_MODIFIER` | numbered | `MIST0310` | Non-private setter modifier | DEC |
| `ProgramModelDiagnostics.EmptyAccessorList` | `MIST_PM_EMPTY_ACCESSORS` | numbered | `MIST0311` | Empty accessor list | DEC |
| `ProgramModelDiagnostics.IndexerCannotHaveInit` | `MIST_PM_INDEXER_INIT` | numbered | `MIST0312` | Init accessor on indexer | DEC |
| `ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier` | `MIST_PM_ABSTRACT_ACCESSOR_MODIFIER` | numbered | `MIST0313` | Modifier on abstract accessor | DEC |
| `ProgramModelDiagnostics.AbstractPropertyInitAccessor` | `MIST_PM_ABSTRACT_INIT_ACCESSOR` | numbered | `MIST0314` | Init accessor on abstract property | DEC |
| `ProgramModelDiagnostics.StaticPropertyInitAccessor` | `MIST_PM_STATIC_INIT_ACCESSOR` | numbered | `MIST0315` | Init accessor on static property | DEC |
| `ProgramModelDiagnostics.ConflictingDisciplineModifiers` | `MIST_PM_CONFLICTING_DISCIPLINE` | numbered | `MIST0316` | Conflicting discipline modifiers | DEC |
| `ProgramModelDiagnostics.StampableRequiresStruct` | `MIST_PM_STAMPABLE_REQUIRES_STRUCT` | numbered | `MIST0317` | Stampable on non-struct | DEC |
| `ProgramModelDiagnostics.PureAndLocal` | `MIST_PM_PURE_AND_LOCAL` | numbered | `MIST0318` | Pure combined with local | EFF |
| `ProgramModelDiagnostics.ReadonlyImpliedByPure` | `MIST_PM_READONLY_IMPLIED_BY_PURE` | numbered | `MIST0319` | Redundant readonly with pure | EFF |
| `ProgramModelDiagnostics.LocalRequiresInstance` | `MIST_PM_LOCAL_REQUIRES_INSTANCE` | numbered | `MIST0320` | Local on non-instance member | EFF |
| `ProgramModelDiagnostics.PurityModifierPlacement` | `MIST_PM_PURITY_MODIFIER_PLACEMENT` | numbered | `MIST0321` | Purity modifier misplaced | EFF |
| `ProgramModelDiagnostics.PureRequiresFunctionType` | `MIST_PM_PURE_REQUIRES_FUNCTION_TYPE` | numbered | `MIST0322` | Pure on non-function type | EFF |
| `ProgramModelDiagnostics.AsyncOnlyOnMethods` | `MIST_PM_ASYNC_NOT_ALLOWED` | numbered | `MIST0323` | Async on non-method | DEC |
| `ProgramModelDiagnostics.IntrinsicNotValidOnMember` | `MIST_PM_INTRINSIC_NOT_VALID` | numbered | `MIST0324` | Intrinsic on unsupported member | DEC |
| `ProgramModelDiagnostics.MethodMustHaveBody` | `MIST_PM_METHOD_MUST_HAVE_BODY` | numbered | `MIST0325` | Method without body | DEC |
| `ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody` | `MIST_PM_INTRINSIC_HAS_BODY` | numbered | `MIST0326` | Body on intrinsic member | DEC |
| `ProgramModelDiagnostics.IntrinsicReservedForStandard` | `MIST_PM_INTRINSIC_RESERVED` | numbered | `MIST0327` | Intrinsic outside Standard library | DEC |
| `ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType` | `MIST_PM_INTRINSIC_NOT_IN_INTRINSIC_TYPE` | numbered | `MIST0328` | Intrinsic member in non-intrinsic type | DEC |
| `ProgramModelDiagnostics.IntrinsicMemberMustBePublic` | `MIST_PM_INTRINSIC_NOT_PUBLIC` | numbered | `MIST0329` | Non-public intrinsic member | DEC |
| `ProgramModelDiagnostics.IndexerCannotHaveAutoAccessor` | `MIST_PM_INDEXER_AUTO_ACCESSOR` | numbered | `MIST0330` | Auto-accessor on indexer | DEC |
| `ProgramModelDiagnostics.CastOperatorMissingParameter` | `MIST_PM_CAST_OP_MISSING_PARAM` | numbered | `MIST0331` | Cast operator without parameter | DEC |
| `ProgramModelDiagnostics.CastOperatorTooManyParameters` | `MIST_PM_CAST_OP_TOO_MANY_PARAMS` | numbered | `MIST0332` | Cast operator with extra parameters | DEC |
| `ProgramModelDiagnostics.OperatorMustBeStatic` | `MIST_PM_OPERATOR_MUST_BE_STATIC` | numbered | `MIST0333` | Non-static operator | DEC |
| `ProgramModelDiagnostics.OperatorWrongParameterCount` | `MIST_PM_OPERATOR_WRONG_PARAM_COUNT` | numbered | `MIST0334` | Operator parameter count mismatch | DEC |
| `ProgramModelDiagnostics.OperatorMustHaveBody` | `MIST_PM_OPERATOR_MUST_HAVE_BODY` | retired, the parser refuses the shape first | none |  |  |
| `ProgramModelDiagnostics.MemberNotAllowedOnAbstract` | `MIST_PM_MEMBER_NOT_ALLOWED_ON_ABSTRACT` | numbered | `MIST0335` | Member not allowed on abstract | DEC |
| `ProgramModelDiagnostics.TypeSlotNotAllowedOnConcrete` | `MIST_PM_TYPE_SLOT_NOT_ALLOWED_ON_CONCRETE` | numbered | `MIST0336` | Type slot on concrete type | DEC |
| `ProgramModelDiagnostics.DuplicateDefaultBlock` | `MIST_PM_DUPLICATE_DEFAULT_BLOCK` | numbered | `MIST0337` | Duplicate default block | DEC |
| `ProgramModelDiagnostics.UnresolvedConstraintTarget` | `MIST_PM_UNRESOLVED_CONSTRAINT_TARGET` | numbered | `MIST0338` | Unknown constraint target | NAM |
| `ProgramModelDiagnostics.ConflictingConstraints` | `MIST_PM_CONFLICTING_CONSTRAINTS` | numbered | `MIST0339` | Conflicting constraints | TYP |
| `ProgramModelDiagnostics.DuplicateConstraint` | `MIST_PM_DUPLICATE_CONSTRAINT` | numbered | `MIST0340` | Duplicate constraint | TYP |
| `ProgramModelDiagnostics.ThisTierOnConstructor` | `MIST_PM_THIS_TIER_ON_CONSTRUCTOR` | numbered | `MIST0341` | This tier on constructor | OWN |
| `ProgramModelDiagnostics.DuplicateExtensionMember` | `MIST_PM_DUPLICATE_EXTENSION_MEMBER` | retired, see `DuplicateExtensionMethod` and `DuplicateExtensionProperty` | none |  |  |
| `ProgramModelDiagnostics.ExtensionPropertyCannotHaveAutoAccessor` | `MIST_PM_EXTENSION_AUTO_ACCESSOR` | numbered | `MIST0342` | Auto-accessor on extension property | DEC |
| `ProgramModelDiagnostics.FieldKeywordOutsideAccessor` | `MIST_PM_FIELD_OUTSIDE_ACCESSOR` | numbered | `MIST0343` | Field keyword outside accessor | SYN |
| `ProgramModelDiagnostics.MemberNameMatchesEnclosingType` | `MIST_PM_MEMBER_NAME_IS_TYPE_NAME` | numbered | `MIST0344` | Member named after enclosing type | NAM |
| `ProgramModelDiagnostics.PatternTypeNotAConstituent` | `MIST_PM_PATTERN_TYPE_NOT_A_CONSTITUENT` | numbered | `MIST0345` | Pattern type not a constituent | TYP |
| `ProgramModelDiagnostics.SwitchExpressionNeedsAnArm` | `MIST_PM_SWITCH_NO_ARMS` | numbered | `MIST0346` | Switch expression without arms | TYP |
| `ProgramModelDiagnostics.SwitchArmsNoCommonType` | `MIST_PM_SWITCH_ARMS_NO_COMMON_TYPE` | numbered | `MIST0347` | Switch arms lack common type | TYP |
| `ProgramModelDiagnostics.PatternBindingNotEstablished` | `MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED` | numbered | `MIST0348` | Binding inside or/not pattern | FLW |
| `ProgramModelDiagnostics.PatternSubpatternNotStorage` | `MIST_PM_PATTERN_SUBPATTERN_NOT_STORAGE` | numbered | `MIST0349` | Subpattern target not storage | TYP |
| `ProgramModelDiagnostics.PatternSubpatternNotInstance` | `MIST_PM_PATTERN_SUBPATTERN_NOT_INSTANCE` | numbered | `MIST0350` | Subpattern target is static | TYP |
| `ProgramModelDiagnostics.TuplePatternArityMismatch` | `MIST_PM_TUPLE_PATTERN_ARITY` | numbered | `MIST0351` | Tuple pattern arity mismatch | TYP |
| `ProgramModelDiagnostics.TuplePatternScrutineeNotATuple` | `MIST_PM_TUPLE_PATTERN_SCRUTINEE` | numbered | `MIST0352` | Tuple pattern on non-tuple | TYP |
| `ProgramModelDiagnostics.ReceiverNotSliceable` | `MIST_PM_RECEIVER_NOT_SLICEABLE` | numbered | `MIST0353` | Receiver not sliceable | TYP |
| `ProgramModelDiagnostics.ReceiverNotIndexable` | `MIST_PM_RECEIVER_NOT_INDEXABLE` | numbered | `MIST0354` | Receiver not indexable | TYP |
| `ProgramModelDiagnostics.ReceiverNotCountable` | `MIST_PM_RECEIVER_NOT_COUNTABLE` | numbered | `MIST0355` | Receiver not countable | TYP |
| `ProgramModelDiagnostics.ListPatternScrutineeNotIndexable` | `MIST_PM_LIST_PATTERN_SCRUTINEE` | numbered | `MIST0356` | List pattern on non-indexable | TYP |
| `ProgramModelDiagnostics.ListPatternMultipleSlices` | `MIST_PM_LIST_PATTERN_MULTIPLE_SLICES` | numbered | `MIST0357` | Multiple slices in list pattern | SYN |
| `ProgramModelDiagnostics.FunctionTypeNeedsAResult` | `MIST_PM_FUNCTION_TYPE_NO_RESULT` | numbered | `MIST0358` | Function type without result | TYP |
| `ProgramModelDiagnostics.LambdaArityMismatch` | `MIST_PM_LAMBDA_ARITY` | numbered | `MIST0359` | Lambda arity mismatch | TYP |
| `ProgramModelDiagnostics.LambdaParameterNeedsAType` | `MIST_PM_LAMBDA_PARAMETER_NO_TYPE` | numbered | `MIST0360` | Untyped lambda parameter | TYP |
| `ProgramModelDiagnostics.LambdaReturnsNoCommonType` | `MIST_PM_LAMBDA_RETURNS_NO_COMMON_TYPE` | numbered | `MIST0361` | Lambda returns lack common type | TYP |
| `ProgramModelDiagnostics.FunctionCallArityMismatch` | `MIST_PM_FUNCTION_CALL_ARITY` | numbered | `MIST0362` | Function call arity mismatch | TYP |
| `ProgramModelDiagnostics.NotCallable` | `MIST_PM_NOT_CALLABLE` | numbered | `MIST0363` | Not callable | TYP |
| `ProgramModelDiagnostics.NotCallableExtensionNotImported` | `MIST_PM_NOT_CALLABLE` | merged into `NotCallable` | none |  |  |
| `ProgramModelDiagnostics.ValueNotCallable` | `MIST_PM_NOT_CALLABLE` | numbered | `MIST0364` | Value not callable | TYP |
| `ProgramModelDiagnostics.DeconstructionArityMismatch` | `MIST_PM_DECONSTRUCTION_ARITY` | numbered | `MIST0365` | Deconstruction arity mismatch | TYP |
| `ProgramModelDiagnostics.DeconstructionSourceNotATuple` | `MIST_PM_DECONSTRUCTION_SOURCE` | numbered | `MIST0366` | Deconstruction of non-tuple | TYP |
| `ProgramModelDiagnostics.DuplicateLocalVariable` | `MIST_PM_DUPLICATE_LOCAL` | numbered | `MIST0367` | Duplicate local variable | NAM |
| `ProgramModelDiagnostics.DuplicateLocalFunction` | `MIST_PM_DUPLICATE_LOCAL_FUNCTION` | numbered | `MIST0368` | Duplicate local function | NAM |
| `ProgramModelDiagnostics.LocalFunctionModifier` | `MIST_PM_LOCAL_FUNCTION_MODIFIER` | numbered | `MIST0369` | Modifier on local function | DEC |
| `ProgramModelDiagnostics.LocalFunctionGeneric` | `MIST_PM_LOCAL_FUNCTION_GENERIC` | numbered | `MIST0370` | Generic local function | DEC |
| `ProgramModelDiagnostics.ThisOutsideType` | `MIST_PM_THIS_OUTSIDE_TYPE` | numbered | `MIST0371` | This outside a type | NAM |
| `ProgramModelDiagnostics.ThisInStaticMember` | `MIST_PM_THIS_IN_STATIC` | numbered | `MIST0372` | This in static member | NAM |
| `ProgramModelDiagnostics.ForEachElementNotInferable` | `MIST_PM_FOREACH_ELEMENT_NOT_INFERABLE` | numbered | `MIST0373` | Foreach element type not inferable | TYP |
| `ProgramModelDiagnostics.RefLocalNeedsInitializer` | `MIST_PM_REF_LOCAL_NEEDS_INITIALIZER` | numbered | `MIST0374` | Ref local without initializer | OWN |
| `ProgramModelDiagnostics.EmptyArrayLiteralNeedsTargetType` | `MIST_PM_EMPTY_ARRAY_LITERAL_NO_TARGET` | numbered | `MIST0375` | Empty array literal needs target | TYP |
| `ProgramModelDiagnostics.ArrayLiteralNoCommonElementType` | `MIST_PM_ARRAY_LITERAL_NO_COMMON_TYPE` | numbered | `MIST0376` | Array elements lack common type | TYP |
| `ProgramModelDiagnostics.NumericLiteralOutOfRange` | `MIST_PM_NUMERIC_LITERAL_OUT_OF_RANGE` | numbered | `MIST0377` | Numeric literal out of range | TYP |
| `ProgramModelDiagnostics.TargetTypedNewNeedsTargetType` | `MIST_PM_TARGET_TYPED_NEW_NO_TARGET` | numbered | `MIST0378` | Target-typed new without target | TYP |
| `ProgramModelDiagnostics.DefaultNeedsTargetType` | `MIST_PM_DEFAULT_NO_TARGET` | numbered | `MIST0379` | Default without target type | TYP |
| `ProgramModelDiagnostics.TargetTypeNotConstructible` | `MIST_PM_TARGET_TYPE_NOT_CONSTRUCTIBLE` | numbered | `MIST0380` | Target type not constructible | TYP |
| `ProgramModelDiagnostics.InitializerTargetNotAssignable` | `MIST_PM_INITIALIZER_TARGET_NOT_ASSIGNABLE` | numbered | `MIST0381` | Initializer target not assignable | TYP |
| `ProgramModelDiagnostics.InitializerTargetNotInstance` | `MIST_PM_INITIALIZER_TARGET_NOT_INSTANCE` | numbered | `MIST0382` | Initializer target is static | TYP |
| `ProgramModelDiagnostics.CollectionInitializerNotSupported` | `MIST_PM_COLLECTION_INITIALIZER` | numbered | `MIST0383` | Collection initializer not supported | BCK |
| `ProgramModelDiagnostics.DuplicateAssemblyName` | `MIST_PM_DUPLICATE_ASSEMBLY_NAME` | numbered | `MIST0384` | Duplicate assembly name | DEP |
| `ProgramModelDiagnostics.NoApplicableOverload` | `MIST_PM_NO_APPLICABLE_OVERLOAD` | numbered | `MIST0385` | No overload with that arity | TYP |
| `ProgramModelDiagnostics.AmbiguousOverload` | `MIST_PM_AMBIGUOUS_OVERLOAD` | numbered | `MIST0386` | Ambiguous overload | TYP |
| `ProgramModelDiagnostics.MethodGroupNotConverted` | `MIST_PM_METHOD_GROUP_NOT_CONVERTED` | numbered | `MIST0387` | Method group not converted | TYP |
| `ProgramModelDiagnostics.TypeArgumentsOnANonMethod` | `MIST_PM_TYPE_ARGUMENTS_ON_A_NON_METHOD` | numbered | `MIST0388` | Type arguments on non-method | TYP |
| `ProgramModelDiagnostics.MethodGroupAmbiguous` | `MIST_PM_METHOD_GROUP_AMBIGUOUS` | numbered | `MIST0389` | Ambiguous method group conversion | TYP |
| `ProgramModelDiagnostics.UnresolvableOverload` | `MIST_PM_UNRESOLVABLE_OVERLOAD` | numbered | `MIST0390` | No convertible overload | TYP |
| `ProgramModelDiagnostics.TypeArgumentsNotInferred` | `MIST_PM_TYPE_ARGUMENTS_NOT_INFERRED` | numbered | `MIST0391` | Type arguments not inferred | TYP |
| `ProgramModelDiagnostics.TypeArgumentsConflict` | `MIST_PM_TYPE_ARGUMENTS_CONFLICT` | numbered | `MIST0392` | Conflicting type argument inference | TYP |
| `ProgramModelDiagnostics.TypeArgumentCountMismatch` | `MIST_PM_TYPE_ARGUMENT_COUNT_MISMATCH` | numbered | `MIST0393` | Type argument count mismatch | TYP |
| `ProgramModelDiagnostics.UnknownAtStoragePosition` | `MIST_PM_UNKNOWN_AT_STORAGE` | numbered | `MIST0394` | Unknown as storage type | TYP |
| `ProgramModelDiagnostics.UnknownViewHidesWriter` | `MIST_PM_UNKNOWN_VIEW_HIDES_WRITER` | numbered | `MIST0395` | Unknown view hides writer | TYP |
| `ProgramModelDiagnostics.ConformanceSourceNotConcrete` | `MIST_PM_CONFORMANCE_NOT_CONCRETE` | numbered | `MIST0396` | Conformance subject not concrete | DEC |
| `ProgramModelDiagnostics.ConformanceTargetNotAbstract` | `MIST_PM_CONFORMANCE_NOT_ABSTRACT` | numbered | `MIST0397` | Conformance target not abstract | DEC |
| `ProgramModelDiagnostics.EnumVariantValueNotInferable` | `MIST_PM_ENUM_VALUE_NOT_INFERABLE` | numbered | `MIST0398` | Enum variant value not inferable | DEC |
| `ProgramModelDiagnostics.EnumVariantValueNotCountable` | `MIST_PM_ENUM_VALUE_NOT_COUNTABLE` | numbered | `MIST0399` | Enum variant value not countable | DEC |
| `ProgramModelDiagnostics.EnumVariantValueOutOfRange` | `MIST_PM_ENUM_VALUE_OUT_OF_RANGE` | numbered | `MIST0400` | Enum variant value out of range | DEC |
| `ProgramModelDiagnostics.ConformanceUnresolvedSlot` | `MIST_PM_CONFORMANCE_UNRESOLVED_SLOT` | numbered | `MIST0401` | Unknown conformance slot | NAM |
| `ProgramModelDiagnostics.ConformanceMappingUnsatisfied` | `MIST_PM_CONFORMANCE_MAPPING_UNSATISFIED` | numbered | `MIST0402` | Conformance mapping unsatisfied | DEC |
| `ProgramModelDiagnostics.AsConversionNoRelationship` | `MIST_PM_AS_NO_RELATIONSHIP` | numbered | `MIST0403` | As conversion has no relationship | TYP |
| `ProgramModelDiagnostics.MultipleUnbracketedPacks` | `MIST_VAR_0001` | numbered | `MIST0404` | Multiple bracket-free packs | TYP |
| `ProgramModelDiagnostics.MultipleParamsParameters` | `MIST_VAR_0001` | numbered | `MIST0405` | Multiple params parameters | DEC |
| `ProgramModelDiagnostics.PackInTypePosition` | `MIST_VAR_0002` | numbered | `MIST0406` | Pack used as single type | TYP |
| `ProgramModelDiagnostics.DisallowedDependentDomain` | `MIST_DT_0001` | numbered | `MIST0407` | Disallowed dependent domain | TYP |
| `ProgramModelDiagnostics.DuplicateTypeParameter` | `MIST_PM_DUPLICATE_TYPE_PARAMETER` | numbered | `MIST0408` | Duplicate type parameter | NAM |
| `ProgramModelDiagnostics.UnprovenPackIndex` | `MIST_VAR_0003` | numbered | `MIST0409` | Unproven pack index | TYP |
| `ProgramModelDiagnostics.PackIndexOutOfBounds` | `MIST_VAR_0004` | numbered | `MIST0410` | Pack index out of bounds | TYP |
| `ProgramModelDiagnostics.TooFewTypeArgumentsForPack` | `MIST_VAR_0005` | numbered | `MIST0411` | Too few type arguments | TYP |
| `ProgramModelDiagnostics.SpreadStemCollision` | `MIST_VAR_0007` | numbered | `MIST0412` | Spread member name collision | NAM |
| `ProgramModelDiagnostics.ParamsArrayNotLast` | `MIST_VAR_0012` | numbered | `MIST0413` | Params parameter not last | DEC |
| `ProgramModelDiagnostics.ParamsParameterMalformed` | `MIST_VAR_0013` | numbered | `MIST0414` | Malformed params parameter | DEC |
| `ProgramModelDiagnostics.UnprovableSpread` | `MIST_VAR_0011` | numbered | `MIST0415` | Unprovable spread | TYP |
| `ProgramModelDiagnostics.AnonymousMemberNotNameable` | `MIST_PM_ANONYMOUS_MEMBER_NOT_NAMEABLE` | numbered | `MIST0416` | Unnamed anonymous type member | SYN |
| `ProgramModelDiagnostics.AbstractConstructed` | `MIST_PM_ABSTRACT_CONSTRUCTED` | numbered | `MIST0417` | Abstract type constructed | TYP |
| `ProgramModelDiagnostics.NewtypeCloneInvalid` | `MIST_PM_NEWTYPE_CLONE_INVALID` | numbered | `MIST0418` | Invalid newtype clone | DEC |
| `ProgramModelDiagnostics.NewtypeSourceNotSupported` | `MIST_PM_NEWTYPE_SOURCE_NOT_SUPPORTED` | numbered | `MIST0419` | Unsupported newtype source | DEC |
| `ProgramModelDiagnostics.SpreadValueParameter` | `MIST_PM_SPREAD_VALUE_PARAMETER` | numbered | `MIST0420` | Spread suffix on value parameter | TYP |
| `ProgramModelDiagnostics.SpreadNonPackArgument` | `MIST_PM_SPREAD_NON_PACK_ARGUMENT` | numbered | `MIST0421` | Spread into non-pack slot | TYP |
