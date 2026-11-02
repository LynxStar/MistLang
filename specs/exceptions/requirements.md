# Results and foreign failure boundaries

## Status and scope

Specification for the owner decision MIST-1048. The owner decisions establish
the requirements below; the policy decisions for this feature are settled.
Selected APIs and implementation considerations are in [design.md](design.md).
This is a language contract, not a claim that the current compiler implements it.

Mist code is panic free. FFI lowers that guarantee. Mist-defined operations make
their ordinary failure cases explicit through types and refinements; foreign
code can fail outside those contracts. A method written in Mist that invokes
foreign code, directly or through another method, inherits that exposure.

The guarantee concerns language-level failures under the runtime's execution
assumptions. It does not promise termination, unlimited memory or stack, recovery
from process termination, or correct behavior from a defective compiler. Purity
and panic freedom are separate properties.

## Requirement 1 Ordinary failure values

1. Standard SHALL provide `Result<T, E>` to represent either a successful value
   of type `T` or an error value of type `E`.
2. Methods SHALL return, accept, store, transform, and pattern-match Results as
   ordinary values. Errors SHALL NOT require exception inheritance or a special
   throwing method declaration.
3. Mist SHALL NOT introduce source-language `throw`, `try`, `catch`, or `finally`
   constructs for this feature. It SHALL NOT provide a general panic recovery
   construct.
4. Result SHALL retain an internal success/error discriminant, including when
   success is optional or `void`, success and error payload types overlap, or
   the successful payload is another Result. Ordinary callers SHALL NOT need
   a public `Success<T>` wrapper. Construction and correlated tuple inspection
   are specified below.
5. A Result SHALL NOT expose an unconditional success or error payload accessor
   that can fail when the opposite alternative is present. This includes
   `unwrap`, a throwing cast, an implicit conversion to the success payload, and
   an accessor with a different name but the same unsafe behavior.
6. Result SHALL NOT be defaultable. Default construction SHALL NOT manufacture
   a success, failure, or observable invalid third state. Initialization SHALL
   select a valid alternative through the construction API or conversion rules.
7. User-defined error types SHALL be admissible as `E`. An API MAY name a
   domain-specific error type, such as ParseError or SaveError, so callers can
   distinguish failure cases through types rather than message strings.
8. Standard SHALL provide a shared Mist error contract and an ordinary `Error`
   for general failures. Domain errors SHALL preserve structured information
   without requiring CLR exception inheritance. Concrete declarations and
   conformance use Mist's existing type and library mechanisms.
9. `Result<T>` SHALL be shorthand for `Result<T, Error>`, preserving the same
   representation, proof, propagation, and discard rules.
10. The initial Standard surface for this feature SHALL omit a generic
    `Error<Payload>` wrapper. Named error types MAY carry their own data; this
    omission does not prohibit user-defined generic error types.
11. This feature SHALL introduce only error types needed by its delivered
    operations. These types MAY live in Standard; example domain names SHALL
    NOT imply a requirement to implement a speculative error catalog.
12. Standard SHALL provide System.Exception conformance/adaptation to the shared
    Mist Error contract. Propagation into Result<T> SHALL NOT require each caller
    or each CLR API to supply a manual error mapper. The original exception and
    its structured information SHALL remain available through that adaptation.
13. An expected Result<T, E> destination SHALL permit implicit construction from
    a payload statically assignable to exactly one of T and E, selecting that
    alternative. If neither alternative accepts the payload, construction SHALL
    be refused. If both accept it, the caller SHALL explicitly select a case.
14. AsSuccess() and AsFailure() SHALL explicitly select the construction case
    for a payload, with the expected Result destination supplying the other
    payload type. These projections SHALL NOT extract an existing Result's
    payload or assert that it succeeded. They SHALL preserve ordinary ownership
    and cleanup obligations.
15. Explicit factories on a closed Result<T, E> SHALL remain available for
    construction without an expected Result destination, overlapping payload
    types, and deliberate nesting. Everyday returns SHALL NOT require repeating
    the closed Result type.
16. Ordinary assignment of an existing Result SHALL preserve its selected
    alternative and layers. Construction SHALL NOT introduce recursive
    flattening, unchecked extraction, or arbitrary Result variance. An inferred
    var binding alone SHALL NOT convert a plain payload into a Result.

## Requirement 2 Access after proof

1. A program SHALL establish the applicable Result alternative before accessing
   its payload. Pattern binding and sound flow narrowing MAY provide that proof.
2. A narrowed success value SHALL expose its `T` payload through ordinary member
   access or pattern binding. A narrowed error value SHALL analogously expose
   its `E` payload.
3. A check of one value SHALL NOT authorize extraction from a different value or
   from a value changed since the check. Mutation and aliasing SHALL invalidate
   proofs wherever they can change the selected alternative or its identity.
4. The implementation SHALL reuse Mist's union and refinement rules where their
   contracts apply. It SHALL NOT introduce a privileged operation that asserts
   success without proof.
5. Result SHALL provide Inspect returning a correlated tuple union equivalent
   to `(True, T) | (False, E)`, where True and False refine bool to their literal
   values. A true-tag pattern SHALL bind a T payload, and a false-tag pattern
   SHALL bind an E payload. This SHALL preserve optional and void successes,
   overlapping payload types, and nested Result layers.
6. Inspect SHALL evaluate its receiver once and preserve its tag/payload
   correlation under the existing ownership and mutation rules. Inspection
   SHALL NOT duplicate an owned payload or by itself resolve an otherwise
   obviously unhandled local error path.

## Requirement 3 Explicit propagation

1. Mist SHALL provide convenient syntax that yields a Result's success payload
   or returns its error from the enclosing callable.
2. The operand SHALL be evaluated once. On error, expressions sequenced after
   the propagation point SHALL NOT execute in that callable.
3. Propagation SHALL preserve the enclosing method's declared return contract.
   Compatible error types, including constituents of an admitted error union,
   SHALL propagate under ordinary assignability. An incompatible error SHALL
   require explicit mapping;
   it SHALL NOT be silently discarded, converted to success, or turned into a
   panic.
4. Propagation SHALL perform the same scope cleanup and ownership transfers as
   the corresponding explicit early return. It SHALL NOT skip context restoration,
   owed drops, or structured task completion.
5. Propagation inside a lambda or local function SHALL return from that callable,
   not an enclosing caller.
6. Propagation SHALL use conventional postfix precedence. It applies to its
   immediate operand; applying it to a compound arithmetic expression requires
   grouping that expression.
7. Propagation applied to an already-proven successful plain value SHALL act as
   identity. It SHALL preserve that value's type, including optionality; it
   SHALL NOT remove `void` or assert that an optional value is present. A known
   error alternative is not a successful plain value for this rule.
8. Error propagation SHALL require a compatible return destination. Constructors
   and other contexts without such a destination SHALL refuse it; fallible
   construction uses an ordinary Result-returning factory. Identity on a proven
   successful plain value does not require an error return destination.
9. For representations admitting nested Results, one propagation operation
   SHALL process one Result layer, without implicit recursive flattening.

Postfix `!` is the selected spelling, distinguishing error propagation from
the existing optional operators.

## Requirement 4 Refinements and arithmetic

1. Standard SHALL express reusable safety preconditions through refinement types
   and operation contracts, allowing the compiler to prove that operations cannot
   experience their defined ordinary failures.
2. For division, WHEN all required safety conditions are proven at the operation,
   its expression type SHALL be the numeric success type. WHEN the operation is
   proven invalid, the compiler SHALL refuse it. OTHERWISE its expression type
   SHALL be `Result<NumericType, DivisionError>`, with the concrete error type
   and numeric-family contracts defined by Standard.
3. The Result-producing route SHALL check the necessary conditions and return the
   corresponding error instead of executing an operation that can trap. Its
   successful value SHALL agree with the proven-safe route for the same operands.
4. Both routes SHALL evaluate operands once and preserve Mist's evaluation order.
   Proof SHALL apply to those evaluated values, not a later read of a mutable
   property, aliased location, or repeated call.
5. An expected result type SHALL NOT supply safety evidence. Writing
   `int z = x / y` SHALL NOT force the numeric route when the operands do not
   establish its preconditions; an unproven Result-to-int assignment is refused.
6. Safety evidence MAY come from constants, declared refinements, or sound flow
   facts. WHEN neither safety nor invalidity is proven, the compiler SHALL select
   the fallible route, not assume safety. Expression types SHALL be determined
   at compile time, not vary with runtime operand values.
7. Proving only a nonzero denominator SHALL NOT establish safety for a numeric
   family with additional division hazards. An unrepresentable signed integer
   quotient, including minimum-value divided by negative one, SHALL be an error.
8. Existing defined arithmetic behavior, including integer wrapping where already
   specified, SHALL NOT change merely because an operation is pure or implemented
   by a CLR intrinsic. Each numeric-family failure contract must be stated.
9. Standard's Result API SHALL provide arithmetic operator overloads that carry
   an existing error forward as a value or apply the underlying operation to
   successful payloads. Supported operand combinations and error types SHALL be
   ordinary operator contracts, not implicit language-wide lifting.
10. Result arithmetic SHALL NOT implicitly return from the enclosing callable.
    Explicit propagation syntax performs that return after the composed
    expression produces its Result. Ordinary operator operand evaluation rules
    continue to apply.
11. WHEN a Mist operation is statically proven to violate its panic-prevention
    conditions, the compiler SHALL report an error instead of emitting the
    operation. Integer `1 / 0` is one such refusal. Explicitly constructing or
    returning an ordinary error value is not itself a panic or a refusal.
12. WHEN both operands of a Result arithmetic overload are errors, the overload
    SHALL return the left operand's error. This does not skip evaluation of the
    right operand or select a policy for simultaneous body and cleanup failures.
13. Numeric operations SHALL follow the contracts below. The three-way rule
    applies to the defined error cases: proven safe returns a numeric value,
    proven invalid is refused, and uncertainty returns a Result.

| Operation | Contract |
| --- | --- |
| Fixed-width integer addition, subtraction, multiplication, signed negation | Preserve wrapping semantics |
| Integer division | Truncate toward zero; zero divisor and unrepresentable quotient are errors |
| Integer remainder | Zero divisor is an error; signed minimum remainder negative one is zero |
| Decimal arithmetic | Preserve rounding; overflow and division/remainder by zero are errors |
| Floating-point arithmetic | Preserve IEEE NaN and infinity as values |

The integer division contract explicitly amends narrow integers' wrapping
division rule. Decimal remainder does not require a representable quotient.

Examples below describe the new typing rule; they are not runnable acceptance
samples yet. `x` and `y` have no refinements beyond their declared type.

```mist
int x = SomeCall();
int y = AnotherCall();
var uncertain = x / y;

if (y > 0)
{
    var proven = x / y;
}
```

`uncertain` has type `Result<int, DivisionError>`. `proven` has type `int`:
positive `y` excludes both zero and the negative-one overflow case. The concrete
error declaration belongs to Standard's implementation of this contract.

## Requirement 5 Foreign calls and optional wrappers

1. Raw CLR calls SHALL remain available through the ambient provider without
   blanket Result wrapping or a requirement to prove every foreign call safe.
2. Unknown foreign failure behavior SHALL NOT be equated with proven safety, but
   SHALL NOT by itself change a raw call's return type or prevent its use.
3. Standard SHALL provide the optional System.Exception.Catch facility described
   below, backed by compiler emission, to convert foreign failures into ordinary
   Result error values at an explicitly chosen boundary.
4. Developers consuming raw CLR APIs remain responsible for their foreign
   behavior. A foreign exception analysis SHALL NOT be a prerequisite for using
   those APIs. This does not add C# exception syntax to Mist.
5. A raw, unhandled foreign exception SHALL remain an unhandled runtime failure.
   Generated applications SHALL NOT acquire a global catch that turns such a
   failure into a Result or resumes the program.
6. Returning a Result from a Mist method SHALL NOT by itself certify foreign
   operations inside that method as panic free. A wrapper's actual conversion
   coverage determines which foreign failures become ordinary error values.
7. Exception handling for selected wrappers and `finally` for existing cleanup
   semantics MAY exist in generated C#. Neither exposes general exception
   control flow as a Mist language feature.
8. Using the conversion facility SHALL NOT require a separately authored
   adapter method for each CLR member. Users and Standard SHALL NOT need a
   catalog of one-off wrappers covering the CLR API surface.
9. Mist SHALL provide `System.Exception.Catch` as a static interop facility
   taking an operation and its arguments separately. Its generic shape SHALL
   use `Catch<params TArgs, TResult>(Func<TArgs..., TResult> operation,
   params TArgs args)` and return `Result<TResult, System.Exception>`, retaining
   the original covered CLR exception as the error payload.
10. The operation reference, receiver, and arguments SHALL retain ordinary
    evaluation semantics before entering Catch. Catch SHALL invoke
    `operation(args...)` once inside its exception-conversion boundary. This
    form SHALL NOT require implicit deferral of invocation expressions.
11. The facility SHALL capture covered CLR exceptions as the error alternative
    without requiring a caller-supplied mapper. Void-returning operations SHALL
    use `Result<void, System.Exception>` under the same variadic contract.
12. Catch SHALL accept any signature-compatible callable: a CLR member, Mist
    member, lambda, or stored delegate. The emitter SHALL provide the protected
    invocation; the API SHALL NOT require the target to be a direct CLR member.
13. Adapting a task-returning operation for completion SHALL produce
    `Task<Result<T, System.Exception>>` and protect both invocation and task
    completion. Mist auto-await SHALL operate on that adapted task; the foreign
    await SHALL NOT move outside the conversion boundary.
14. Catch SHALL capture catchable System.Exception failures escaping the
    protected invocation or adapted task completion, under the runtime
    assumptions. It SHALL NOT promise recovery from process termination or
    failures the runtime cannot deliver to that exception boundary.
15. A caught OperationCanceledException, including a derived cancellation
    exception, SHALL become the ordinary error payload with its identity
    preserved. The task adapter SHALL complete normally with that failure
    Result, rather than become canceled or faulted because of that cancellation.
    Postfix ! SHALL propagate it under the ordinary Result rules. Raw CLR tasks
    outside this adapter SHALL retain their existing cancellation behavior.

The selected synchronous call shape is
`System.Exception.Catch(File.ReadAllText, path)`. Async overload resolution and
signature adaptation must implement the compatible-callable and protected
completion contracts above; they are implementation details of the selected API.

## Requirement 6 Composition and cleanup

1. Operations entirely within Mist's panic-free contracts SHALL compose without
   introducing hidden language-level panic paths. FFI exposure is transitive;
   placing a foreign call behind a Mist method does not remove it.
2. Existing context and drop obligations SHALL hold on success, explicit error
   returns, and propagation. No new source-level `finally` is needed to express
   their established scope semantics.
3. An unwrapped foreign call made during cleanup remains FFI exposure. The
   compiler SHALL NOT silently absorb its exception to preserve a pending Result.
4. Foreign task completion, iteration, callbacks, and disposal SHALL remain
   outside the Mist-only guarantee. Successfully creating a foreign object or
   task SHALL NOT establish that its later operations cannot fail.
5. Automatic cleanup SHALL NOT silently discard a Result error. The design must
   refuse implicit Result-valued cleanup unless an explicit handling route
   exists. Fallible cleanup MAY be called explicitly and its outcome handled
   using ordinary Result rules. This SHALL NOT imply automatic aggregation of
   body and cleanup failures.
6. This feature SHALL NOT introduce a panicfree keyword. No compiler assumption
   of infallible cleanup follows from a void return type or from this keyword's
   omission. FFI exposure includes calls performed by implicit cleanup.
7. This feature SHALL NOT require a transitive proof that Drop is infallible.
   Existing exceptional-exit cleanup obligations SHALL still be preserved;
   omitting the proof does not authorize skipping promised cleanup or silently
   discarding cleanup errors represented by Results.
8. When task cleanup would implicitly await and discard a Result outcome, the
   compiler SHALL require explicit observation and disposition of that Result
   instead. A bare Task<Result<T, E>> at scope exit SHALL NOT silently erase
   the Result-handling obligation.

## Requirement 7 Explicit Result discard

1. An expression statement that silently discards a possible Result error SHALL
   be a compile-time error. A bare `Save(document);`, where Save returns
   `Result<void, SaveError>`, is such a statement.
2. The explicit discard operator SHALL permit `_ = Save(document);`. This
   deliberately ignores the outcome, whether success or error, and requires no
   enclosing error return destination.
3. Explicit discard SHALL evaluate its operand once and preserve its effects
   and owed payload cleanup. It SHALL NOT turn a foreign exception into a
   Result, undo partial effects, or establish that the operation succeeded.
4. The checker SHALL exhaustively examine its defined local control-flow scope
   for obvious loss of an unresolved Result, including an unused local and
   straightforward overwrites or unhandled branches. It SHALL remain a
   best-effort diagnostic rather than a whole-program proof of useful recovery.
5. Storing an error or Result in a collection SHALL be a sufficient use for this
   analysis. The compiler SHALL NOT report an error merely because that
   collection is never subsequently used. It SHALL NOT recursively impose
   Result-handling obligations on arbitrary containers and their consumers.

The implementation must define and test local alias and branch-transfer rules
within this selected analysis boundary. The checker does not prove that an
observed error has been usefully recovered from.

## Relationship to existing contracts

On delivery, this feature amends the unconditional division return signatures in
[standard-library Requirement 3](../standard-library/requirements.md), and the
arithmetic result rule in [refinement-types Requirement 8](../refinement-types/requirements.md).
It also resolves the ordinary arithmetic-failure example deferred by
[purity](../purity/requirements.md) and [defaultability](../defaultability/requirements.md),
without making purity imply termination or safety of arbitrary FFI.

The auto-drop return-value discard rules in [drop-semantics Requirements 3 and
5](../drop-semantics/requirements.md) must be reconciled for Result-valued drop
and task outcomes. Their existing blanket discard cannot silently consume a
cleanup error under this feature.

Result itself is not defaultable. Explicit Result-valued initializers in other
types remain subject to the existing defaultability and purity contracts. This
specification does not change the existing array-bounds refusal policy.

MIST-1048 remains the owner decision record. MIST-1049 holds the production
inventory for self-hosting; an implementation must reconcile its observable
boundary and cleanup requirements with this design. No implementation dispatch
or closure follows from this specification alone.
