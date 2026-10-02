// Feature: unknown-type (Phase B, Task 13) — Enforce inheritance prohibition.
//
// Validates Requirements 6.1, 6.2, 6.3, 6.4, 16.6 — a class, struct,
// or abstract type declaration that would name `unknown` as a base
// or implemented type is rejected.
//
// Reachability note. The grammar makes the positive form of each case
// unreachable from parsed source:
//
//   * `BaseTypeRef = name:IDENTIFIER typeArgs:TypeArgumentList?` — the
//     name position is a bare `IDENTIFIER` token, so `unknown` (which
//     lexes to the reserved `UNKNOWN` keyword token) cannot appear
//     there. This covers the class base clause, the struct abstract type
//     list, and the abstract type base list.
//
// The tests below assert the observable parse behavior — each source
// fails to produce a type declaration — exactly the pattern established
// by `UnknownTypeParserTests.Rejects_*`.

using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for the inheritance prohibition: <c>unknown</c> used as a
/// base or implemented type on a class, struct, or abstract type
/// declaration (Requirements 6.1–6.4, 16.6).
/// </summary>
/// <remarks>
/// <para>
/// Every case is <i>unreachable from parsed source</i> because the
/// grammar rejects the form.
/// These tests therefore assert the parse-time behavior — mirroring the
/// negative pattern in <see cref="Parsing.UnknownTypeParserTests"/>
/// (see <c>Rejects_Class_Named_Unknown</c> and friends). Each invalid
/// source is paired with the analogous valid source (substituting a
/// legal identifier for <c>unknown</c>) so the test documents the
/// observable difference between "parsed successfully" and "parser
/// rejected because <c>unknown</c> is a reserved keyword token."
/// </para>
/// </remarks>
[Trait("Tier", "3")]
public class UnknownInheritanceProhibitionTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Parse helpers ──────────────────────────────────────────────────────

    private static (CompilationUnitNode? Root, bool HasErrors) ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var hasErrors = bag.ToImmutableArray()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

        return (result.Root as CompilationUnitNode, hasErrors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // `class Foo : unknown { }` — Requirement 6.1.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassBaseClauseWithUnknown_FailsToParse()
    {
        // Valid analog: `class Foo : Bar { }` — produces a ClassDeclaration
        // with `BaseList.First.Name.Lexeme == "Bar"`. If `unknown` were a
        // normal identifier, `class Foo : unknown { }` would parse to an
        // equivalent AST with `baseClassName == "unknown"`. Because
        // `unknown` lexes to the reserved `UNKNOWN` token, the
        // `BaseTypeRef = name:IDENTIFIER ...` rule fails to match, the
        // optional `baseClause` is discarded, and the enclosing
        // ClassDeclaration fails (no error recovery in v1).
        var (cuInvalid, _) = ParseSource("class Foo : unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        // Sanity check: the analogous source with a legal identifier
        // parses to a single BodyTypeTypeDeclarationNode.
        var (cuValid, _) = ParseSource("class Bar { } class Foo : Bar { }");
        Assert.NotNull(cuValid);
        Assert.Equal(2, cuValid!.GetTypes().Length);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // `struct S : unknown { }` — Requirement 6.2.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StructAbstractListWithUnknown_FailsToParse()
    {
        // Valid analog: `abstract I { } struct S : I { }` — a struct
        // with an abstract type list of one abstract type. If `unknown` could
        // be an identifier, `struct S : unknown { }` would parse to an
        // equivalent shape.
        var (cuInvalid, _) = ParseSource("struct S : unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("abstract I { } struct S : I { }");
        Assert.NotNull(cuValid);
        Assert.Equal(2, cuValid!.GetTypes().Length);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // `abstract I : unknown { }` — Requirement 6.3.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractBaseListWithUnknown_FailsToParse()
    {
        // Valid analog: `abstract J { } abstract I : J { }` — an
        // abstract type whose base list has one entry.
        var (cuInvalid, _) = ParseSource("abstract I : unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("abstract J { } abstract I : J { }");
        Assert.NotNull(cuValid);
        Assert.Equal(2, cuValid!.GetTypes().Length);
    }
}