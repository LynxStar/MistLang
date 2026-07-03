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
///   (<see cref="MethodMemberMemberDeclarationNode"/> with <see cref="MethodNoBodyMethodBodyNode"/>).</item>
///   <item>A method WITH a braced body is classified as a default implementation
///   (<see cref="MethodMemberMemberDeclarationNode"/> with <see cref="MethodBlockBodyMethodBodyNode"/>).</item>
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
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);
        return (result.Root as CompilationUnitNode, bag.ToImmutableArray());
    }

    private static TypeBodyDeclarationNode ParseAbstract(string source)
    {
        var (cu, diagnostics) = Parse(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}");
        Assert.NotNull(cu);
        var types = cu!.GetTypes();
        Assert.NotEmpty(types);
        var bodyType = Assert.IsType<BodyTypeTypeDeclarationNode>(types[0]);
        _ = Assert.IsType<AbstractKeywordTypeKeywordNode>(bodyType.TypeBody.Keyword);
        return bodyType.TypeBody;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Slot classification (no body) — Requirement 13.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodWithoutBody_IsClassifiedAsSlot()
    {
        var typeBody = ParseAbstract("abstract IGreeting { string Name(); }");

        var member = Assert.Single(typeBody.Members);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(member);
        _ = Assert.IsType<MethodNoBodyMethodBodyNode>(methodMember.Method.Body);
        Assert.Equal("Name", methodMember.Method.Name.Lexeme);
    }

    [Fact]
    public void SlotWithParameters_IsClassifiedAsSlot()
    {
        var typeBody = ParseAbstract("abstract ICalc { int Add(int a, int b); }");

        var member = Assert.Single(typeBody.Members);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(member);
        _ = Assert.IsType<MethodNoBodyMethodBodyNode>(methodMember.Method.Body);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Default classification (braced body) — Requirements 13.1, 13.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodWithBody_IsClassifiedAsDefault()
    {
        var typeBody = ParseAbstract(
            "abstract IGreeting { string Greet() { return \"hi\"; } }");

        var member = Assert.Single(typeBody.Members);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(member);
        Assert.Equal("Greet", methodMember.Method.Name.Lexeme);
        // The captured method retains its block body.
        _ = Assert.IsType<MethodBlockBodyMethodBodyNode>(methodMember.Method.Body);
    }

    [Fact]
    public void DefaultMethodWithEmptyBody_IsClassifiedAsDefault()
    {
        var typeBody = ParseAbstract("abstract IFoo { void Noop() { } }");

        var member = Assert.Single(typeBody.Members);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(member);
        _ = Assert.IsType<MethodBlockBodyMethodBodyNode>(methodMember.Method.Body);
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

        var typeBody = ParseAbstract(source);

        Assert.Equal(4, typeBody.Members.Length);

        var member0 = Assert.IsType<MethodMemberMemberDeclarationNode>(typeBody.Members[0]);
        _ = Assert.IsType<MethodNoBodyMethodBodyNode>(member0.Method.Body);

        var member1 = Assert.IsType<MethodMemberMemberDeclarationNode>(typeBody.Members[1]);
        _ = Assert.IsType<MethodBlockBodyMethodBodyNode>(member1.Method.Body);

        var member2 = Assert.IsType<MethodMemberMemberDeclarationNode>(typeBody.Members[2]);
        _ = Assert.IsType<MethodNoBodyMethodBodyNode>(member2.Method.Body);

        var member3 = Assert.IsType<MethodMemberMemberDeclarationNode>(typeBody.Members[3]);
        _ = Assert.IsType<MethodBlockBodyMethodBodyNode>(member3.Method.Body);
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

        var typeBody = ParseAbstract(source);

        Assert.Equal(2, typeBody.Members.Length);
        _ = Assert.IsType<PropertyMemberMemberDeclarationNode>(typeBody.Members[0]);

        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeBody.Members[1]);
        _ = Assert.IsType<MethodBlockBodyMethodBodyNode>(methodMember.Method.Body);
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