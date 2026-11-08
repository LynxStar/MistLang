# Flow-sensitive refinement inference

## Status and scope

Requirements settled with the owner on 2026-11-07 and 2026-11-08, developed
from [proposal.md](proposal.md). The design and tasks follow. This is a language
contract, not a claim that the current compiler implements it.

In scope: the facts the compiler infers about the current values of variables
along a control-flow path, how those facts survive joins, writes and
calls, and how a contract that requires static proof consumes them. The facts
extend what the refinement flow pass already tracks: intervals, literal values
and relationships to sequence lengths. Compound assignment and increment are in
scope too, because a loop that accumulates a value is where these facts are
used and the language has no spelling for it today.

Out of scope: the declaration of any particular operation, including Array's
growth operation and its ownership contract. Those belong to the specs that own
Array and ownership. This spec proves a contract once an operation declares one;
it does not say which operations exist.

Dependent type arguments remain compile-time constants, as
[dependent-types](../dependent-types/requirements.md) Requirement 5 states. A
dependent slot that takes a runtime value without a soundness hole is a stretch
goal of this spec. It is revisited only after every other requirement here is
settled and delivered, and nothing below depends on it.

A second stretch goal, under the same rule: a refinement on a return type that
mentions the parameters, so that a method states `result == x * 2` in its
signature. One spelling under consideration is a dependent type alias over an
expression, `type Doubled<int:x> = x * 2;` with `Doubled<x> Double(int x)`. The
exact syntax is open. Nothing below depends on it, and this spec reads no body
to make up for its absence.

## Requirement 1 How facts combine at a check

A fact is a statement about variables. A variable here is anything that holds a
value the compiler can name: a local, a parameter, or a member reached through
a receiver it can name, such as `backing.Length`. Requirement 2 states which
statements are recorded. This requirement states how the recorded statements
are used.

1. The compiler SHALL keep the facts in hand at a program point as one flat list
   of statements. A refinement declared on a variable's type SHALL enter the list
   as ordinary statements about that variable: a parameter `Capacity capacity`
   with `type Capacity = int >= 0 and <= 1_000_000;` contributes `capacity >= 0`
   and `capacity <= 1_000_000`. No fact is privileged as the variable's range.
2. When a use requires static proof of a contract, whether an argument against a
   parameter predicate, an assignment or return against a refinement, or an index
   against a bound, the compiler SHALL bind the contract's names to the use's
   variables, add the statement that the contract is broken to the facts
   in hand, and chain the statements through the variables they share,
   substituting and reducing, until no two statements can be combined.
3. The compiler SHALL judge the contract proven when the chain reaches a
   statement no value satisfies. The number of steps SHALL be bounded by the
   number of facts and variables involved, never by the width of a type. A proof
   never walks the values of a type.
4. When the chain ends without such a statement, the compiler SHALL refuse the
   use, and the diagnostic SHALL state the contract and the leftover statement as
   the failing case, in the program's own names.
5. A statement between two variables, such as `used <= capacity`, SHALL be usable
   at any step of a chain, not only when it matches the contract word for word.
6. A check that exceeds the compiler's proof budget SHALL be refused with a
   diagnostic distinct from a disproof. It SHALL say the proof was not completed
   and that an explicit check on the path would complete it. The budget is a
   safety net for a body with a very large number of interacting facts, not a
   limit programs are expected to meet.
7. This requirement governs variables of the integral family: int, uint, long,
   ulong, short, ushort, byte, sbyte, char and enums. Requirement 7 extends it
   to the other primitive types.

Example. `Reserve` declares `where needed <= available`.

```mist
type Capacity = int >= 0 and <= 1_000_000;

void Demo(int used, Capacity capacity)
{
    if(used <= capacity)
    {
        var doubled = capacity * 2;
        Reserve(used, doubled);
    }
}
```

Facts in hand at the call: `capacity >= 0`, `capacity <= 1_000_000`,
`used <= capacity`, `doubled == capacity * 2`. The broken contract is
`used >= doubled + 1`. The chain: `used <= capacity` with `used >= doubled + 1`
gives `doubled + 1 <= capacity`; substituting `doubled` gives
`capacity * 2 + 1 <= capacity`, which reduces to `capacity <= -1`; with
`capacity >= 0` no value satisfies both, so the call is proven. Remove the
`Capacity` type and the chain stops at `capacity <= -1`; stated over the call's
own values it is `doubled <= -2`, and that is the failing case the diagnostic
reports.

## Requirement 2 Which facts are recorded

1. A fact SHALL be a comparison, `==`, `<=` or `>=`, between two sides, where
   each side is a sum of variables, each multiplied by a whole-number constant,
   plus a constant. A fact MAY mention any number of variables:
   `count <= items.Length - start` is one fact. `<`, `>` and `!=` are spelled
   with these: `a < b` is `a <= b - 1`, and `a != b` is the pair of alternatives
   `a <= b - 1` or `a >= b + 1`.
2. A fact MAY carry alternatives. A proof over a fact with alternatives SHALL
   hold under each alternative.
3. There SHALL be no limit on the number of facts recorded about a variable. A
   fact leaves the list only by the rules Requirements 3, 4 and 5 state for
   writes, calls, joins and loops, never to save space.
4. The compiler SHALL record facts from each of these sources:
   1. The declared type of a variable, at its declaration and again after every
      write to it, since the type governs every write.
   2. A `where` clause on the enclosing method, constructor or indexer, inside
      its body, and the predicate of a dependent type parameter in scope.
   3. An initializer or assignment whose right side has the shape of item 1:
      `var a = b;` gives `a == b`, `var a = 7;` gives `a == 7`,
      `var a = b * 2 + 1;` gives `a == b * 2 + 1`, and `var total = used + extra;`
      gives `total == used + extra`.
   4. A condition on the current path. Within the branch a condition selects,
      the facts the condition states hold; within the branch it rejects, the facts
      its denial states hold. `and` contributes each operand, `or` contributes
      alternatives, and a relational pattern such as `is >= 0` contributes its
      comparison. A loop's condition holds in its body, and its denial holds after
      the loop exits through it.
   5. A call's result: what the callee's return type states, as
      [refinement-types](../refinement-types/requirements.md) reads it, bound to
      the variable that receives the result. Requirement 6 has the detail.
   6. The length of a sequence: `new Array<int, 7>` gives `a.Length == 7`, and a
      copy whose declaration states that its length follows its source gives
      `copy.Length == source.Length`.
5. An arithmetic fact SHALL be recorded only when the facts in hand prove that
   the operation stays within its result type's range. With `Capacity capacity`,
   `var doubled = capacity * 2;` records `doubled == capacity * 2`, because
   `capacity <= 1_000_000` proves the product fits an int. With `int capacity` it
   records only `doubled`'s type: a product that wrapped is not twice its operand,
   so the fact would be false, and a false fact is not a fact.
6. An operation not named here, among them `/`, `%`, shifts, bitwise operators,
   element reads and calls whose result type carries no refinement, SHALL record
   what its result type says and nothing more. Adding an operation to the
   language's facts means adding its rule to this list.

Example. The where clause is one fact with three variables, and it proves the
index.

```mist
void Show(Span<int> items, ZeroOrPositiveInt start, ZeroOrPositiveInt count)
    where count <= items.Length - start
{
    if(count >= 1)
    {
        Console.WriteLine(items[start]);
    }
}
```

`items[start]` requires `start <= items.Length - 1`. Facts in hand:
`start >= 0`, `count >= 1`, `count <= items.Length - start`. Assume the index is
out of range: `start >= items.Length`. With the where clause,
`start >= start + count`, so `count <= 0`, which `count >= 1` rules out.

## Requirement 3 Facts are about values, not names

A variable holds a value. Every write gives it a new value. Facts are recorded
about values, and a variable's facts at a point are the facts about the value it
holds there. Below, `x@1` names the value `x` holds after line 1.

1. A fact SHALL be about a value, never about a variable's name. A write to a
   variable SHALL begin a new value for it, whose facts start from the variable's
   declared type. The facts about the previous value SHALL stay as they are, and
   every fact that refers to the previous value keeps referring to it.
2. Reading a variable whose type copies on assignment, the integral family,
   every struct and an immutable type such as string, SHALL take the value it
   holds at that moment. `var y = x * 2;`
   records `y@2 == x@1 * 2`. The later write `x = Random();` begins `x@3` and
   changes nothing about `y@2` or `x@1`.
3. Nothing SHALL be recomputed or removed at a write. The name moves to the new
   value, and the facts about the previous value remain available to every proof
   that reaches them through another value's facts.
4. A variable of a reference type assigned from another SHALL refer to the same
   object, and facts about an object SHALL be shared by every variable that refers
   to it. Replacing which object a variable refers to begins a new value for that
   variable, and the facts about the previous object stay with that object.
5. Facts SHALL be recorded only about values nothing outside the body can write
   while the body runs: its locals and parameters, and the objects it owns or
   holds exclusively under the ownership rules. A value another party may write
   between a read and a use, such as a member of a shared object, SHALL
   contribute only its type's facts. A local that a lambda or a local function
   writes is such a value.
6. A call SHALL count as a write to every value it may write. A value handed to a
   callee with permission to write it, by ownership transfer, by a mutable borrow,
   or as the `this` of a method that may write the member, begins a new value
   after the call, unless the callee declares that it does not write it. A value
   the callee cannot reach is untouched. Whether a callee may write a value is
   the ownership and purity rules' judgement, not a judgement this spec makes.

Example.

```mist
var x = 5;
var y = x * 2;
x = Random();
NeedsTen(y);
```

Line 1 records `x@1 == 5`. Line 2 records `y@2 == x@1 * 2`, since `5 * 2` fits
an int. Line 3 begins `x@3`, which knows only that it is an int. Line 4 requires
`y == 10`. Assume it is broken, `y@2 != 10`: `y@2 == x@1 * 2` and `x@1 == 5`
give `y@2 == 10`, which rules the denial out. Nothing was worked out at line 3;
the facts line 4 needs were recorded at lines 1 and 2.

## Requirement 4 The bag at a line

Every point in a body, called a line here, has a bag: the facts in hand there.
The bag is what Requirement 1 chains over and what Requirements 2 and 3 add to.

1. The bag after a line SHALL be the bag before it, plus the facts the line
   records under Requirement 2, with the line's writes handled as Requirement 3
   states.
2. Where two paths meet, the bag SHALL be the alternatives: the first path's bag
   or the second path's bag. Nothing is merged per variable, so no pairing
   between variables is lost. A proof at a later line holds only when it holds
   under every alternative, as Requirement 2 item 2 states.
3. Alternatives that differ only in values a proof never reaches SHALL count as
   one alternative for that proof. The alternatives a proof pays for are the ways
   the values it touches were set, not the number of branches in the body.
4. The number of alternatives at a line SHALL be capped. When a join would
   exceed the cap, the compiler SHALL merge the oldest alternatives per variable,
   keeping for each variable what it is under any of them and losing the pairing
   between variables, until the count is within the cap. A refusal that rests on
   a merged fact SHALL say that paths were merged, so the failing case it reports
   is read as a possibility, not as a path the program takes. The cap's number
   belongs to the design, and the budget of Requirement 1 item 6 remains the
   backstop.

Example.

```mist
void Demo(bool flag)
{
    int a;
    int b;
    if(flag)
    {
        a = 1;
        b = 2;
    }
    else
    {
        a = 100;
        b = 200;
    }

    Needs(a, b);    // where a < b
}
```

The bag at the call is `a == 1 and b == 2` or `a == 100 and b == 200`. The
denial `a >= b` is ruled out under the first alternative by `1 >= 2` and under
the second by `100 >= 200`, so the call is proven. A bag that held `a == 1 or
a == 100` and `b == 2 or b == 200` separately would admit `a == 100, b == 2`,
which no path produces, and would refuse the call. That bag is not this spec.

## Requirement 5 Loops

A loop head is a join between the bag entering the loop and the bag at the end
of its body. Under Requirement 4 alone every round would add an alternative
without end, so the loop head is the one line where the compiler summarizes.

1. The compiler SHALL run the body a fixed number of rounds, then SHALL keep at
   the head every fact about a variable the loop writes that held on every round,
   and SHALL drop every such fact that changed between rounds.
2. The compiler SHALL then run the body once more from the summary and drop any
   kept fact that does not hold again at the end of the body, repeating until the
   summary holds.
3. Facts about variables the loop does not write SHALL stay as Requirement 3
   keeps them. They are not summarized.
4. The loop condition holds in the body and its denial holds after the loop exits
   through it, as Requirement 2 states. A path that leaves the loop by `break`
   joins the line after the loop under Requirement 4 with the bag at the `break`.
5. The number of rounds belongs to the design.
6. This is the floor, not the ceiling. A later requirement may teach the compiler
   a stronger summary for a loop shape it recognizes; it may only add facts to
   the head, never remove ones this requirement keeps.

Example.

```mist
void Fill(Span<int> items, int n) where n <= items.Length
{
    var i = 0;
    while(i < n)
    {
        items[i] = i;
        i = i + 1;
    }
}
```

`i >= 0` holds on every round and the upper bound on `i` moves every round, so
the head keeps `i >= 0` and `n <= items.Length`. In the body the condition adds
`i <= n - 1`. The index requires `i >= 0` and `i <= items.Length - 1`; the first
is in the bag, and the second follows from `i <= n - 1` and `n <= items.Length`.

## Requirement 6 Calls

1. At a call, the bag SHALL gain about the result exactly what the callee's
   return type states, as [refinement-types](../refinement-types/requirements.md)
   reads a return type, Requirement 18's lift included where that requirement
   applies. This spec SHALL NOT read a callee's body for any fact. A relation
   between the result and the arguments reaches the caller only through a
   refinement the signature states, and no such refinement exists today; that is
   the second stretch goal in Status and scope.
2. A callee's `where` clauses SHALL be proven at the call under Requirement 1,
   with each parameter name bound to the value passed for it. An argument that is
   an expression is a value with the facts Requirement 2 gives that expression,
   so `Reserve(used, capacity * 2)` is proven exactly as `Reserve(used, doubled)`
   is after `var doubled = capacity * 2;`.
3. Inside the callee, the parameter types and `where` clauses are facts in the
   bag at the first line of the body, as Requirement 2 states. Nothing the caller
   knew reaches the callee.
4. What a call does to the caller's values is Requirement 3 item 6.

Example.

```mist
int Double(int x)
{
    return x * 2;
}

var d = Double(n);
```

The bag after the call holds that `d` is an int and nothing relating `d` to
`n`. Refinement-types Requirement 18 lifts nothing here, because `x * 2` is an
arithmetic result. A contract on `d` that needs `d == n * 2` is refused until a
return refinement can state it.

## Requirement 7 The other primitive types

Requirements 1 to 6 are written for the integral family. This requirement
extends them to the rest of the primitive types: float, double, decimal, bool,
string and regex. With it, every primitive type the grammar names has its facts
defined by this spec.

1. Requirements 1 to 6 SHALL apply to every primitive type: equality between
   variables and, for an ordered type, order between them, recorded from
   conditions and assignments; values, bags, alternatives, joins, loops and
   calls exactly as stated. The chain in Requirement 1 does not depend on the
   type of the variables it combines.
2. For float, double and decimal, `<` and `>` SHALL be kept as written rather
   than respelled with `- 1`, and an arithmetic fact SHALL be recorded only when
   the operation is exact in that type, for the reason Requirement 2 item 5
   gives for wrapping: a result that rounded is not the value the fact names.
   The design lists the operations the compiler treats as exact.
3. For string, the facts SHALL be equality between variables, a literal value,
   and a match against a regex value, recorded from a regex-refined type such
   as ``type Email = string `^[^@]+@[^@]+\.[^@]+$`;``, from an `is` pattern that
   names a regex, and from a `where` clause. `s.Length` is an integer variable
   and falls under Requirements 1 to 6 with no further rule. A match SHALL be
   proven when the bag holds a literal value for the string and the pattern
   decides it, under today's match limit, or when the bag holds the same match
   for a variable the string is known to equal.
4. For regex, the facts SHALL be a literal pattern and equality between
   variables. A match fact names a regex value; the pattern it decides with is
   the literal that value is known to hold.
5. For bool, the facts SHALL be a literal and equality between variables, and
   `var ok = i < n;` SHALL record the two alternatives of Requirement 4,
   `ok == true and i <= n - 1` or `ok == false and i >= n`, so that a later
   `if(ok)` selects the first and proves what the condition proves. `not`, `and`
   and `or` over bool variables follow Requirement 2 item 4.4.

## Requirement 8 Compound assignment and increment

The grammar has `=` and `??=` and nothing else. This requirement adds the
compound forms and defines them by the assignment they stand for, so that every
other requirement here applies to them unchanged.

1. The language SHALL have `+=`, `-=`, `*=`, `/=` and `%=`, and `x op= e` SHALL
   mean `x = x op e`: the same operator selection, the same result type, the
   same wrapping for fixed-width `+`, `-` and `*`, and the same `Result` contract
   for `/` and `%` that [exceptions](../exceptions/requirements.md) states.
   Whatever is valid for the long form is valid for the short form, and nothing
   else is. `s += t` on strings is concatenation, and a user-defined operator is
   called.
2. The target SHALL be evaluated once. In `items[Next()] += 1`, `Next()` runs
   once, before `1` is evaluated, and the element it names is both read and
   written. Order: the target's receiver and index, then the value, then the
   operation, then the write.
3. The language SHALL have `++` and `--`, prefix and postfix, on the integral
   and real families, and as a statement `x++`, `++x`, `x--` and `--x` SHALL mean
   `x += 1` and `x -= 1`.
4. For the facts, `x op= e` SHALL be the assignment it stands for: the read of
   `x` is of the value it holds before the write (Requirement 3.2), the write
   begins a new value (Requirement 3.1), and `new == old op e` is recorded
   exactly when Requirement 2 would record it for `x = x op e`, no-wrap proof
   included.
5. `x++`, `++x`, `x--` and `--x` MAY appear inside an expression, as in C#.
   Postfix yields the value before the write and prefix the value after it, and
   the write happens where C# makes it happen. `items[i++]` reads `items[i]` and
   then increments `i`.
6. The facts MAY stop short of the expression form. An increment inside a larger
   expression MAY yield a value that knows only its type, while the statement
   form SHALL be recorded as item 4 states. This spec raises what the compiler
   can prove; it does not promise to prove everything the grammar admits.

Example.

```mist
var total = 0;
for(var i = 0; i < 10; i++)
{
    total += i;
}
```

`i++` is `i = i + 1`. Inside the body the bag holds `i >= 0` and `i <= 9` after
the head is summarized, so `i + 1` cannot wrap and the fact is recorded.
`total += i` is `total = total + i`; `total` is only known to be `>= 0` at the
head, its upper end having moved every round, so `total + i` is not proven to
fit an int and the new `total` knows only its type. A contract on `total` after
the loop is refused with `fails when total <= ...`, and a programmer who needs it
states it with a condition.
