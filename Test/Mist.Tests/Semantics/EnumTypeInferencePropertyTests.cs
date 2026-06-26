// Feature: enums, Property 3: Underlying type inference correctness
// **Validates: Requirements 3.1, 3.2, 3.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Describes a generated enum scenario for underlying type inference testing.
/// </summary>
public sealed class EnumInferenceScenario
{
    public string EnumName { get; }
    public IReadOnlyList<string> MemberNames { get; }
    public InferenceCategory Category { get; }

    /// <summary>
    /// For <see cref="InferenceCategory.AllSameType"/>, the expected underlying
    /// type DisplayName (e.g., "int", "string").
    /// For <see cref="InferenceCategory.NameOnly"/>, always "byte".
    /// For <see cref="InferenceCategory.MixedTypes"/>, the expected union DisplayName.
    /// </summary>
    public string ExpectedUnderlyingDisplayName { get; }

    public EnumInferenceScenario(
        string enumName,
        IReadOnlyList<string> memberNames,
        InferenceCategory category,
        string expectedUnderlyingDisplayName)
    {
        EnumName = enumName;
        MemberNames = memberNames;
        Category = category;
        ExpectedUnderlyingDisplayName = expectedUnderlyingDisplayName;
    }

    public override string ToString() =>
        $"enum {EnumName} ({MemberNames.Count} members, category={Category}, expected={ExpectedUnderlyingDisplayName})";
}

/// <summary>
/// The category of enum inference scenario being tested.
/// </summary>
public enum InferenceCategory
{
    /// <summary>All members are name-only (no explicit values). Underlying type should be byte.</summary>
    NameOnly,

    /// <summary>All members have values of the same type. Underlying type should be that type.</summary>
    AllSameType,

    /// <summary>Members have values of different types. Underlying type should be a union.</summary>
    MixedTypes,
}

/// <summary>
/// Property-based tests for underlying type inference correctness.
///
/// **Validates: Requirements 3.1, 3.2, 3.3**
///
/// For any enum declaration without an explicit type annotation:
/// - If all member values have the same type T, the underlying type SHALL be T (Req 3.1).
/// - If member values have distinct types, the underlying type SHALL be a union of those types (Req 3.2).
/// - If no member has an explicit value (name-only), the underlying type SHALL be byte (Req 3.3).
/// </summary>
public class EnumTypeInferencePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private static ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Fruit", "Animal", "Planet", "Season", "Shape", "Flavor", "Signal"];

    private static readonly string[] MemberNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a name-only enum scenario (no values, no type annotation).
    /// Expected underlying type: byte.
    /// </summary>
    private static Gen<EnumInferenceScenario> GenNameOnlyScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 6)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               select new EnumInferenceScenario(
                   name, members, InferenceCategory.NameOnly, "byte");
    }

    /// <summary>
    /// Generates an enum scenario where all members have integer literal values.
    /// Expected underlying type: int (integer literals without suffix resolve to int).
    /// </summary>
    private static Gen<EnumInferenceScenario> GenAllIntScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 6)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               select new EnumInferenceScenario(
                   name, members, InferenceCategory.AllSameType, "int");
    }

    /// <summary>
    /// Generates an enum scenario where all members have string literal values.
    /// Expected underlying type: string.
    /// </summary>
    private static Gen<EnumInferenceScenario> GenAllStringScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 6)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               select new EnumInferenceScenario(
                   name, members, InferenceCategory.AllSameType, "string");
    }

    /// <summary>
    /// Generates an enum scenario with mixed int and string values.
    /// Expected underlying type: a union of int and string (canonical order: "int | string").
    /// </summary>
    private static Gen<EnumInferenceScenario> GenMixedIntStringScenario()
    {
        // Need at least 2 members to have mixed types.
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(2, 6)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               select new EnumInferenceScenario(
                   name, members, InferenceCategory.MixedTypes, "int | string");
    }

    private static Gen<EnumInferenceScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNameOnlyScenario(),
            GenAllIntScenario(),
            GenAllStringScenario(),
            GenMixedIntStringScenario());
    }

    public static Arbitrary<EnumInferenceScenario> ArbitraryEnumInferenceScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    private static ExpressionNode MakeIntLiteralExpression(int value)
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value.ToString()),
        };
        return MistAstGenerator.WrapPrimary(primary);
    }

    private static ExpressionNode MakeStringLiteralExpression(string value)
    {
        var primary = new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = Tok($"\"{value}\""),
        };
        return MistAstGenerator.WrapPrimary(primary);
    }

    private static EnumMemberNode MakeEnumMember(string name, ExpressionNode? value = null) =>
        new()
        {
            Name = Tok(name),
            Value = value,
        };

    private static EnumMemberListNode MakeEnumMemberList(IReadOnlyList<EnumMemberNode> members)
    {
        var first = members[0];
        var rest = members.Skip(1).ToImmutableArray();
        return new EnumMemberListNode
        {
            EnumMember = first,
            EnumMembers = rest,
        };
    }

    private static CompilationUnitNode BuildAst(EnumInferenceScenario scenario)
    {
        var members = new List<EnumMemberNode>();

        switch(scenario.Category)
        {
            case InferenceCategory.NameOnly:
                // All members are name-only (no values).
                foreach(var memberName in scenario.MemberNames)
                {
                    members.Add(MakeEnumMember(memberName));
                }
                break;

            case InferenceCategory.AllSameType:
                // All members have values of the same type.
                for(var i = 0; i < scenario.MemberNames.Count; i++)
                {
                    ExpressionNode value = scenario.ExpectedUnderlyingDisplayName switch
                    {
                        "int" => MakeIntLiteralExpression(i * 10),
                        "string" => MakeStringLiteralExpression($"val{i}"),
                        _ => MakeIntLiteralExpression(i),
                    };
                    members.Add(MakeEnumMember(scenario.MemberNames[i], value));
                }
                break;

            case InferenceCategory.MixedTypes:
                // First member gets an int value, second gets a string value,
                // remaining alternate between int and string.
                for(var i = 0; i < scenario.MemberNames.Count; i++)
                {
                    ExpressionNode value = i % 2 == 0
                        ? MakeIntLiteralExpression(i * 10)
                        : MakeStringLiteralExpression($"val{i}");
                    members.Add(MakeEnumMember(scenario.MemberNames[i], value));
                }
                break;
        }

        var memberList = MakeEnumMemberList(members);

        // No type annotation — inference should determine the underlying type.
        var enumDecl = new EnumDeclarationNode
        {
            Name = Tok(scenario.EnumName),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            TypeAnnotation = null,
            Members = memberList,
        };

        var typeDecl = new EnumTypeTypeDeclarationNode
        {
            EnumDeclaration = enumDecl,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(typeDecl).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Underlying type inference correctness
    //
    // **Validates: Requirements 3.1, 3.2, 3.3**
    //
    // For any enum declaration without an explicit type annotation:
    // - If all member values have the same type T, the underlying type
    //   SHALL be T (Requirement 3.1).
    // - If member values have distinct types, the underlying type SHALL be
    //   a union of those types (Requirement 3.2).
    // - If no member has an explicit value (name-only), the underlying type
    //   SHALL be byte (Requirement 3.3).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumTypeInferencePropertyTests) },
        MaxTest = 100)]
    public Property UnderlyingType_IsInferredCorrectly_BasedOnMemberValueTypes(
        EnumInferenceScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Find the enum symbol in the semantic model.
        var enumSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.EnumName && s.Kind == SymbolKind.Enum);

        if(enumSymbol is null)
        {
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' not found in semantic model. " +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        if(enumSymbol.DeclaredType is not EnumType enumType)
        {
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' DeclaredType is not EnumType, got: {enumSymbol.DeclaredType?.GetType().Name ?? "null"}");
        }

        if(enumType.UnderlyingType is null)
        {
            return false.ToProperty()
                .Label($"UnderlyingType is null for enum '{scenario.EnumName}'");
        }

        // Verify the underlying type matches the expected type based on category.
        var actualDisplayName = enumType.UnderlyingType.DisplayName;

        if(scenario.Category == InferenceCategory.MixedTypes)
        {
            // For mixed types, the underlying type should be a UnionType.
            if(enumType.UnderlyingType is not UnionType)
            {
                return false.ToProperty()
                    .Label($"Expected UnionType for mixed-type enum, got {enumType.UnderlyingType.GetType().Name} " +
                           $"with DisplayName '{actualDisplayName}'. Scenario: {scenario}");
            }
        }

        if(actualDisplayName != scenario.ExpectedUnderlyingDisplayName)
        {
            return false.ToProperty()
                .Label($"Expected underlying type '{scenario.ExpectedUnderlyingDisplayName}', " +
                       $"got '{actualDisplayName}'. Category: {scenario.Category}. Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' correctly inferred underlying type '{actualDisplayName}' " +
                   $"for category {scenario.Category}");
    }
}