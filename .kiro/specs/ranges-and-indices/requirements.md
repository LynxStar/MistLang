# Requirements Document

## Introduction

This document specifies the requirements for adding the two C# 8.0 expression-level "slicer operators" to the Mist programming language: the index-from-end operator `^` and the range operator `..`. Together these enable index-from-end access (`arr[^1]`) and slicing (`arr[1..3]`), matching C# 8.0 "Indexes and ranges" semantics exactly.

Mist is a C#-transpiled language: source flows through a lexer, a parser (Storm grammar), a semantic model builder, a type checker analyzer, and a C# transpiler backend. This feature threads two new expression forms through that pipeline.

The two operators are:

1. **Index-from-end operator `^`** — a prefix operator applied to an integer expression that produces a `System.Index` value counting from the end of a collection. `^1` refers to the last element and `^0` refers to the position one past the last element (equal to the length). Example: `numbers[^1]` reads the last element.

2. **Range operator `..`** — an infix operator that produces a `System.Range` value used for slicing. The start bound is inclusive and the end bound is exclusive, matching C# 8.0. All four bound forms are supported:
   - `a..b` — both bounds
   - `a..` — start only, slicing through to the end
   - `..b` — end only, slicing from the start
   - `..` — full range
   Either bound may itself be an index-from-end expression, for example `1..^1` (trim one element from each end) and `^3..` (last three elements).

When a `System.Range` value is used in an element-access position on a **sliceable** target, the range operator performs slicing; when a `System.Index` value is used in an element-access position, it performs index-from-end (or direct) element access. Sliceable targets are arrays, `Span<T>` / `ReadOnlySpan<T>`, `string`, and countable types that expose a `Length` or `Count` together with either a `Range` indexer or a `Slice(int, int)` method. Slicing an array produces a new array; slicing a span produces a view over the same backing memory; slicing a string produces a new string.

### Pattern Matching (List and Slice Patterns)

This spec also covers **list patterns** and **slice patterns** in pattern-matching position, matching C# 11 "List patterns" semantics. A list pattern `x is [1, .., 5]` tests a countable value positionally against an ordered sequence of element sub-patterns. A slice pattern `..` inside a list pattern matches zero or more contiguous elements that are not consumed by the surrounding element sub-patterns, and it may bind the matched sub-sequence to a variable, for example `x is [1, .. var middle, 5]`. List and slice patterns are usable everywhere the Grammar accepts a pattern: the operand of an `is` expression and the pattern of a `switch` arm. The Grammar already defines the `ListPattern` and `SliceElement` rules, and array-receiver list-pattern type checking already exists in part; this spec specifies the remaining semantic-model wiring, slice binding, type checking, match semantics, C# emission, and pretty-printing so that list and slice patterns are fully supported.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Source/Mist/Grammar/Mist.storm` and the token files under `Source/Mist/Grammar/tokens/`).
- **Lexer**: The component (`MistLexer`) that converts Mist source text into a token stream according to the token definitions.
- **Parser**: The component (`MistParser`) that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST, resolving type references and expression types.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model and emits diagnostics.
- **Emitter**: The C# code emission component (`CSharpEmitter`) that translates the Mist AST into C# source files.
- **From_End_Operator**: The prefix operator token `^`, written before an integer expression to produce an Index_Value counting from the end of a collection.
- **Range_Operator**: The infix operator token `..`, written between optional bound expressions to produce a Range_Value.
- **Index_Expression**: An expression of the form `^operand` where `operand` is an integer-typed expression. Its value is an Index_Value.
- **Range_Expression**: An expression of the form `start .. end` where `start` and `end` are each optional bound expressions. Its value is a Range_Value.
- **Index_Value**: A value of the CLR type `System.Index`, surfaced in Mist as the type named `Index` in the `Standard` namespace. Represents a position that is either from the start or from the end of a countable sequence.
- **Range_Value**: A value of the CLR type `System.Range`, surfaced in Mist as the type named `Range` in the `Standard` namespace. Represents a start Index_Value (inclusive) and an end Index_Value (exclusive).
- **Element_Access**: The postfix expression form `receiver[argument]` (the grammar's `IndexSuffix`), where `argument` is an expression evaluated to select or slice from `receiver`.
- **Countable_Type**: A type that exposes an integer-typed `Length` property or an integer-typed `Count` property, allowing an Index_Value to be resolved to a concrete offset.
- **Indexable_Type**: A Countable_Type that also exposes an indexer accepting an integer argument (for example arrays, `Span<T>`, `ReadOnlySpan<T>`, and `string`).
- **Sliceable_Type**: A Countable_Type from which a contiguous sub-sequence can be produced using a Range_Value. This includes arrays, `Span<T>`, `ReadOnlySpan<T>`, `string`, and any Countable_Type that exposes either a `Range`-accepting indexer or a `Slice(int, int)` method.
- **Inclusive_Start**: The property that the start bound of a Range_Value selects the element at that offset.
- **Exclusive_End**: The property that the end bound of a Range_Value selects up to, but not including, the element at that offset.
- **Compile_Time_Type**: The static type of an expression as determined by the Compiler during type checking.
- **CSHARP_FEATURES**: The feature-tracking document `CSHARP_FEATURES.md` at the repository root.
- **Pattern**: A syntactic form (the Grammar's `Pattern` rule) used to test and optionally destructure a value in a Pattern_Match_Position.
- **Pattern_Match_Position**: Any position where the Grammar accepts a Pattern, namely the operand of an `is` expression (the `IsExpression` form) and the pattern of a `switch` arm (the `SwitchArm` rule).
- **List_Pattern**: A Pattern of the form `[element0, element1, ...]` (the Grammar's `ListPattern` rule) that matches a countable receiver positionally against an ordered sequence of List_Pattern_Elements.
- **List_Pattern_Element**: A single entry of a List_Pattern, which is either an element sub-pattern (any Pattern) or a Slice_Pattern.
- **Slice_Pattern**: A List_Pattern_Element written with the Range_Operator `..` (the Grammar's `SliceElement` rule) that matches zero or more contiguous elements not consumed by the surrounding element sub-patterns, optionally binding the matched sub-sequence through a Slice_Binding.
- **Slice_Binding**: The optional variable introduced by a Slice_Pattern that binds the matched contiguous sub-sequence (for example `middle` in `.. var middle`).

## Requirements

### Requirement 1: From-End and Range Operator Tokens

**User Story:** As a Mist developer, I want the compiler to recognize `^` and `..` as expression operators, so that I can write index-from-end and range expressions.

#### Acceptance Criteria

1. THE Grammar SHALL define a token for the From_End_Operator with the lexeme `^`.
2. THE Grammar SHALL reuse the existing `DOTDOT` token (lexeme `..`) for the Range_Operator without redefining or renaming it.
3. WHEN the Lexer encounters the character `^` in source text, THE Lexer SHALL produce a single From_End_Operator token.
4. WHEN the Lexer encounters the integer literal, `..`, integer literal sequence `N..M` (for example `0..2`), THE Lexer SHALL produce an integer-literal token, a `DOTDOT` token, and an integer-literal token, without interpreting the trailing `.` of the first integer as the start of a floating-point literal.
5. WHERE the From_End_Operator token `^` appears, THE Grammar SHALL NOT treat `^` as a bitwise-exclusive-or operator, because Mist defines no bitwise-exclusive-or operator.

### Requirement 2: Index-From-End Expression Parsing

**User Story:** As a Mist developer, I want to write `^n` to refer to a position measured from the end of a collection, so that I can access trailing elements without computing offsets manually.

#### Acceptance Criteria

1. WHEN the Parser reads the From_End_Operator `^` immediately followed by an operand expression, THE Parser SHALL produce an Index_Expression AST node that captures the operand expression.
2. THE Parser SHALL accept an integer literal as the operand of an Index_Expression (for example `^1`, `^0`, `^10`).
3. THE Parser SHALL accept an identifier or member-access expression as the operand of an Index_Expression (for example `^count`, `^items.Length`).
4. THE Parser SHALL accept a parenthesized expression as the operand of an Index_Expression (for example `^(count + 1)`).
5. THE Parser SHALL accept an Index_Expression in every value position where a Postfix expression is accepted, including variable initializers, assignment right-hand sides, method arguments, return values, and the argument of an Element_Access.

### Requirement 3: Range Expression Parsing

**User Story:** As a Mist developer, I want to write all four range forms, so that I can express any contiguous slice bounds.

#### Acceptance Criteria

1. WHEN the Parser reads a start expression, the Range_Operator `..`, and an end expression, THE Parser SHALL produce a Range_Expression AST node capturing both the start bound and the end bound (form `a..b`).
2. WHEN the Parser reads a start expression followed by the Range_Operator `..` with no following end expression, THE Parser SHALL produce a Range_Expression AST node capturing the start bound and recording that the end bound is absent (form `a..`).
3. WHEN the Parser reads the Range_Operator `..` followed by an end expression with no preceding start expression, THE Parser SHALL produce a Range_Expression AST node capturing the end bound and recording that the start bound is absent (form `..b`).
4. WHEN the Parser reads the Range_Operator `..` with neither a preceding start expression nor a following end expression, THE Parser SHALL produce a Range_Expression AST node recording that both bounds are absent (form `..`).
5. WHERE a bound of a Range_Expression is an Index_Expression, THE Parser SHALL accept the Index_Expression as that bound (for example `1..^1`, `^3..`, `..^2`).
6. THE Parser SHALL accept a Range_Expression in every value position where a Postfix expression is accepted, including variable initializers, assignment right-hand sides, method arguments, return values, and the argument of an Element_Access.

### Requirement 4: Operator Precedence and Associativity

**User Story:** As a Mist developer, I want `^` and `..` to bind with the same precedence as C# 8.0, so that expressions parse the way they do in C#.

#### Acceptance Criteria

1. THE Parser SHALL bind the From_End_Operator `^` tighter than the Range_Operator `..`, so that `^1..^2` parses as `(^1)..(^2)`.
2. THE Parser SHALL treat each bound of a Range_Expression as a unary-level expression, so that a bound composed with a binary operator requires parentheses (for example `..(n - 1)` is accepted and `..n - 1` parses as `(..n) - 1`), matching C# 8.0.
3. THE Parser SHALL bind the From_End_Operator `^` tighter than binary arithmetic operators, so that `^a + b` parses as `(^a) + b`.
4. THE Parser SHALL parse a bare Index_Expression that is not followed by the Range_Operator as a standalone Index_Value expression (for example `var i = ^2;`).

### Requirement 5: Index and Range Type Resolution

**User Story:** As a Mist developer, I want `^` and `..` expressions to resolve to well-defined Index and Range types, so that I can store, pass, and reuse them like any other value.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL resolve the Compile_Time_Type of an Index_Expression to the Index_Value type (`System.Index`).
2. THE Semantic_Model_Builder SHALL resolve the Compile_Time_Type of a Range_Expression to the Range_Value type (`System.Range`), independently of which bound forms are present.
3. THE Compiler SHALL surface the Index_Value type as a spellable type named `Index` in the `Standard` namespace, so that a developer can declare a variable, parameter, field, or return value of type `Index`.
4. THE Compiler SHALL surface the Range_Value type as a spellable type named `Range` in the `Standard` namespace, so that a developer can declare a variable, parameter, field, or return value of type `Range`.

   > Note: The simple name `Range` is intentionally retained for the language slice type (`System.Range`). Pre-existing user-defined types named `Range` are migrated to non-conflicting names per Requirement 18 (Migration of Existing `Range` Type Names), so that surfacing `Standard.Range` does not conflict with existing user-defined code.
5. WHEN an Index_Value is assigned to a variable, parameter, field, or return position typed as `Index`, THE Type_Checker SHALL accept the assignment.
6. WHEN a Range_Value is assigned to a variable, parameter, field, or return position typed as `Range`, THE Type_Checker SHALL accept the assignment.

### Requirement 6: Index-From-End Operand Type Checking

**User Story:** As a Mist developer, I want the compiler to verify that `^` is applied to an integer, so that I get a clear error instead of a malformed index.

#### Acceptance Criteria

1. WHEN the operand of an Index_Expression has an integer-compatible Compile_Time_Type, THE Type_Checker SHALL accept the Index_Expression.
2. IF the operand of an Index_Expression has a Compile_Time_Type that is not integer-compatible, THEN THE Type_Checker SHALL emit a diagnostic error naming the operand type and stating that the From_End_Operator requires an integer operand.
3. IF the operand of an Index_Expression has the Compile_Time_Type `void`, THEN THE Type_Checker SHALL emit a diagnostic error stating that a void-typed expression cannot be used as an index-from-end operand.

### Requirement 7: Range Bound Type Checking

**User Story:** As a Mist developer, I want the compiler to verify that range bounds are valid, so that malformed ranges are rejected at compile time.

#### Acceptance Criteria

1. WHEN a present bound of a Range_Expression has an integer-compatible Compile_Time_Type, THE Type_Checker SHALL accept that bound.
2. WHEN a present bound of a Range_Expression has the Index_Value type (`Index`), THE Type_Checker SHALL accept that bound.
3. IF a present bound of a Range_Expression has a Compile_Time_Type that is neither integer-compatible nor the Index_Value type, THEN THE Type_Checker SHALL reject the entire Range_Expression and emit a diagnostic error naming the bound type and stating that a range bound must be an integer or an `Index`.
4. WHERE a bound of a Range_Expression is syntactically absent, THE Type_Checker SHALL treat that absent bound as the start of the sequence (for an absent start) or the end of the sequence (for an absent end).
5. THE Type_Checker SHALL NOT treat a present-but-invalid bound as an absent bound; a present bound with an invalid type rejects the whole Range_Expression per criterion 3.

### Requirement 8: Sliceable and Indexable Target Type Checking

**User Story:** As a Mist developer, I want the compiler to allow index-from-end and range slicing only on targets that support them, so that unsupported uses are caught at compile time.

#### Acceptance Criteria

1. WHEN an Index_Value is used as the argument of an Element_Access whose receiver is an Indexable_Type, THE Type_Checker SHALL accept the Element_Access and set its Compile_Time_Type to the element type of the receiver.
2. WHEN a Range_Value is used as the argument of an Element_Access whose receiver is an array type, THE Type_Checker SHALL accept the Element_Access and set its Compile_Time_Type to the same array type.
3. WHEN a Range_Value is used as the argument of an Element_Access whose receiver is `Span<T>` or `ReadOnlySpan<T>`, THE Type_Checker SHALL accept the Element_Access and set its Compile_Time_Type to the receiver's span type.
4. WHEN a Range_Value is used as the argument of an Element_Access whose receiver is `string`, THE Type_Checker SHALL accept the Element_Access and set its Compile_Time_Type to `string`.
5. WHEN a Range_Value is used as the argument of an Element_Access whose receiver is a Sliceable_Type exposing a `Range`-accepting indexer or a `Slice(int, int)` method, THE Type_Checker SHALL accept the Element_Access and set its Compile_Time_Type to the slice result type declared by that indexer or method.
6. IF an Index_Value is used as the argument of an Element_Access whose receiver is not an Indexable_Type, THEN THE Type_Checker SHALL emit a diagnostic error naming the receiver type and stating that index-from-end access requires a countable, indexable receiver.
7. IF a Range_Value is used as the argument of an Element_Access whose receiver is not a Sliceable_Type, THEN THE Type_Checker SHALL emit a diagnostic error naming the receiver type and stating that range slicing requires a sliceable receiver.

### Requirement 9: Index-From-End Element Access Semantics

**User Story:** As a Mist developer, I want `arr[^n]` to select the element `n` positions from the end, so that trailing-element access behaves exactly like C# 8.0.

#### Acceptance Criteria

1. WHEN an Element_Access uses an Index_Value produced by `^k` on an Indexable_Type of length `L`, THE Compiler SHALL resolve the accessed offset to `L - k`.
2. THE Compiler SHALL treat `^0` as the offset equal to the receiver length `L` (one position past the last element).
3. THE Compiler SHALL treat `^1` as the offset of the last element (`L - 1`).
4. WHEN an Element_Access uses an Index_Value that represents a from-start position (an ordinary integer index), THE Compiler SHALL resolve the accessed offset to that integer directly.

### Requirement 10: Range Element Access (Slicing) Semantics

**User Story:** As a Mist developer, I want `collection[a..b]` to produce the sub-sequence from `a` inclusive to `b` exclusive, so that slicing behaves exactly like C# 8.0.

#### Acceptance Criteria

1. WHEN an Element_Access uses a Range_Value with start offset `s` and end offset `e` on a Sliceable_Type, THE Compiler SHALL produce the sub-sequence containing the elements at offsets `s` through `e` minus one, honoring Inclusive_Start and Exclusive_End.
2. WHERE the start bound of the Range_Value is absent, THE Compiler SHALL use offset `0` as the start.
3. WHERE the end bound of the Range_Value is absent, THE Compiler SHALL use the receiver length as the end.
4. WHEN a bound of the Range_Value is an Index_Value produced by `^k`, THE Compiler SHALL resolve that bound offset to the receiver length minus `k`.
5. WHEN an Element_Access slices an array, THE Compiler SHALL produce a new array containing copies of the selected elements, including when the Range_Value covers the full array (`..`), matching C# 8.0 (no view optimization).
6. WHEN an Element_Access slices a `Span<T>` or `ReadOnlySpan<T>`, THE Compiler SHALL produce a span that is a view over the same backing memory as the receiver.
7. WHEN an Element_Access slices a `string`, THE Compiler SHALL produce a new string containing the selected characters.

### Requirement 11: C# Code Emission

**User Story:** As a Mist developer, I want the transpiled C# to use native `System.Index`, `System.Range`, and slicing, so that the compiled output has the same behavior and performance as hand-written C# 8.0.

#### Acceptance Criteria

1. WHEN the Emitter emits an Index_Expression `^operand`, THE Emitter SHALL emit the C# index-from-end expression `^(<operand>)` where `<operand>` is the C# emission of the operand.
2. WHEN the Emitter emits a Range_Expression, THE Emitter SHALL emit the corresponding C# range expression preserving the present and absent bounds (`<start>..<end>`, `<start>..`, `..<end>`, or `..`).
3. WHEN the Emitter emits an Element_Access whose argument is an Index_Value, THE Emitter SHALL emit a C# element-access expression `<receiver>[<indexArgument>]`.
4. WHEN the Emitter emits an Element_Access whose argument is a Range_Value, THE Emitter SHALL emit a C# element-access expression `<receiver>[<rangeArgument>]`, relying on the C# compiler's built-in slicing lowering for arrays, spans, and strings.
5. WHEN the Emitter emits an Element_Access whose argument is a Range_Value on a Sliceable_Type that exposes a `Slice(int, int)` method but no `Range`-accepting indexer, THE Emitter SHALL emit C# that produces the equivalent slice through that type's supported slicing member.
6. THE Emitter SHALL emit Index_Expression and Range_Expression operands with parentheses where required to preserve evaluation order and operator precedence.

### Requirement 12: Diagnostics for Invalid Uses

**User Story:** As a Mist developer, I want clear, specific error messages when I misuse `^` or `..`, so that I can quickly understand and fix the problem.

#### Acceptance Criteria

1. WHEN the Type_Checker rejects an Index_Expression because the operand is not integer-compatible, THE Compiler SHALL emit a distinct diagnostic that names the operand type and identifies the source span of the Index_Expression.
2. WHEN the Type_Checker rejects a Range_Expression because a bound is neither integer-compatible nor an `Index`, THE Compiler SHALL emit a distinct diagnostic that names the offending bound type and identifies the source span of the bound.
3. WHEN the Type_Checker rejects an Element_Access because an Index_Value is applied to a non-indexable receiver, THE Compiler SHALL emit a distinct diagnostic that names the receiver type and identifies the source span of the Element_Access.
4. WHEN the Type_Checker rejects an Element_Access because a Range_Value is applied to a non-sliceable receiver, THE Compiler SHALL emit a distinct diagnostic that names the receiver type and identifies the source span of the Element_Access.
5. THE Compiler SHALL register each diagnostic in this feature through the diagnostic descriptor system used by the existing type-checker diagnostics, with a unique diagnostic identifier assigned during design.

### Requirement 13: Pretty-Printer Support and Round-Trip

**User Story:** As a Mist developer, I want the pretty-printer to reproduce `^` and `..` expressions faithfully, so that formatting preserves program meaning.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format an Index_Expression as `^` immediately followed by the formatted operand, with no space between the operator and the operand.
2. THE Pretty_Printer SHALL format a Range_Expression preserving the present and absent bounds (`a..b`, `a..`, `..b`, or `..`), with no space between a bound and the Range_Operator.
3. THE Pretty_Printer SHALL format an Element_Access whose argument is an Index_Expression or a Range_Expression using bracket syntax `receiver[argument]`.
4. FOR ALL valid Mist ASTs containing Index_Expressions and Range_Expressions, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 14: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want the `^` and `..` operators to be syntax-highlighted, so that index-from-end and range expressions are visually distinct.

#### Acceptance Criteria

1. THE TextMate grammar (`Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`) SHALL recognize the From_End_Operator `^` in expression position and highlight it with the operator scope `keyword.operator.mist`.
2. THE TextMate grammar SHALL recognize the Range_Operator `..` in expression position and highlight it with the operator scope `keyword.operator.mist`.
3. WHEN a Mist source file contains `^` or `..` in an expression, THE TextMate grammar SHALL assign the operator the same scope category used for other Mist operators, so that theme-based styling renders it consistently (the grammar assigns scope; the editor theme controls visual style).
4. WHERE the Range_Operator `..` appears as a Slice_Pattern inside a list pattern, THE TextMate grammar SHALL highlight it with the operator scope `keyword.operator.mist`, reusing the existing `..` token highlighting and introducing no new tokens for list or slice patterns.

### Requirement 15: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating indexes and ranges, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/RangesAndIndices/Program.mist`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: index-from-end access (`^1`, `^2`); the four range forms (`a..b`, `a..`, `..b`, `..`); index-from-end bounds within ranges (`1..^1`, `^3..`); index-from-end and range access on arrays; range slicing on `Span<T>` or `ReadOnlySpan<T>` demonstrating view semantics; range slicing on `string`; and storing an `Index` value and a `Range` value in typed variables and reusing them in an Element_Access.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `RangesAndIndices.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.
6. The sample SHALL contain the Proof Code below verbatim within `RunDemo`, and running the sample SHALL produce the expected output below exactly.
7. The sample SHALL additionally demonstrate list and slice pattern matching within the Proof Code below, including: a fixed-length list pattern in an `is` expression; an element sub-pattern combined with a discard element and a `var` binding; a slice pattern that binds the middle sub-sequence (`.. var middle`); a discard slice pattern (`..`); a list pattern used in a `switch` arm; and an empty list pattern (`[]`).

#### Proof Code

```mist
using Standard;

namespace RangesAndIndices;

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== Ranges and Indices ===");

        var numbers = [10, 20, 30, 40, 50];

        // PROOF: the `^` operator lexes and parses, produces a System.Index, and
        // index-from-end access resolves ^1 to the last element and ^2 to the
        // second-to-last element. If `^` were not implemented this fails to lex/parse.
        Console.WriteLine("--- Index from end ---");
        var last = numbers[^1];
        Console.WriteLine($"last={last}");
        var secondToLast = numbers[^2];
        Console.WriteLine($"secondToLast={secondToLast}");

        // PROOF: the `..` operator in expression position parses with both bounds and
        // slices [start inclusive, end exclusive) producing a NEW array of length 3.
        // Inclusive-start/exclusive-end semantics are falsified if the length or
        // element values differ.
        Console.WriteLine("--- Range both bounds ---");
        var middle = numbers[1..4];
        Console.WriteLine($"middleLength={middle.Length}");
        Console.WriteLine($"middle0={middle[0]}");
        Console.WriteLine($"middle2={middle[2]}");

        // PROOF: start-only range `a..` slices through to the end.
        Console.WriteLine("--- Range start only ---");
        var tail = numbers[2..];
        Console.WriteLine($"tailLength={tail.Length}");
        Console.WriteLine($"tail0={tail[0]}");

        // PROOF: end-only range `..b` slices from the start.
        Console.WriteLine("--- Range end only ---");
        var head = numbers[..2];
        Console.WriteLine($"headLength={head.Length}");
        Console.WriteLine($"head0={head[0]}");

        // PROOF: full range `..` copies the whole collection.
        Console.WriteLine("--- Full range ---");
        var all = numbers[..];
        Console.WriteLine($"allLength={all.Length}");

        // PROOF: an index-from-end expression is accepted as a range bound (`1..^1`),
        // trimming one element from each end; the from-end bound resolves to L - k.
        Console.WriteLine("--- Range with from-end bound ---");
        var trimmed = numbers[1..^1];
        Console.WriteLine($"trimmedLength={trimmed.Length}");
        Console.WriteLine($"trimmed0={trimmed[0]}");
        Console.WriteLine($"trimmedLast={trimmed[^1]}");

        // PROOF: `^k` resolves to a spellable `Index` value and `a..b` to a spellable
        // `Range` value; both can be stored in typed locals and reused in element access.
        Console.WriteLine("--- Stored Index and Range ---");
        Index fromEnd = ^2;
        var valueAtStored = numbers[fromEnd];
        Console.WriteLine($"valueAtStored={valueAtStored}");
        Range r = 0..2;
        var sliceFromStored = numbers[r];
        Console.WriteLine($"slice0={sliceFromStored[0]}");
        Console.WriteLine($"slice1={sliceFromStored[1]}");

        // PROOF: a fixed-length list pattern `[1, 2, 3]` matches a countable receiver
        // positionally, succeeding only when the length and every element pattern
        // match; a `var` element pattern binds the element at its offset. If list
        // patterns were unimplemented this fails to compile or matches incorrectly.
        Console.WriteLine("--- List pattern (fixed length) ---");
        var triple = [1, 2, 3];
        if (triple is [1, 2, 3])
        {
            Console.WriteLine("tripleMatched=true");
        }
        if (triple is [1, _, var third])
        {
            Console.WriteLine($"third={third}");
        }

        // PROOF: a slice pattern binds the matched contiguous middle sub-sequence with
        // `.. var middle`, while the element patterns before and after bind from the
        // start and from the end. Slicing an array yields a new array, so middle has
        // length 3 with middle[0]=20. Falsified if slice binding is not implemented.
        Console.WriteLine("--- Slice pattern with binding ---");
        if (numbers is [var patHead, .. var middle, var patLast])
        {
            Console.WriteLine($"patHead={patHead}");
            Console.WriteLine($"patLast={patLast}");
            Console.WriteLine($"middleLen={middle.Length}");
            Console.WriteLine($"middleFirst={middle[0]}");
        }

        // PROOF: a discard slice pattern `..` matches the remaining elements without
        // binding, so `[10, ..]` tests only the first element.
        Console.WriteLine("--- Slice pattern discard ---");
        if (numbers is [10, ..])
        {
            Console.WriteLine("startsWith10=true");
        }

        // PROOF: list patterns are accepted in switch-arm position; `[]` matches a
        // zero-length receiver and `[_]` matches a single-element receiver.
        Console.WriteLine("--- List pattern in switch ---");
        var label = numbers switch {
            [] => "empty",
            [_] => "single",
            _ => "many"
        };
        Console.WriteLine($"label={label}");

        // PROOF: an empty list pattern `[]` matches only a zero-length receiver.
        Console.WriteLine("--- Empty list pattern ---");
        var emptyArr = numbers[0..0];
        if (emptyArr is [])
        {
            Console.WriteLine("emptyMatched=true");
        }

        // PROOF: a slice pattern may carry a subpattern beyond a bare `var` binding or
        // discard. `[.. var rest, 50]` is a slice-before-element binding: the slice
        // binds the leading sub-sequence (rest = [10, 20, 30, 40], length 4) and the
        // match succeeds only when the final element is 50. `[10, .. [.., 40, 50]]`
        // matches the trailing slice against a NESTED list pattern requiring the
        // sub-sequence to end with 40 then 50. Both are falsified if slice subpatterns
        // beyond var/discard are unimplemented.
        Console.WriteLine("--- Slice subpattern (beyond var/discard) ---");
        if (numbers is [.. var rest, 50])
        {
            Console.WriteLine($"restLen={rest.Length}");
            Console.WriteLine($"restFirst={rest[0]}");
        }
        if (numbers is [10, .. [.., 40, 50]])
        {
            Console.WriteLine("nestedSliceMatched=true");
        }

        Console.WriteLine("Done!");
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== Ranges and Indices ===
--- Index from end ---
last=50
secondToLast=40
--- Range both bounds ---
middleLength=3
middle0=20
middle2=40
--- Range start only ---
tailLength=3
tail0=30
--- Range end only ---
headLength=2
head0=10
--- Full range ---
allLength=5
--- Range with from-end bound ---
trimmedLength=3
trimmed0=20
trimmedLast=40
--- Stored Index and Range ---
valueAtStored=40
slice0=10
slice1=20
--- List pattern (fixed length) ---
tripleMatched=true
third=3
--- Slice pattern with binding ---
patHead=10
patLast=50
middleLen=3
middleFirst=20
--- Slice pattern discard ---
startsWith10=true
--- List pattern in switch ---
label=many
--- Empty list pattern ---
emptyMatched=true
--- Slice subpattern (beyond var/discard) ---
restLen=4
restFirst=10
nestedSliceMatched=true
Done!
```

### Requirement 16: Book Documentation

**User Story:** As a Mist developer, I want the language book to document indexes and ranges, so that I can learn the syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a section covering the From_End_Operator `^` and the Range_Operator `..` with compiled code examples.
2. THE book section SHALL demonstrate: index-from-end access; the four range forms; index-from-end bounds within ranges; inclusive-start and exclusive-end slicing semantics; slicing arrays (new array), spans (view), and strings (new string); and storing `Index` and `Range` values.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
4. THE book's SUMMARY.md SHALL reference the new section in the appropriate chapter.
5. THE book SHALL document list patterns and slice patterns, demonstrating: fixed-length list patterns; element sub-patterns; the slice pattern `..`; slice binding with `.. var name`; list patterns used in `is` expressions and `switch` arms; the empty list pattern `[]`; and the at-most-one-slice-per-list-pattern constraint. All examples SHALL be compiled snippets following the Book Snippet Policy.

### Requirement 17: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the indexes and ranges feature, so that feature tracking stays accurate.

#### Acceptance Criteria

1. WHEN the indexes and ranges feature is implemented, THE Compiler team SHALL add or update entries in CSHARP_FEATURES for the index-from-end operator `^` and the range operator `..` and mark them implemented.
2. WHEN list and slice pattern matching is implemented, THE Compiler team SHALL add or update entries in CSHARP_FEATURES for list patterns and slice patterns and mark them implemented.
3. THE Compiler team SHALL update the Summary table at the bottom of CSHARP_FEATURES to reflect the new implementation counts.

### Requirement 18: Migration of Existing `Range` Type Names

**User Story:** As a Mist project maintainer, I want pre-existing user-defined types named `Range` renamed to non-conflicting names, so that introducing `Standard.Range` reserves the simple name `Range` for the language slice type without creating ambiguity or confusion with existing code.

#### Acceptance Criteria

1. THE Compiler team SHALL rename every pre-existing user-defined type declaration named `Range` that collides with the new `Standard.Range` type to a name that does not contain the word `Range`, because introducing `Standard.Range` reserves the simple name `Range` for the language slice type and renaming avoids ambiguity between user-defined types and the slice type.

2. THE Compiler team SHALL rename the `Range` class declared in `samples/Iterators/Program.mist` (the `0..N` counting enumerable containing the nested `Enumerator` struct) to `CountSequence`, updating the constructor name, every `new Range(...)` call site (including `new Range(4)` and `new Range(count)`), and every comment that references the type.

3. WHERE the `Range` class in `samples/Iterators/Program.mist` is renamed, THE Compiler team SHALL keep the sample's expected stdout baseline unchanged, because the observable output (`x=0`, `x=1`, and so on) does not contain the type name.

4. THE Compiler team SHALL rename the `Range` class declared in `samples/RefinementTypes/Program.mist` to `Interval` and rename the associated type alias `ValidRange` to `ValidInterval`, updating the constructor name, the alias declaration, every `new Range(...)` call site (including `new Range(10, 50)`), and every refinement pattern that names the alias (including `is ValidRange validR`).

5. WHERE the `Range` class in `samples/RefinementTypes/Program.mist` is renamed, THE Compiler team SHALL update the `Describe()` method's returned string literal to match the new type name (for example `Interval(10, 50)`) and update the sample's expected-output baseline to reflect the renamed output.

6. THE Compiler team SHALL rename the `Range` class and the `ValidRange` alias in `book/snippets/type-system/refinement-types/property-pattern.mist` consistently to `Interval` and `ValidInterval`, updating the constructor name, the alias declaration, every `new Range(...)` call site (including `new Range(10, 50)`), and every refinement pattern that names the alias (including `is ValidRange valid`).

7. WHERE a migrated `.mist` book snippet has a corresponding generated `.cs` snippet, THE Compiler team SHALL regenerate that `.cs` snippet in accordance with the Book Snippet Policy.

8. WHERE book chapter prose references a migrated snippet's `Range` type, THE Compiler team SHALL update that prose to use the new type name.

9. WHEN the migration is complete, every affected sample and snippet SHALL compile and run without errors and SHALL produce output consistent with the renamed types, with expected-output baselines updated wherever the type name is observable.

10. THE Compiler team SHALL leave method declarations named `Range` unchanged, specifically `public static string Range(int min, int max)` in `book/snippets/type-system/refinement-types/adhoc-parameters.mist` and in `samples/AdhocRefinementConstraints/Program.mist`, because a method name occupies a separate declaration space and does not collide with the `Standard.Range` type.

11. THE Compiler team SHALL leave the attribute usage `[Range(1, 100)]` in `book/snippets/advanced/attributes/attribute-arguments.mist` unchanged, because that name resolves in attribute context and does not refer to the `Standard.Range` struct.

12. WHERE the Compiler team chooses replacement names during implementation, THE Compiler team MAY adjust the suggested names (`CountSequence`, `Interval`, and `ValidInterval`) provided the chosen names do not reintroduce the name `Range`.

### Requirement 19: List and Slice Pattern Parsing

**User Story:** As a Mist developer, I want to write list patterns such as `x is [1, .., 5]` and slice patterns such as `x is [first, .. var rest]`, so that I can match and destructure countable values by shape.

#### Acceptance Criteria

1. WHEN the Parser reads a `[` token, an optional comma-separated sequence of List_Pattern_Elements, and a `]` token in a Pattern_Match_Position, THE Parser SHALL produce a List_Pattern AST node that captures the List_Pattern_Elements in source order.
2. THE Parser SHALL accept any Pattern as an element sub-pattern of a List_Pattern, including constant, relational, type, `var`, discard, property, tuple, and nested List_Pattern forms.
3. WHEN the Parser reads the Range_Operator `..` as a List_Pattern_Element optionally followed by any Pattern, THE Parser SHALL produce a Slice_Pattern element that captures the optional subpattern, accepting all of: no subpattern (`..`), a discard subpattern (`.. _`), a `var` binding subpattern (`.. var name`), a declaration subpattern (for example `.. int[] rest`), a nested List_Pattern subpattern (for example `.. [.., 5]`), and a property subpattern (for example `.. { Length: > 0 }`).
4. WHEN the Parser reads the Range_Operator `..` as a List_Pattern_Element with no following subpattern (`..`), THE Parser SHALL produce a Slice_Pattern element that records the absence of a subpattern.
5. THE Parser SHALL accept a List_Pattern as the operand of an `is` expression and as the pattern of a `switch` arm.
6. THE Parser SHALL accept an empty List_Pattern `[]` that contains no List_Pattern_Elements.
7. WHEN the Parser reads `[` in a Pattern_Match_Position, THE Parser SHALL produce a List_Pattern rather than an array-literal expression, so that `scrutinee is [ ... ]` is parsed as a list pattern.

> Note: The Grammar's existing `SliceElement` rule binds a slice through a bare identifier (`.. name`). Criterion 3 generalizes the `SliceElement` rule to accept a full optional subpattern after `..` (rather than only a bare identifier or `var name`), matching C# 11; the design reconciles the `SliceElement` rule accordingly.

### Requirement 20: List and Slice Pattern Type Checking

**User Story:** As a Mist developer, I want the compiler to verify that list and slice patterns are applied to appropriate values, so that invalid pattern matches are caught at compile time.

#### Acceptance Criteria

1. WHEN a List_Pattern is matched against a receiver whose Compile_Time_Type is an Indexable_Type, THE Type_Checker SHALL accept the List_Pattern and type-check each element sub-pattern against the element type of the receiver.
2. WHEN a List_Pattern contains a Slice_Pattern that carries a subpattern, THE Type_Checker SHALL require the receiver Compile_Time_Type to be a Sliceable_Type and SHALL type-check that subpattern against the receiver's slice result type, and WHERE that subpattern is a binding (a `var` binding or a declaration pattern) THE Type_Checker SHALL set the Compile_Time_Type of the bound variable to that slice result type.
3. IF a List_Pattern is matched against a receiver that is not an Indexable_Type, THEN THE Type_Checker SHALL emit a diagnostic error naming the receiver type and stating that list-pattern matching requires a countable, indexable receiver.
4. IF a List_Pattern contains more than one Slice_Pattern element, THEN THE Type_Checker SHALL emit a diagnostic error stating that a list pattern may contain at most one slice pattern.
5. IF a Slice_Pattern that carries a subpattern is matched against a receiver that is not a Sliceable_Type, THEN THE Type_Checker SHALL emit a diagnostic error naming the receiver type and stating that a slice subpattern requires a sliceable receiver.
6. THE Type_Checker SHALL register each diagnostic added for list and slice patterns through the diagnostic descriptor system used by the existing type-checker diagnostics, with a unique diagnostic identifier assigned during design.

### Requirement 21: List and Slice Pattern Matching Semantics

**User Story:** As a Mist developer, I want list and slice patterns to match exactly like C# 11, so that shape tests and destructuring behave predictably.

#### Acceptance Criteria

1. WHEN a List_Pattern that contains no Slice_Pattern is matched against a receiver, THE Compiler SHALL evaluate the match to true only when the receiver length equals the number of element sub-patterns and every element sub-pattern matches the element at its corresponding offset.
2. WHEN a List_Pattern that contains a Slice_Pattern is matched against a receiver, THE Compiler SHALL evaluate the match to true only when the receiver length is at least the number of element sub-patterns, every element sub-pattern before the Slice_Pattern matches the element at its offset from the start, and every element sub-pattern after the Slice_Pattern matches the element at its corresponding offset from the end.
3. WHERE a Slice_Pattern carries a subpattern, THE Compiler SHALL match the contiguous sub-sequence consisting of the receiver elements not consumed by the element sub-patterns before and after the Slice_Pattern against that subpattern, honoring Inclusive_Start and Exclusive_End, SHALL evaluate the overall match to true only when that subpattern also matches the sub-sequence, and SHALL bind any variables introduced by that subpattern to the corresponding sliced sub-sequence.
4. WHERE a Slice_Pattern carries no subpattern, THE Compiler SHALL match the intervening elements without producing a binding.
5. WHEN an empty List_Pattern `[]` is matched against a receiver, THE Compiler SHALL evaluate the match to true only when the receiver length is zero.

### Requirement 22: List and Slice Pattern C# Emission

**User Story:** As a Mist developer, I want list and slice patterns to transpile to native C# list patterns, so that the compiled output matches hand-written C# 11.

#### Acceptance Criteria

1. WHEN the Emitter emits a List_Pattern, THE Emitter SHALL emit the corresponding C# list pattern `[<elements>]`, preserving the order of the element sub-patterns.
2. WHEN the Emitter emits a Slice_Pattern that carries no subpattern, THE Emitter SHALL emit the C# slice pattern `..`.
3. WHEN the Emitter emits a Slice_Pattern that carries a subpattern, THE Emitter SHALL emit the C# slice pattern `.. <subpattern>` where `<subpattern>` is the C# emission of that subpattern.
4. WHEN the Emitter emits a List_Pattern or Slice_Pattern that appears as the operand of an `is` expression or the pattern of a `switch` arm, THE Emitter SHALL emit it in the corresponding C# `is` or `switch` position.

### Requirement 23: List and Slice Pattern Pretty-Printing and Round-Trip

**User Story:** As a Mist developer, I want the pretty-printer to reproduce list and slice patterns faithfully, so that formatting preserves program meaning.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a List_Pattern as `[` followed by its comma-separated element sub-patterns followed by `]`.
2. THE Pretty_Printer SHALL format a Slice_Pattern that carries no subpattern as `..` and a Slice_Pattern that carries a subpattern as `..` followed by a space and the formatted subpattern (`.. <subpattern>`).
3. FOR ALL valid Mist ASTs containing List_Patterns and Slice_Patterns, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).
