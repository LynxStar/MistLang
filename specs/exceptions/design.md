# Result and FFI design

The [requirements](requirements.md) record the selected direction: panic-free
Mist operations, ordinary Results, proof before payload access, and division whose
type depends on proof. Raw FFI lowers the guarantee and remains the developer's
responsibility. System.Exception.Catch provides an optional boundary for CLR
failures, including cancellation. The feature's policy decisions are settled;
compiler and library implementation must satisfy the contracts below without
adding a general exception language or effect system.

## Result representation

The selected public surface supports user-defined error types through
`Result<T, E>`, an ordinary Standard `Error` for general failures, and
`Result<T>` as shorthand for `Result<T, Error>`. Different operations can
describe different failure domains:

```text
Result<Person, ParseError> ParsePerson(string text);
Result<void, SaveError> Save(Document document);
```

Named error types can carry their own data and expose distinguishable cases.
For example, a ParseError can identify a field and source location; a caller
need not interpret a message string to determine the failure kind.

The initial Standard API omits `Error<Payload>`. A generic wrapper around a
string adds little when the string is just a message, while a named error type
can carry richer data directly. This does not prohibit user-defined generic
error types. A bare string is not itself an error merely because it contains
a failure message.

The owner selects a shared Mist error contract that preserves structured
information without CLR Exception inheritance. Only error types required by
the feature's actual operations are introduced, and these can live in Standard.
ParseError and SaveError above illustrate domain types; they are not a request
for a speculative catalog. Concrete Error declarations and conformance use
Mist's existing library mechanisms. The shorthand alone introduces neither a
new implicit error conversion nor a requirement for default generic arguments.

The owner selects an internal success/error discriminant without a required
public Success<T> wrapper. Result<Error, Error> distinguishes a successful error
value from a failed operation, and nested Results retain separate layers.
Implicit construction selects an unambiguous case, explicit projections select
a case when needed, and closed Result factories remain available. Correlated
tuple inspection is selected. A separate public Failure<E> wrapper is not
required merely to represent an already typed error.

The current `Optional<T>` is `T | void` in `Source/Standard/Optional.mist`.
Result shares its proof discipline: narrow the alternative, then access the
value. The selected tagged representation needs a proven success view or
pattern. Testing only the payload type cannot distinguish overlapping cases.
There is no unsafe payload accessor.

Result is not defaultable. Equality and ownership follow the selected ordinary
type representation; an all-zero backing struct must not become an observable
invalid Result through Mist construction. Extracting or discarding a Result
preserves the ownership and cleanup obligations of its active payload.

### Selected construction API

The owner selects safe implicit construction, explicit AsSuccess()/AsFailure()
projections, and closed Result<T, E> factories as a fallback. Normal returns
do not require repeating the closed Result type. These are selected contracts;
current compiler support remains implementation work.

Explicit factory examples, with method names illustrative:

```text
var success = Result<Document, ValidationError>.Success(document);
var failure = Result<Document, ValidationError>.Failure(error);
var completed = Result<void, ValidationError>.Success(void);
```

These are methods, not public Success<T>/Failure<E> wrapper types. The explicit
factories work for overlapping payload types, standalone construction without
an expected Result type, and deliberately nested Results. Constructing a domain
error and selecting a Result alternative are separate operations. A domain
factory such as ValidationError.MissingTitle() produces the error payload;
callers should not then have to repeat the enclosing Result type to return it.

The declared Result destination permits implicit construction when the source
is statically assignable to exactly one payload alternative:

```text
Result<Document, ValidationError> Validate(Document document)
{
    if (!document.HasTitle)
    {
        return ValidationError.MissingTitle();
    }

    return document;
}
```

If neither alternative accepts the source, construction is refused. If both
alternatives accept the source, including a broad T that can contain
the error type, refuse to guess. In particular, returning an Error directly
into Result<Error, Error> is ambiguous. Explicit construction or a projection
can identify the intended branch. Existing Results retain their layers; no
recursive flattening or arbitrary Result covariance is introduced. A plain
var binding does not create a Result merely because the value could be an
error; a Result destination or explicit construction supplies that intent.

Explicit projections make ambiguous returns concise:

```text
return error.AsSuccess();
return error.AsFailure();
```

These methods select the case for an input payload; they never assert
that an existing Result succeeded or extract an unchecked payload. One possible
library design returns a small typed projection carrying the payload and case,
then converts that projection into the expected Result type. Callers need not
name projection implementation types. The projection is not a complete Result
until its target supplies the other payload type. Projections preserve ordinary
ownership and cleanup obligations. Their internal representation is an
implementation choice, not an unresolved construction policy.

Current generic method inference learns type arguments from arguments and
callable signatures, not from an enclosing return type. A naive
AsSuccess<T, E>(T value) returning Result<T, E> cannot infer E from the receiver
alone. Typed projection values avoid assuming a new target-return inference
feature. Implementing the selected API requires verifying conversion behavior
against Mist's operator lookup and generic rules and extending those mechanisms
where needed. No current-compiler execution probe has established support for
this API; that verification does not reopen the selected construction design.

### Selected inspection and defaultability

The owner selects an ordinary Inspect method returning a correlated
tuple union, conceptually (True, T) | (False, E). True and False here are bool
refinements for the literal values, expressible through existing refinement
syntax. The correlation is essential: this is not (bool, T | E).

```text
var description = result.Inspect() switch
{
    (true, var value) => Describe(value),
    (false, var error) => DescribeError(error)
};
```

The true branch binds T and the false branch binds E. The tag still distinguishes
the cases when T equals E, T is optional, or T is void. This uses tuple/constant
pattern syntax rather than adding an exception-specific pattern form. It does
require the binder's union/refinement narrowing to preserve tag/payload
correlation; that composition has not been verified by a current-compiler probe.
Inspect is a snapshot of one evaluated result and obeys ordinary move/borrow
rules, without duplicating an owned active payload. It is an inspection step,
not an automatic discharge of a plainly unresolved local error outcome.

A Match method accepting success and error callbacks could be ordinary library
convenience over the same cases. It is not required for this feature.
There is no unconditional Value/Error accessor and no unsafe unwrap.

The owner selects no default value for Result. Construction selects a valid
alternative; default(Result<T, E>) and an implicit parameterless construction
are refused. Local initialization and array construction follow the existing
defaultability, definite-assignment, and stampability rules. No invalid third
state is exposed.

### Shared Error contract and Standard adaptation

The practical compatibility issue is the declared error destination. Catch
returns Result<T, System.Exception>, whereas Result<T> abbreviates
Result<T, Error>. Propagating with ! requires System.Exception to conform or
convert to Error. Merely naming both types as errors does not create that rule.

The owner selects Standard solving the System.Exception compatibility once,
without caller-supplied or per-CLR-method mappings. The proposed minimal shape
is an Error abstract contract with a Message getter, plus one Standard
retroactive conformance/adaptation for System.Exception through the existing
abstract/with model. Domain types retain their additional structured fields.
The concrete members and declaration are library implementation details; the
requirement to provide this Standard compatibility is selected.

With that conformance, ! performs ordinary error assignability into Result<T>.
It does not need a blanket conversion of one closed Result type to another.
The exact declaration and any minimal concrete general-error value are library
implementation details. There is no new mandatory CLR exception
inheritance for Mist error types.

## Propagation spelling

Conventional postfix precedence and postfix `!` are selected, expressing
success extraction or early return of an error, while `?` remains associated
with optional values.

| Form | Meaning |
| --- | --- |
| `T?` | Optional type, an alias for `T` or `void` |
| `value?.Member` | Access the member if the optional receiver is present |
| `value ?? fallback` | Substitute for an absent optional value |
| `value ??= fallback` | Assign a fallback when the optional value is absent |
| `expression!` | Produce success or return the error from this callable |

Examples:

```text
var scaled = (x / y * scale)!;
var person = ReadPerson()!;
var name = ReadPerson()!?.Name;
```

In the arithmetic example, the parentheses make the whole calculation the
operand. `x / y * scale!` would apply propagation only to `scale`.

The final example assumes `ReadPerson` returns `Result<Person?, Error>` and
`Person.Name` returns `string`. Postfix `!` produces `Person?` on success;
`?.Name` then produces `string?`. Its cases are:

| Result from ReadPerson | Outcome |
| --- | --- |
| Error | Return the error from the enclosing callable before member access |
| Success containing `void` | The expression produces `void`; the callable continues |
| Success containing a Person | Read `Name` and use that string as the expression's value |

This follows the existing Optional contract. `?.` propagates absence through
the expression, not out of the enclosing callable. Making absence itself exit
would require a separate decision; it is not implied by combining these two
operators.

If `ReadPerson` instead returns `Result<Person, Error>`, success already
establishes a present Person and the expression is `ReadPerson()!.Name`.
Existing Optional Requirement 3.4 refuses `?.` on a nonoptional receiver. A
property whose own type is `string?` remains optional when read with ordinary
`.`; `?.` tests the receiver rather than the returned property value.

The lexer already distinguishes `!`, `!=`, `?.`, `??`, and `??=`. The
`!?.` chain can tokenize as `!` followed by `?.`; it needs no combined token.
Prefix `!flag` retains logical negation, while postfix `result!` would be the
new propagation expression. The parser and semantic model must distinguish
these operations by position. Postfix propagation checks the alternative and
returns on error; it must never assert success and panic on a failed assertion.

Propagation on a proven successful plain value is selected as identity. This
keeps `(x / y)!` valid when a refinement makes the division return an `int`.
Identity preserves optionality: applying it to `Person?` does not prove that a
person exists. An error alternative does not become identity merely because
narrowing has established which error it is.

Compatible errors propagate through ordinary assignability, including error
unions; incompatible errors require explicit mapping. Where nested Results are
admitted, each propagation operation processes one layer. Error propagation
requires a compatible enclosing return contract. Fallible construction uses a
Result-returning factory; a constructor has no error return destination. Async
Result methods must preserve these rules through existing await semantics, not
interpret a Result error as a faulted task.

## Discarding Results

The owner selects a compile-time error for a silently discarded Result and
permits deliberate discard through `_ =`. The following uses a proposed `Save`
returning `Result<void, SaveError>`:

```text
Save(document);                 // Refused: outcome silently ignored.
var pending = Save(document);   // Refused when the local is plainly left unused.
Save(document)!;               // Propagate failure; continue on success.
return Save(document);         // Transfer the outcome to this method's caller.
_ = Save(document);            // Accepted explicit choice to ignore the outcome.
```

These lines illustrate separate alternatives. Propagation continues on success
and returns on failure; returning the Result exits on either outcome. At the
end of a compatible Result<void, E> method, propagating then completing with
void success can have the same outcome as returning the Result directly.
Elsewhere, subsequent statements distinguish them. Propagation makes the save
a prerequisite for continuing successfully. Explicit discard permits continuing
after either ordinary outcome and is the best-effort form in that sense.

The guarantee is an explicit disposition of the outcome, not proof of useful
recovery. The compiler cannot establish that a report, retry, or fallback is
appropriate for the application. A Result-returning Save still reports failure;
one caller's decision to discard it does not alter that API contract or require
renaming it TrySave.

Explicit handling uses the selected correlated tuple inspection:

```text
var outcome = Save(document);
if (outcome.Inspect() is (false, var error))
{
    Report(error);
}
```

Here `Report` handles the error and returns void; the successful void case needs
no payload use.

The owner selects best-effort checking that exhaustively covers the defined
local control-flow scope, hunting obvious pointless or forgotten Results.
Checking success alone does not discharge a plainly ignored error path. The
programmer can explicitly discard there:

```text
if (result.Inspect() is (true, _))
{
    ShowSaved();
}
else
{
    _ = result;
}
```

An empty success branch is acceptable when no separate payload-use obligation
exists. The unresolved error path, rather than lack of work on success, is the
reason for a local diagnostic. The implementation must define and test
branch-transfer and local alias rules within this boundary; merely printing an
unrelated field does not establish that an unresolved error path is addressed.

Assignment to an otherwise unused local and straightforward overwrites are in
scope. Passing, returning, or storing the outcome counts as a disposition at
this boundary. In particular, saving an error or Result into a list is enough;
the compiler does not then require the list to be read. There is no recursive
obligation analysis through containers, callbacks, or the whole program. This
is deliberately a best-effort diagnostic, not linear consumption of all errors
or proof that handlers improve the application's outcome.

Explicit discard still performs owed cleanup of the active payload. A discard
inserted by the compiler is not equivalent to a deliberate user-written discard.

## Proof-sensitive arithmetic

Division has a safe numeric route and a general Result route. The route is
selected from operand facts at compile time. The safe route cannot be selected
merely because a caller wants an `int`.

Standard should own the safety contracts; the compiler should apply shared
refinement reasoning. A reusable nonzero refinement is useful but insufficient
for every signed division: overflow at minimum-value divided by negative one
is a relation between the two operands. A positive denominator supplies a
simple sufficient proof for Int32 division.

| Operand evidence for Int32 division | Expression type |
| --- | --- |
| Both operands are constants with a valid quotient | `int` |
| Denominator is proven positive | `int` |
| Denominator is nonzero, overflow case still possible | `Result<int, DivisionError>` |
| Proven zero denominator or another invalid operand pair | Compile-time error |
| Neither safety nor invalidity proven | `Result<int, DivisionError>` |

The selected division policy distinguishes zero-divisor and overflow cases.
Existing wrapping addition is not made fallible by this feature. The numeric
contracts below specify the other families and operations.

The selected rule has three outcomes: proven safe yields a value; proven
invalid is a compile-time error; uncertainty yields a Result. Integer `1 / 0`
is refused. The refusal concerns a known invalid operation, not an ordinary
method deliberately returning an Error. A lack of proof of safety is not proof
of invalidity.

### Selected numeric policies

The owner accepts the policies below. They apply the three-way rule to each
operation's stated domain.

| Operation | Selected behavior |
| --- | --- |
| Fixed-width integer addition, subtraction, multiplication and signed negation | Preserve existing wrapping semantics; offer explicit overflow-detecting methods separately |
| Integer division | Truncate toward zero; zero divisor and an unrepresentable signed quotient are errors |
| Integer remainder | Zero divisor is an error; signed minimum remainder negative one is zero, with no quotient-overflow failure |
| Decimal addition, subtraction and multiplication | Preserve rounding; out-of-range results are errors |
| Decimal division | Preserve rounding; zero divisor and out-of-range results are errors |
| Decimal remainder | Zero divisor is an error; compute the representable remainder without requiring a representable quotient |
| Float and double arithmetic | Preserve IEEE nonfinite values and existing remainder semantics; NaN and infinity are values, not errors |

Applying the signed-division overflow policy uniformly to narrow integers
explicitly amends their existing wrapping division behavior. Wrapping remains
the policy for addition/subtraction/multiplication and signed negation.
The backend must implement signed remainder's minimum/negative-one case without
executing a target operation that could trap.

Under the floating-point policy, `1.0 / 0.0` is defined infinity, so it is not
a statically known panic. Code needing finite results can use a finite-value
refinement and operations returning errors when a finite result cannot be
produced. Rounding alone is not an error for decimal or floating-point values.

The CLR has a signed integer division overflow case even in unchecked code;
decimal arithmetic can throw for overflow or zero division, while float/double
arithmetic represents nonfinite outcomes as values. Mist's policies above are
explicit language choices, with backend work to meet them.
[Microsoft arithmetic reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/arithmetic-operators)

The source expression has one static type at its location. A Result produced
earlier does not turn into a plain value because its operands are proven safe
later. Declared function return types remain contracts; a caller does not
automatically specialize them by inspecting the implementation.

Result's arithmetic overloads allow intermediate errors to remain ordinary
values through a composed expression. For example, if `x / y` produces
`Result<int, E>`, an appropriate multiplication overload can accept that Result
and `scale`, return the existing failure, or multiply the success payload. The
expression `x / y * scale` then produces a Result, and a final propagation
operation handles the return to the caller.

This behavior belongs to Standard's types and ordinary overload resolution.
The compiler does not automatically lift every operator, flatten nested
Results, or insert early returns. The overloads must compose any failure from
the underlying operation into their declared result type. Exact support for
Result/value, value/Result, and Result/Result combinations and distinct error
domains belongs to the API design. When both operands are errors, the selected
rule is to return the left error. Compatible error unions and explicit mapping
provide the agreed composition mechanisms.

Ordinary operator arguments are evaluated before the operator body runs. Thus
`x / y * GetScale()` still evaluates `GetScale()` when division has produced an
error Result. The multiplication overload can skip multiplication of payloads;
it cannot retroactively skip evaluation of its right operand. Explicit earlier
propagation can exit before that call. Lazy composition would require a
separate API accepting a deferred computation, not an implicit change to
ordinary arithmetic evaluation.

A stable Result-returning arithmetic method could also be useful when an API
intentionally keeps the same type across refinements.

## Raw foreign calls and optional wrappers

Consuming a raw CLR API requires the developer to understand its failure
behavior. Mist does not automatically make that API safe or change its return
type. Optional wrappers for selected foreign calls are required for self-hosting;
this does not restore blanket wrapping or a foreign exception analysis requirement.

The owner rejects a design presented as a separate named wrapper and explicit
exception mapper for each CLR method. The facility must scale across foreign
APIs without requiring users or Standard to author one-off adapters per member.
The owner selects System.Exception.Catch with a method reference and separate
arguments, implemented through the variadic type/value pack contract. A bare
File.ReadAllText(path) remains an ordinary invocation; this API needs no new
implicit invocation-to-callable conversion. The boundary captures catchable
System.Exception failures, including cancellation, as specified below.

The selected shape uses one generic facility whose emitter wraps the invocation
in exception handling. It passes a callable and its arguments separately,
preserving ordinary invocation syntax:

```text
var text = System.Exception.Catch(System.IO.File.ReadAllText, path)!;
System.Exception.Catch(System.IO.File.WriteAllText, path, text)!;
```

The owner selects the original System.Exception as the error payload, giving
Result<string, System.Exception> and Result<void, System.Exception> above.
This replaces mandatory generic Error mapping at the interception boundary.
Neither call requires a named adapter, a domain error, or a caller-supplied
mapper. Domain-specific conversion remains an optional ordinary Result operation.

The internal Result discriminant distinguishes success from error independently
of the payload's CLR ancestry. Standard supplies the accepted conformance or
adaptation to the shared Error contract; its exact declaration remains to be
written. Returning a CLR exception as data does not require Mist errors to
inherit from it or expose source-language throwing. Retaining the original
object preserves its existing diagnostic context; later foreign member access
still has its ordinary FFI contract.

The variadic specification provides one signature across argument counts,
with static types preserved. The selected shape with intrinsic interception is:

```text
intrinsic public static Result<TResult, System.Exception> Catch<params TArgs, TResult>(
    Func<TArgs..., TResult> operation,
    params TArgs args);
```

The value pack is declared with params TArgs args, not TArgs... args. Inside
the body, args is a tuple of type (TArgs...), and operation(args...) spreads it
back into the callable's argument list. Func<TArgs..., TResult> spreads the
type pack into the function signature. The existing Variadics sample's Apply
method uses this same parameter and forwarding shape. This is specification
work, not an implemented Catch API or an executed Catch inference probe.

The implementation invokes operation(args...) inside the conversion boundary.
Each receiver and argument is evaluated once before entering Catch, under the
ordinary argument evaluation rules. For Catch(File.ReadAllText, GetPath()),
GetPath executes before interception begins. The selected method-reference
form protects invocation after preparation, not work already performed while
evaluating its arguments.

An explicit zero-argument lambda can defer the whole expression instead:

```text
System.Exception.Catch(() => System.IO.File.ReadAllText(GetPath()))
```

That alternative makes both GetPath and ReadAllText execute inside the
protected invocation. It introduces ordinary closure capture and a callable
return scope, and uses an empty TArgs pack. The parameter shape can represent
it. The owner selects acceptance of any signature-compatible callable,
including Mist methods, lambdas, and stored delegates. The method-reference
form does not require callers to write a lambda.

Mist's extension-method specification permits static extension members
callable on a type name, providing a design route for a Mist-supplied Catch
on the projected System.Exception type. Catch is not an existing BCL member.

Catch invokes the supplied operation once inside the protected region and
captures covered CLR exceptions into the error alternative. The two callable
forms above differ in which work has already occurred before that invocation.

The selected coverage is catchable System.Exception failures escaping the
protected invocation or adapted task completion, under the runtime assumptions.
It does not promise recovery from process termination or failures the runtime
cannot deliver to that exception boundary. Success or converted failure becomes
a normal Result, with the usual handling/discard obligations.
The conversion needs trusted runtime or compiler support because ordinary Mist
has no exception-catching operation. No per-CLR-method compiler implementation
is implied.

Catch does not inspect callable provenance or require a direct projected CLR
member. Any compatible delegate is accepted; emitted exception handling
implements the boundary. A normal Result returned by the delegate stays a
successful payload of the outer Result, even when that inner Result is an
error. No implicit flattening or conversion of ordinary Mist errors into CLR
exceptions occurs. Closure ownership restrictions, by-reference arguments,
and overload selection still need their ordinary treatment. Synchronous callbacks can
throw during a foreign invocation, so matching exception types does not prove
where the exception originally arose. Retaining a foreign exception object
for diagnostics does not certify its later member accesses as safe.

The owner selects task-completion coverage: its adapter returns
Task<Result<T, System.Exception>> and protects both invocation and awaiting the
task inside its generated boundary. Result<Task<T>, E> alone only describes
successful task creation, not eventual completion. Mist's auto-await must act
on the adapted task rather than move the foreign await outside the boundary.

The owner selects cancellation as an ordinary error inside an explicitly chosen
Catch boundary. A caught OperationCanceledException, including a derived
cancellation exception, retains its identity as the failure payload. The adapter
task completes normally with that failure Result rather than becoming canceled
or faulted because of the captured cancellation. Postfix ! propagates the
failure under the ordinary Result return rules. This does not change the
cancellation behavior of raw CLR tasks outside the adapter or suppress
cancellation beyond the selected boundary.

Async overload selection and signature adaptation must preserve these contracts
and ordinary ownership and by-reference argument rules. A deferred iterator's
later MoveNext and disposal are not protected by wrapping its creation; those
operations require their own invocation boundary. Wrappers cannot undo partial
foreign mutations. Their declared result types must not imply broader
protection than they provide.

The boundary follows the operation's contract, not the implementation language
of generated code. Standard's Mist-defined division owes its panic-free behavior
even though the backend implements it in C#. An arbitrary projected CLR member
carries foreign behavior. A Mist-authored method inherits the foreign exposure
of its dependencies. Here Mist-only code is distinct from the `pure` modifier.

## Cleanup, infallibility and termination

Results replace internal failure signalling; scoped cleanup restores compiler
state and disposes resources. Propagation must honor the same scope obligations
as explicit return. The owner requires an account of cleanup failure, especially
when another error is already being propagated.

The existing [drop spec](../drop-semantics/requirements.md) permits any Drop
return type and discards non-void results during automatic drop and with-block
cleanup (Requirements 3.5 and 5.6). Task auto-drop also discards its result. These
rules must not silently discard Result errors under the new feature. Any change
must preserve value-returning Drop and structured task semantics, not assume
that all existing drops return void.

Three promises are distinct:

| Promise | Meaning |
| --- | --- |
| Panic free | No hidden language-level trap under the operation's contract |
| Infallible | The operation has no ordinary error outcome |
| Total | The operation produces a result for every admitted input and terminates |

A total function returning `Result<T, E>` can return an error: that is still a
valid result. Requiring cleanup to terminate successfully would therefore need
both termination and infallibility. Neither follows from purity. A termination
checker requires a restricted/provable subset and a policy for recursion,
loops, awaits, and foreign calls. It is not just another name for a void-returning
method. Totality systems such as Idris distinguish these termination obligations
explicitly. [Idris totality documentation](https://idris2.readthedocs.io/en/stable/tutorial/typesfuns.html#totality)

### Cleanup without an infallibility promise

The owner rejects introducing a panicfree keyword and asks whether leaving
cleanup unenforced is a missing verification tool or a soundness bug. No
checked infallible-Drop contract has been selected. Void return types and
purity do not establish absence of escaping foreign exceptions.

Under the selected guarantee, missing transitive no-escape analysis is a
verification gap rather than inherently a type-soundness defect: Mist-defined
operations represent their failures explicitly, while raw FFI may escape.
Implicit calls made by Drop participate in that FFI exposure. The compiler
must not assume cleanup is infallible, optimize away exceptional paths, or
claim successful cleanup solely from the absence of a Result return type.

The selected scope retains this ordinary FFI boundary without requiring a
transitive infallibility proof for Drop in this feature. A library author can explicitly use
Exception.Catch and ordinary Result handling where recovery is desired. No
automatic catch-and-discard or aggregation is implied. Inference or tooling
could provide additional diagnostics later without being required for this
contract. Result-valued implicit cleanup follows the explicit handling policy
below; omitting the proof does not permit silently discarding its error.

The cost is runtime exposure at implicit scope exit. A foreign cleanup exception
can prevent a pending success or error Result from being returned, and a
cleanup exception during another exception's propagation can replace the
earlier failure. Cleanup may be incomplete. The emitter still owes every
cleanup attempt required by the exceptional-exit contract; abandoning promised
outer drops or context restoration is an implementation defect, not permission
granted by omitting an infallibility proof. Sequencing multiple drops in one
finally versus protecting each remaining obligation must be accounted for.

A separate issue is returning Result from Drop. Automatically discarding that
error would violate the selected explicit-handling contract even if it causes
no panic. The owner selects the initial narrow policy: refuse implicit
Result-valued drops unless an explicit handling route exists, while allowing
the owner to call fallible Close/Commit/Drop operations explicitly. This is a
signature/outcome rule, not a transitive proof that a void Drop never throws.
Task<Result<...>> and other value-returning task drops need the corresponding
explicit observation rule. The owner accepts that such implicit completion
must not silently discard a Result; precise task-observation and generic-body
diagnostics must be specified consistently with the bounded local analysis.

Finally, capturing and discarding a close failure prevents its outward report
without proving resource release succeeded. The compiler cannot claim a
resource-specific postcondition from generic no-escape behavior. Total
termination is also outside the selected guarantee.

Automatic aggregation of body and cleanup errors is outside this feature.
Explicit fallible cleanup uses ordinary Result handling; the left-error rule
for arithmetic does not authorize implicitly discarding cleanup failures.
Uncovered CLR failures remain FFI exposure; no global recovery is introduced.

For self-hosting, MIST-1049 identifies the foreign IO, Tomlyn, process and metadata
boundaries, internal parse failures, and state/resource restoration requirements.
The selected CLR boundary and ordinary Result methods can express those
requirements. Their implementation must be verified against the recorded
behavior before claiming the self-hosted port preserves it.

## Compiler responsibilities and verification

Frontend binding owns expression types, overload selection, and conversions.
Because proof now affects division's type, binding must obtain sound operand
facts before dependent member lookup and inference. Stage 5
cannot change a finished model's expression types or rebind dependent calls.
Reuse the shared refinement semantics and flow infrastructure; do not add a
second incompatible proof engine inside the emitter.

Stage 5 validates proof-backed access, return compatibility, ownership, and
cleanup decisions over the finished model. The backend emits the selected
numeric/Result route without choosing new policy.

The verification design follows the repository testing philosophy: Tier 1
decisions for result alternatives, safety predicates, and conversions; Tier 2
hand-built bodies for narrowing, propagation, evaluation order, and cleanup;
emitter tests over directly built inputs; a bounded seam check where necessary.
User-visible success and Result-valued failure behavior belongs in the existing
AllFeaturesV2 baseline. Refusal rules need their owning-tier tests. No second
full-suite orchestrator is justified by this feature.

The owner policy decisions for this specification are settled. Concrete library
declarations, conversion lookup, bounded local analysis, async overload
resolution, and compiler verification remain implementation work constrained
by this contract. This specification does not dispatch implementation work or
change the status of the owner Decision MIST-1048.
