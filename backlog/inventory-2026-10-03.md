# Reconciliation, 2026-10-03

Snapshot of [the canonical issue ledger](issues.yaml), using Mist code/audit baseline `da1aaa8fd416bd3bdf93e496281e7b2cbf52594c`,
Storm `b4e5853ccca45623e59fcdb9ee0afd6e90e836ce`.
This dated view is evidence of this pass; `issues.yaml` owns later updates.
The ProgramModel guidance mismatch was corrected during the pass in published documentation
commit `4ff6310c`; it is archived rather than counted as outstanding.

## Result

205 distinct outstanding records across 15 reconciled source documents:

- **52 confirmed gaps** — An expected current contract is absent or violated, supported by precise current source evidence or an executed probe.

- **53 unverified leads** — A retained claim, historical incident or suspected gap needs targeted confirmation or attribution before selecting a fix.

- **43 policy decisions** — An unresolved language/product policy choice; do not implement an invented policy to close it.

- **57 optional improvements** — A deferred feature, precision/performance exploration or refactor without a demonstrated required correctness fix.

136 archived groups explain completed, stale and superseded leads. Group counts
are not counts of individual removed checklist bullets. Unverified records and optional
work are not counted as confirmed defects. Spec task documents are excluded because the
owner confirms they are implemented.

## Evidence and limits

Three parallel audits inspected binder/provider/Storm, analysis, and backend/general/retirement
records; the coordinator inspected flake and test-runner records and merged overlaps by
observable obligation. Every record links its source selectors and evidence. Many historical
unchecked tasks describe current implementation and have been archived. Commits covering
09–16 are on the inspected main; their old broad gaps are not scheduled again, while
distinct remaining cases are tracked individually. This does not assert their chats ended.

The baseline solution built with zero warnings and errors. Four focused stdlib-cache and
nested-build guard tests passed in ledger `20261003-230913`. Auditors executed focused CLI
probes to distinguish live gaps from stale reports; each row distinguishes those results
from source inspection. No full-suite run or exhaustive search for undiscovered defects is
claimed. This inventory counts bounded tracked obligations, not every possible bug.
The formatter and diff checks passed. Validation maps all 106 analysis task bullets, 155
binder unchecked/partial markers, 173 emitter tasks and 47 general sections to canonical IDs;
source/evidence paths and the parsed YAML also validate.

## Outstanding records

| ID | Status | Obligation |
| --- | --- | --- |
| MIST-001 | confirmed | Add tuple-product exhaustiveness reasoning |
| MIST-002 | confirmed | Report missing void in value-covered optional switches |
| MIST-003 | confirmed | Model CLR optional arguments before enforcing projected call/index types |
| MIST-004 | confirmed | Judge collection-initializer Add refinement and parameter constraints |
| MIST-005 | confirmed | Refuse valued returns from void bodies and top-level statements at source |
| MIST-006 | confirmed | Validate equality refinement constants against declaration domain |
| MIST-007 | confirmed | Reject duplicate operator and conversion signatures |
| MIST-008 | confirmed | Inject semantic defaults into uninitialized user-constructor storage |
| MIST-009 | confirmed | Do not let default block entries satisfy constructor assignment |
| MIST-010 | confirmed | Check accessibility of explicit constructor chains |
| MIST-011 | confirmed | Close associated type slots in member signatures and compare generic constraints |
| MIST-012 | confirmed | Extend switch-arm containment across unsupported value domains |
| MIST-013 | confirmed | Propagate readonly parameter tier through deep member receivers |
| MIST-014 | confirmed | Resolve extension receiver members in symbolic where bounds |
| MIST-015 | confirmed | Suppress derivative generic local-function diagnostics |
| MIST-016 | confirmed | Bind and substitute other-parameter/dependent symbolic bounds |
| MIST-017 | confirmed | Check readonly IFormattable behavior through generic closures |
| MIST-018 | confirmed | Mention owned receiver alternative in readonly mutable-call diagnostic |
| MIST-019 | confirmed | Expose a dependent alias parameter by the alias-declared name |
| MIST-020 | confirmed | Report ambiguous bare names in expression position |
| MIST-021 | confirmed | Close parameterized constructor factories over a generic type’s own arguments |
| MIST-022 | confirmed | Preserve optional-reference context keys through generic consumers |
| MIST-023 | confirmed | Supply CLR iteration boilerplate through inherited abstract protocols |
| MIST-024 | confirmed | Replace subtree drop elision for linear parameters and fields |
| MIST-025 | confirmed | Consume a linear local when it enters a scoped with block |
| MIST-026 | confirmed | Reject operator modifiers outside the specified public static form |
| MIST-027 | confirmed | Emit optional reference annotations consistent with the generated project |
| MIST-028 | confirmed | Entail all constraint kinds for a forwarded pack or pack element |
| MIST-029 | confirmed | Support both specified user-defined sliceable protocols |
| MIST-030 | confirmed | Preserve a valid readonly newtype modifier when cloning its source |
| MIST-031 | confirmed | Distinguish sized constituents that share a CLR array representation |
| MIST-032 | confirmed | Distinguish declared non-instance members from unknown storage targets |
| MIST-033 | confirmed | Use a parenthesized switch expression’s result type for following member access |
| MIST-034 | confirmed | Retain universal parameterless ToString beside user overloads |
| MIST-035 | confirmed | Preserve explicitly sized lengths on direct call results |
| MIST-036 | confirmed | Duplicate local diagnostics lack source location |
| MIST-037 | confirmed | Local declaration discards become real named bindings |
| MIST-038 | confirmed | Foreach body lacks an independent shadowing scope |
| MIST-039 | confirmed | Typed for initializers do not contextually target their value |
| MIST-040 | confirmed | Best common type inference stops at identity |
| MIST-041 | confirmed | Assignment expression retains RHS type rather than storage type |
| MIST-042 | confirmed | Non-indexable intrinsic access lacks its owning refusal |
| MIST-043 | confirmed | Large tuple type-test spelling uses flat ValueTuple arity |
| MIST-044 | confirmed | Noncallable contextual arguments across overload candidates |
| MIST-045 | confirmed | Qualified base type references do not parse |
| MIST-046 | confirmed | Qualified named dependent domains do not parse |
| MIST-047 | confirmed | CLR non-enum constant field values are not projected |
| MIST-048 | confirmed | CLR interface default bodies are not marked as default implementations |
| MIST-049 | confirmed | Tuple names on CLR base/interface/constraint signatures are missing |
| MIST-050 | confirmed | Rule literals overlapping pattern tokens can never match |
| MIST-051 | confirmed | Namespaced rule identity is lost during lookup/lowering/emission |
| MIST-052 | confirmed | Ordinary unreachable alternatives use warning severity rather than required error |
| MIST-053 | unverified | Revalidate projected System.Object as the classifier top type |
| MIST-054 | unverified | Validate purity of setters run by default synthesis |
| MIST-055 | unverified | Revalidate lone bodied getter backing-field contract |
| MIST-056 | unverified | Correct defaultability diagnostics for intrinsic constructions and closed bases |
| MIST-057 | unverified | Revalidate zero-argument value-pack constructor eligibility |
| MIST-058 | unverified | Revalidate bodiless operator and static abstract slot obligations |
| MIST-059 | unverified | Revalidate tuple checks after pack closure |
| MIST-060 | unverified | Define operator discovery through conformances/extensions |
| MIST-061 | unverified | Revalidate borrowed call while a frozen lease exists |
| MIST-062 | unverified | Establish per-field moves, leases and parent composite states |
| MIST-063 | unverified | Judge this passed as an argument by receiver tier |
| MIST-064 | unverified | Extend stack-backed span escape checking to stores |
| MIST-065 | unverified | Verify refinement-union runtime narrowing implications |
| MIST-066 | unverified | Revalidate static extension member callability |
| MIST-067 | unverified | Specify Span to ReadOnlySpan conversion |
| MIST-068 | unverified | Revalidate purity aliases crossing ordinary call arguments/results |
| MIST-069 | unverified | Revalidate extension method-group preservation under CLR shadowing |
| MIST-070 | unverified | Merge duplicate purity findings for one source root cause |
| MIST-071 | unverified | Align book entry-point ambiguity example with ordinal suggestions |
| MIST-072 | unverified | Identify external runtime entry attributes for context requirements |
| MIST-073 | unverified | Preserve union payload narrowing after widening to unknown |
| MIST-074 | unverified | Admit named compile-time constant references as dependent arguments |
| MIST-075 | unverified | Expose dependency diagnostics during inspect and while resolution proceeds |
| MIST-076 | unverified | Settle linear handoff around receiver failure and untyped trailing arguments |
| MIST-077 | unverified | Make generated multi-project csproj entry point selection agree with native compilation |
| MIST-078 | unverified | Extend cleanup planning to currently excluded binding forms |
| MIST-079 | unverified | Lower or source-diagnose a ref operator parameter |
| MIST-080 | unverified | Diagnose escaping local ref returns at Mist source |
| MIST-081 | unverified | Recheck specific variadic shapes outside the delivered constraint/specialization closeout |
| MIST-082 | unverified | Revalidate inference under mutually converting argument types |
| MIST-083 | unverified | Revalidate initializer duplicate names and nested or unknown targets |
| MIST-084 | unverified | Revalidate references to nested aliases and newtypes |
| MIST-085 | unverified | Revalidate void-to-unit method-group adaptation |
| MIST-086 | unverified | Revalidate member filtering for readonly generic storage |
| MIST-087 | unverified | Revalidate widening a readonly generic struct to unknown |
| MIST-088 | unverified | Revalidate enum conversion to a union underlying type |
| MIST-089 | unverified | Variadic generic method groups as function values |
| MIST-090 | unverified | Inherited projected CLR members and construction coverage |
| MIST-091 | unverified | Revalidate ref-local liveness for field and element referents |
| MIST-092 | unverified | Revalidate source refusal or lowering of ref fields |
| MIST-093 | unverified | Revalidate deep composite and alias ref-struct legality |
| MIST-094 | unverified | Revalidate implicit cloning into owned fields and field assignments |
| MIST-095 | unverified | Revalidate ownership containment through nested generic wrappers |
| MIST-096 | unverified | Revalidate arguments of distant generic struct ancestors |
| MIST-097 | unverified | Separate unrelated constraint errors from newtype cast diagnostics |
| MIST-098 | unverified | Keyword type literals do not redirect static lookup |
| MIST-099 | unverified | Tuple element-name mismatch is emitted but warnings are absent |
| MIST-100 | unverified | Generic/operator tie resolution beyond implemented builtin selection |
| MIST-101 | unverified | Nested inline-block AST/capture projection is not proved complete |
| MIST-102 | unverified | Identify the intermittent snippet-emitter null reference |
| MIST-103 | unverified | Identify the unnamed Mist test failure observed on 2026-08-02 |
| MIST-104 | unverified | Identify the unnamed Mist test failure observed on 2026-09-13 |
| MIST-105 | unverified | Identify the unnamed Storm test failure observed on 2026-08-04 |
| MIST-106 | decision | Choose non-exhaustive switch severity |
| MIST-107 | decision | Decide casts entering unions through refined constituents |
| MIST-108 | decision | Specify ambiguous union entry selection and diagnostic |
| MIST-109 | decision | Set protected receiver accessibility policy |
| MIST-110 | decision | Choose source refusal for private/protected top-level types |
| MIST-111 | decision | Specify mixed extension declaration collision rules |
| MIST-112 | decision | Permit default block entries naming auto-properties |
| MIST-113 | decision | Specify default(refinement) and refinement default constraints |
| MIST-114 | decision | Decide whether explicit conformance mappings must target public members |
| MIST-115 | decision | Define namespace scoping of conformance declarations |
| MIST-116 | decision | Choose always-true type-test diagnostic policy |
| MIST-117 | decision | Choose impossible-value switch arm diagnostic policy |
| MIST-118 | decision | Choose duplicate enum arm severity |
| MIST-119 | decision | Define readonly callability for closed unknown generic receivers |
| MIST-120 | decision | Adopt stricter constructor this/field definite assignment |
| MIST-121 | decision | Set owned non-owned-type mutable call/cooperation restrictions |
| MIST-122 | decision | Reconcile plain linear parameter transfer across analysis and cleanup |
| MIST-123 | decision | Choose direct reassignment rules for readonly/borrowed parameters |
| MIST-124 | decision | Resolve refined filler input exception versus banked refusal |
| MIST-125 | decision | Decide whether mutable classes may derive from unchanged readonly classes |
| MIST-126 | decision | Choose ownership transfer when storing into a non-owning holder |
| MIST-127 | decision | Specify additional accessor modifier spelling |
| MIST-128 | decision | Decide readonly function-type capability |
| MIST-129 | decision | Choose purity of invariant explicit primitive ToString |
| MIST-130 | decision | Prove or specify refined ambient clause compatibility |
| MIST-131 | decision | Specify escaped contextual function lifetime |
| MIST-132 | decision | Decide transparent lifting of open generic context keys |
| MIST-133 | decision | Decide whether inserted Clone is context-transparent |
| MIST-134 | decision | Choose context behavior when foreign code disposes a Mist value |
| MIST-135 | decision | Choose how overwriting a live linear value discharges it |
| MIST-136 | decision | Specify ToString and formatting for absent or null values |
| MIST-137 | decision | Choose whether formatting dispatches to a declared struct ToString |
| MIST-138 | decision | Define currently meaningless type/cast modifier combinations |
| MIST-139 | decision | Choose runtime semantics for Standard universal placeholder bodies |
| MIST-140 | decision | Define retroactive conformance to a variadic abstract |
| MIST-141 | decision | Choose relational subpattern behavior for unknown-typed members |
| MIST-142 | decision | Specify readonly ref method semantics and lowering |
| MIST-143 | decision | Specify unsupported class and struct relationship forms |
| MIST-144 | decision | Specify by-ref widening of flattened struct ancestors |
| MIST-145 | decision | General const declaration and dependent const-reference syntax |
| MIST-146 | decision | Getter and constructor task completion policy |
| MIST-147 | decision | A CLR-discriminated-union projection representation |
| MIST-148 | decision | Inline-block reachability proof contract remains unsound |
| MIST-149 | optional | Improve nonintrinsic refinement default proof |
| MIST-150 | optional | Use getter-specific diagnostics for predicate property reads |
| MIST-151 | optional | Improve opaque storage and empty-generic definite-assignment precision |
| MIST-152 | optional | Model ordered local-function writes and ref/interprocedural assignment effects |
| MIST-153 | optional | Refine unrestricted var receiver/lending tier inference |
| MIST-154 | optional | Extend supported real proof domains conservatively |
| MIST-155 | optional | Extend selected filler predicate implication beyond supported identities |
| MIST-156 | optional | Follow more sequence operations only with verified identities |
| MIST-157 | optional | Lift return values from solved refinement flow |
| MIST-158 | optional | Extend regex/literal/readonly-field predicate proof carefully |
| MIST-159 | optional | Extend stored-range bound knowledge beyond supported identities |
| MIST-160 | optional | Retain field facts across proven readonly accessor/call effects |
| MIST-161 | optional | Bounded purity desugaring audit remains explicitly unfinished |
| MIST-162 | optional | Purity/local promises on enums and newtypes remain deferred |
| MIST-163 | optional | Regex runtime panic policy and flow-literal sugar are deliberately deferred |
| MIST-164 | optional | Move missing-entry refusal earlier after library probe-model support |
| MIST-165 | optional | Prove imported CLR struct stampability from complete storage metadata |
| MIST-166 | optional | Make the number representation role of Array<void,n> explicit |
| MIST-167 | optional | Bank or lower-tier test currently sample-uncovered grammar forms |
| MIST-168 | optional | Provide context storage isolated by execution flow |
| MIST-169 | optional | Admit optional parenthesized function-type syntax |
| MIST-170 | optional | Consider inheritance support for newtypes |
| MIST-171 | optional | Maintain rejection and invisible-modifier coverage at unit tiers |
| MIST-172 | optional | Emit project references as separate assembly outputs |
| MIST-173 | optional | Qualify emitted field receivers as structural hardening |
| MIST-174 | optional | Strengthen capability evidence where sample output cannot distinguish behavior |
| MIST-175 | optional | Update design/requirements wording that documents superseded compiler architecture |
| MIST-176 | optional | Constructor overload selection coverage |
| MIST-177 | optional | Audit namespace inference below a nested directory |
| MIST-178 | optional | Using-import resolution beyond sample coverage |
| MIST-179 | optional | Strengthen focused coverage of parameter-dependent constructor chains |
| MIST-180 | optional | Improve diagnostic ownership for conflicting constructed inference bounds |
| MIST-181 | optional | Distinguish same-spelled type parameters in inference diagnostics |
| MIST-182 | optional | Consider foreign CLR closings for parameterized constructor constraints |
| MIST-183 | optional | Strengthen coverage of generic and method-level constructor constraints |
| MIST-184 | optional | Generate Standard.Void from its Mist declaration |
| MIST-185 | optional | Align foreach dependency ordering with CLR projection |
| MIST-186 | optional | CLR declaration projection coverage |
| MIST-187 | optional | Remove process-wide test-state dependencies |
| MIST-188 | optional | Identify pre-marker lexer output during source discovery |
| MIST-189 | optional | Improve concurrent SDK cache publication |
| MIST-190 | optional | AST-free bound contracts, persistent scopes and bound-node origin API |
| MIST-191 | optional | Additional local-function syntax for ownership/context/attributes |
| MIST-192 | optional | Scrutinee type narrowing beyond introduced pattern bindings |
| MIST-193 | optional | Unify refinement predicate AST wrapper with AndPattern |
| MIST-194 | optional | Reprofile memoization coverage before tuning it |
| MIST-195 | optional | Reduce Roslyn compilation cost through caching or another backend |
| MIST-196 | optional | System.Tuple reference-tuple structural reshaping |
| MIST-197 | optional | Finish removing parallel pipeline AST arrays |
| MIST-198 | optional | Always populate compilation-unit project metadata |
| MIST-199 | optional | First-class delimited-list grammar/AST syntax |
| MIST-200 | optional | Remove obsolete repeated-label scalar aliases |
| MIST-201 | optional | Document bounded reachability and literal/token alias limits |
| MIST-202 | optional | Left-recursion diagnostic number differs from old design table |
| MIST-203 | optional | Hand-built forwarding-only AST aliases lack canonical capture ownership |
| MIST-204 | optional | Mixed token/node labelled choices need an AST union shape |
| MIST-205 | optional | Build a Mist-native test runner and its tooling |
