using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests for default method implementation parsing inside abstract types
/// (Phase 3, task 3.1).
///
/// Validates that:
/// <list type="bullet">
///   <item>A method WITHOUT a body (semicolon-terminated) is classified as a slot
///   (<see cref="AbstractMethodMemberAbstractMemberDeclarationNode"/>).</item>
///   <item>A method WITH a braced body is classified as a default implementation
///   (<see cref="AbstractDefaultMethodMemberAbstractMemberDeclarationNode"/>).</item>
///   <item>Slots and defaults may be freely mixed in any order.</item>
///   <item>A property with a body inside an abstract type produces the
///   <c>MIST_ABSTRACT_PROPERTY_DEFAULT</c> diagnostic.</item>
/// </list>
///
/// **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5**
/// </summary>
public class AbstractDefaultMethodParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static (CompilationUnitNode? Cu, ImmutableArray<Diagnostic> Diagnostics) Parse(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);
        return (result.Root as CompilationUnitNode, bag.ToImmutableArray());
    }

    private static AbstractDeclarationNode ParseAbstract(string source)
    {
        var (cu, diagnostics) = Parse(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}");
        Assert.NotNull(cu);
        var types = cu!.GetTypes();
        Assert.NotEmpty(types);
        var abstractType = Assert.IsType<AbstractTypeTypeDeclarationNode>(types[0]);
        return abstractType.AbstractDeclaration;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Slot classification (no body) — Requirement 13.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodWithoutBody_IsClassifiedAsSlot()
    {
        var abstractDecl = ParseAbstract("abstract IGreeting { string Name(); }");

        var member = Assert.Single(abstractDecl.Members);
        var slot = Assert.IsType<AbstractMethodMemberAbstractMemberDeclarationNode>(member);
        Assert.Equal("Name", slot.Method.Name.Lexeme);
    }

    [Fact]
    public void SlotWithParameters_IsClassifiedAsSlot()
    {
        var abstractDecl = ParseAbstract("abstract ICalc { int Add(int a, int b); }");

        var member = Assert.Single(abstractDecl.Members);
        Assert.IsType<AbstractMethodMemberAbstractMemberDeclarationNode>(member);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Default classification (braced body) — Requirements 13.1, 13.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodWithBody_IsClassifiedAsDefault()
    {
        var abstractDecl = ParseAbstract(
            "abstract IGreeting { string Greet() { return \"hi\"; } }");

        var member = Assert.Single(abstractDecl.Members);
        var def = Assert.IsType<AbstractDefaultMethodMemberAbstractMemberDeclarationNode>(member);
        Assert.Equal("Greet", def.DefaultMethod.Name.Lexeme);
        // The captured method retains its block body.
        Assert.IsType<MethodBlockBodyMethodBodyNode>(def.DefaultMethod.Body);
    }

    [Fact]
    public void DefaultMethodWithEmptyBody_IsClassifiedAsDefault()
    {
        var abstractDecl = ParseAbstract("abstract IFoo { void Noop() { } }");

        var member = Assert.Single(abstractDecl.Members);
        Assert.IsType<AbstractDefaultMethodMemberAbstractMemberDeclarationNode>(member);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mixed slots and defaults in any order — Requirement 13.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SlotsAndDefaults_MixedInAnyOrder_ClassifiedIndividually()
    {
        var source = """
            abstract IMixed
            {
                string Name();
                string Greet() { return "hi"; }
                int Count();
                bool IsReady() { return true; }
            }
            """;

        var abstractDecl = ParseAbstract(source);

        Assert.Equal(4, abstractDecl.Members.Length);
        Assert.IsType<AbstractMethodMemberAbstractMemberDeclarationNode>(abstractDecl.Members[0]);
        Assert.IsType<AbstractDefaultMethodMemberAbstractMemberDeclarationNode>(abstractDecl.Members[1]);
        Assert.IsType<AbstractMethodMemberAbstractMemberDeclarationNode>(abstractDecl.Members[2]);
        Assert.IsType<AbstractDefaultMethodMemberAbstractMemberDeclarationNode>(abstractDecl.Members[3]);
    }

    [Fact]
    public void PropertySlotAlongsideDefaultMethod_BothParse()
    {
        var source = """
            abstract IHasProp
            {
                int Value { get; set; }
                string Describe() { return "x"; }
            }
            """;

        var abstractDecl = ParseAbstract(source);

        Assert.Equal(2, abstractDecl.Members.Length);
        Assert.IsType<AbstractPropertyMemberAbstractMemberDeclarationNode>(abstractDecl.Members[0]);
        Assert.IsType<AbstractDefaultMethodMemberAbstractMemberDeclarationNode>(abstractDecl.Members[1]);
    }

    [Fact]
    public void DefaultMethodBodyContainingLambda_DoesNotTriggerPropertyError()
    {
        // A lambda's `=>` lives inside the method body (deeper brace depth) and must
        // not be mistaken for an expression-bodied property at the member level.
        var source = """
            abstract IFoo
            {
                int Compute() { var f = (x) => x + 1; return f(2); }
            }
            """;

        var (_, diagnostics) = Parse(source);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_ABSTRACT_PROPERTY_DEFAULT");
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property-with-body diagnostic — Requirement 13.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropertyWithBlockAccessorBody_EmitsDiagnostic()
    {
        var source = "abstract IFoo { int Value { get { return 1; } } }";

        var (_, diagnostics) = Parse(source);

        var diag = Assert.Single(diagnostics,
            d => d.Id.Value == "MIST_ABSTRACT_PROPERTY_DEFAULT");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(
            "Property slots cannot have default implementations. Only methods can have defaults.",
            diag.Message);
    }

    [Fact]
    public void PropertyWithExpressionAccessorBody_EmitsDiagnostic()
    {
        var source = "abstract IFoo { int Value { get => 1; } }";

        var (_, diagnostics) = Parse(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_ABSTRACT_PROPERTY_DEFAULT");
    }

    [Fact]
    public void ExpressionBodiedProperty_EmitsDiagnostic()
    {
        var source = "abstract IFoo { int Value => 1; }";

        var (_, diagnostics) = Parse(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_ABSTRACT_PROPERTY_DEFAULT");
    }

    [Fact]
    public void ValidPropertySlot_DoesNotEmitDiagnostic()
    {
        var source = "abstract IFoo { int Value { get; set; } }";

        var (_, diagnostics) = Parse(source);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_ABSTRACT_PROPERTY_DEFAULT");
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NestedClassPropertyWithBody_InsideAbstract_IsNotFlagged()
    {
        // A property with a body is legitimate on a nested class — only DIRECT
        // members of the abstract body are subject to the no-default rule.
        var source = """
            abstract IFoo
            {
                string Name();
                class Helper
                {
                    int X { get { return 1; } }
                }
            }
            """;

        var (_, diagnostics) = Parse(source);

        Assert.DoesNotContain(diagnostics,
            d => d.Id.Value == "MIST_ABSTRACT_PROPERTY_DEFAULT");
    }

    [Fact]
    public void NestedAbstractPropertyWithBody_IsFlagged()
    {
        // A property-with-body directly inside a NESTED abstract type is still
        // subject to the rule and reported.
        var source = """
            abstract IOuter
            {
                abstract IInner
                {
                    int Value { get => 1; }
                }
            }
            """;

        var (_, diagnostics) = Parse(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_ABSTRACT_PROPERTY_DEFAULT");
    }
}