using System.Collections.Generic;

namespace Storm.SourceGenerator.Frontend;

// -----------------------------------------------------------------------
//  Top-level file
// -----------------------------------------------------------------------

/// <summary>
/// Represents a complete Storm grammar file: an optional grammar declaration,
/// a sequence of imports, followed by a sequence of declarations.
/// </summary>
public sealed record StormFile(
    StormGrammarDecl? GrammarDeclaration,
    IReadOnlyList<StormImport> Imports,
    IReadOnlyList<StormDeclaration> Declarations);

// -----------------------------------------------------------------------
//  Grammar declaration
// -----------------------------------------------------------------------

/// <summary>
/// A <c>grammar Name;</c> declaration that identifies the root grammar file
/// and provides the grammar name.
/// </summary>
public sealed record StormGrammarDecl(
    string Name,
    int Offset,
    int Line,
    int Column);

// -----------------------------------------------------------------------
//  Imports
// -----------------------------------------------------------------------

/// <summary>
/// An <c>import "path";</c> or <c>import "path" as Namespace;</c> declaration.
/// </summary>
public sealed record StormImport(
    string Path,
    string? Alias,
    int Offset,
    int Line,
    int Column);

// -----------------------------------------------------------------------
//  Declarations
// -----------------------------------------------------------------------

/// <summary>
/// Base type for all top-level Storm declarations
/// (<c>rule</c>, <c>token</c>, <c>fragment</c>, <c>hidden token</c>).
/// </summary>
public abstract record StormDeclaration(
    string Name,
    StormExpression Body,
    int Offset,
    int Line,
    int Column);

/// <summary>A <c>rule Name = ...;</c> declaration, optionally annotated with <c>@memo</c>.</summary>
public sealed record StormRuleDecl(
    string Name,
    StormExpression Body,
    int Offset,
    int Line,
    int Column,
    bool IsMemo = false)
    : StormDeclaration(Name, Body, Offset, Line, Column);

/// <summary>A <c>token Name = ...;</c> declaration.</summary>
public sealed record StormTokenDecl(
    string Name,
    StormExpression Body,
    int Offset,
    int Line,
    int Column,
    StormModeAction? Action = null)
    : StormDeclaration(Name, Body, Offset, Line, Column);

/// <summary>A <c>fragment Name = ...;</c> declaration.</summary>
public sealed record StormFragmentDecl(
    string Name,
    StormExpression Body,
    int Offset,
    int Line,
    int Column)
    : StormDeclaration(Name, Body, Offset, Line, Column);

/// <summary>A <c>hidden token Name = ...;</c> declaration.</summary>
public sealed record StormHiddenTokenDecl(
    string Name,
    StormExpression Body,
    int Offset,
    int Line,
    int Column,
    StormModeAction? Action = null)
    : StormDeclaration(Name, Body, Offset, Line, Column);

// -----------------------------------------------------------------------
//  Mode actions
// -----------------------------------------------------------------------

/// <summary>
/// Base type for mode transition actions on token declarations (<c>=> push</c> / <c>=> pop</c>).
/// </summary>
public abstract record StormModeAction;

/// <summary>A push action: <c>=> push ModeName</c></summary>
public sealed record StormPushAction(string ModeName) : StormModeAction;

/// <summary>A pop action: <c>=> pop</c></summary>
public sealed record StormPopAction() : StormModeAction;

// -----------------------------------------------------------------------
//  Mode declarations
// -----------------------------------------------------------------------

/// <summary>
/// A mode block declaration: <c>mode ModeName { declarations }</c>.
/// The <see cref="StormDeclaration.Body"/> is set to a placeholder
/// <see cref="StormReferenceExpr"/> since mode declarations don't have a body expression.
/// </summary>
public sealed record StormModeDecl(
    string Name,
    IReadOnlyList<StormDeclaration> Declarations,
    int Offset,
    int Line,
    int Column)
    : StormDeclaration(Name, new StormReferenceExpr(Name), Offset, Line, Column);

// -----------------------------------------------------------------------
//  Expressions
// -----------------------------------------------------------------------

/// <summary>
/// Base type for all Storm expression nodes used in rule/token/fragment bodies.
/// </summary>
public abstract record StormExpression;

/// <summary>
/// A sequence of two or more expressions (juxtaposition).
/// <c>A B C</c> → <c>StormSequenceExpr([A, B, C])</c>
/// </summary>
public sealed record StormSequenceExpr(
    IReadOnlyList<StormExpression> Elements)
    : StormExpression;

/// <summary>
/// An alternation of two or more expressions.
/// <c>A | B | C</c> → <c>StormAlternationExpr([A, B, C])</c>
/// </summary>
public sealed record StormAlternationExpr(
    IReadOnlyList<StormExpression> Alternatives)
    : StormExpression;

/// <summary>
/// An optional expression: <c>E?</c>
/// </summary>
public sealed record StormOptionalExpr(
    StormExpression Inner)
    : StormExpression;

/// <summary>
/// A zero-or-more repetition: <c>E*</c>
/// </summary>
public sealed record StormZeroOrMoreExpr(
    StormExpression Inner)
    : StormExpression;

/// <summary>
/// A one-or-more repetition: <c>E+</c>
/// </summary>
public sealed record StormOneOrMoreExpr(
    StormExpression Inner)
    : StormExpression;

/// <summary>
/// A parenthesized group: <c>( E )</c>
/// </summary>
public sealed record StormGroupExpr(
    StormExpression Inner)
    : StormExpression;

/// <summary>
/// A double-quoted string literal: <c>"fn"</c>
/// </summary>
public sealed record StormStringLiteralExpr(
    string Value)
    : StormExpression;

/// <summary>
/// A character class: <c>[a-zA-Z_]</c>, <c>[^"\\]</c>, etc.
/// The <see cref="Text"/> property holds the raw bracket-delimited text.
/// </summary>
public sealed record StormCharClassExpr(
    string Text)
    : StormExpression;

/// <summary>
/// A backtick-delimited regex literal: <c>`[a-zA-Z]+`</c>
/// </summary>
public sealed record StormRegexLiteralExpr(
    string Pattern)
    : StormExpression;

/// <summary>
/// A simple (unqualified) reference to a rule, token, or fragment: <c>Identifier</c>
/// </summary>
public sealed record StormReferenceExpr(
    string Name)
    : StormExpression;

/// <summary>
/// A qualified reference: <c>Namespace.Name</c>
/// </summary>
public sealed record StormQualifiedReferenceExpr(
    string Qualifier,
    string Name)
    : StormExpression;

/// <summary>
/// A field-labeled expression: <c>label:Element</c>
/// </summary>
public sealed record StormLabeledExpr(
    string Label,
    StormExpression Element)
    : StormExpression;

/// <summary>
/// A named alternative: an expression followed by <c>as Name</c>.
/// Used within rule alternations to name AST subtypes.
/// </summary>
public sealed record StormNamedAlternative(
    StormExpression Expression,
    string AlternativeName)
    : StormExpression;
