// Feature: enums, Property 2: Enum type construction invariants
// **Validates: Requirements 2.1, 2.2, 2.3**

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
/// Describes a generated enum declaration scenario for property testing.
/// Carries the enum name, member names, whether it has a type annotation,
/// and whether members have values.
/// </summary>
public sealed class EnumConstructionScenario
{
    public string EnumName { get; }
    public IReadOnlyList<string> MemberNames { get; }
    public bool HasTypeAnnotation { get; }
    public bool HasMemberValues { get; }

    public EnumConstructionScenario(
        string enumName,
        IReadOnlyList<string> memberNames,
        bool hasTypeAnnotation,
        bool hasMemberValues)
    {
        EnumName = enumName;
        MemberNames = memberNames;
        HasTypeAnnotation = hasTypeAnnotation;
        HasMemberValues = hasMemberValues;
    }

    public override string ToString()
    {
        return $"enum {EnumName} ({MemberNames.Count} members, annotation={HasTypeAnnotation}, values={HasMemberValues})";
    }
}

/// <summary>
/// Property-based tests for enum type construction invariants.
///
/// **Validates: Requirements 2.1, 2.2, 2.3**
///
/// For any valid enum declaration, the semantic model SHALL produce an EnumType with
/// Kind == TypeKind.Enum, a non-null UnderlyingType, and a Members list whose length
/// equals the number of declared members.
/// </summary>
public class EnumTypeConstructionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

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
        ["Direction", "Color", "Status", "Priority", "Level", "Mode", "State"];

    private static readonly string[] MemberNamePool =
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending", "High", "Low", "Medium"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a name-only enum scenario (no values, no type annotation).
    /// </summary>
    private static Gen<EnumConstructionScenario> GenNameOnlyScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 5)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               select new EnumConstructionScenario(name, members, hasTypeAnnotation: false, hasMemberValues: false);
    }

    /// <summary>
    /// Generates an enum scenario with integer values and optional type annotation.
    /// </summary>
    private static Gen<EnumConstructionScenario> GenIntValuedScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 5)
               from members in Gen.Shuffle(MemberNamePool)
                   .Select(m => m.Take(memberCount).ToArray())
               from hasAnnotation in Gen.Elements(true, false)
               select new EnumConstructionScenario(name, members, hasAnnotation, hasMemberValues: true);
    }

    /// <summary>
    /// Generates an empty enum scenario (no members).
    /// </summary>
    private static Gen<EnumConstructionScenario> GenEmptyScenario()
    {
        return from name in Gen.Elements(EnumNames)
               from hasAnnotation in Gen.Elements(true, false)
               select new EnumConstructionScenario(name, Array.Empty<string>(), hasAnnotation, hasMemberValues: false);
    }

    private static Gen<EnumConstructionScenario> GenScenario()
    {
        return Gen.OneOf(GenNameOnlyScenario(), GenIntValuedScenario(), GenEmptyScenario());
    }

    public static Arbitrary<EnumConstructionScenario> ArbitraryEnumConstructionScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static ExpressionNode MakeIntLiteralExpression(string value)
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value),
        };
        return MistAstGenerator.WrapPrimary(primary);
    }

    private static TypeReferenceNode MakeIntTypeRef()
    {
        return new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            }
        };
    }

    private static EnumMemberNode MakeEnumMember(string name, ExpressionNode? value = null)
    {
        return new()
        {
            Name = Tok(name),
            Value = value,
        };
    }

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

    private static CompilationUnitNode BuildAst(EnumConstructionScenario scenario)
    {
        EnumMemberListNode? memberList = null;

        if(scenario.MemberNames.Count > 0)
        {
            var members = new List<EnumMemberNode>();
            for(var i = 0; i < scenario.MemberNames.Count; i++)
            {
                var value = scenario.HasMemberValues
                    ? MakeIntLiteralExpression(i.ToString())
                    : null;
                members.Add(MakeEnumMember(scenario.MemberNames[i], value));
            }

            memberList = MakeEnumMemberList(members);
        }

        var enumDecl = new EnumDeclarationNode
        {
            Name = Tok(scenario.EnumName),
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            TypeAnnotation = scenario.HasTypeAnnotation ? MakeIntTypeRef() : null,
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
    // Property 2: Enum type construction invariants
    //
    // **Validates: Requirements 2.1, 2.2, 2.3**
    //
    // For any valid enum declaration, the semantic model SHALL produce an
    // EnumType with Kind == TypeKind.Enum, a non-null UnderlyingType, and a
    // Members list whose length equals the number of declared members.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumTypeConstructionPropertyTests) },
        MaxTest = 100)]
    public Property EnumType_HasCorrectKind_NonNullUnderlyingType_AndMatchingMemberCount(
        EnumConstructionScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Find the enum symbol in the semantic model.
        var enumSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.EnumName && s.Kind == SymbolKind.Enum);

        if(enumSymbol is null)
        {
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' not found with SymbolKind.Enum. " +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        // Requirement 2.1: EnumType implements IType
        if(enumSymbol.DeclaredType is not EnumType enumType)
        {
            return false.ToProperty()
                .Label($"Enum '{scenario.EnumName}' DeclaredType is not EnumType, got: {enumSymbol.DeclaredType?.GetType().Name ?? "null"}");
        }

        // Requirement 2.3: Kind == TypeKind.Enum
        if(enumType.Kind != TypeKind.Enum)
        {
            return false.ToProperty()
                .Label($"Expected TypeKind.Enum, got {enumType.Kind}");
        }

        // Requirement 2.2: Non-null UnderlyingType
        if(enumType.UnderlyingType is null)
        {
            return false.ToProperty()
                .Label($"UnderlyingType is null for enum '{scenario.EnumName}'");
        }

        // Requirement 2.2: Members.Count equals declared member count
        if(enumType.Members.Count != scenario.MemberNames.Count)
        {
            return false.ToProperty()
                .Label($"Expected {scenario.MemberNames.Count} members, got {enumType.Members.Count}. " +
                       $"Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' correctly constructed with {scenario.MemberNames.Count} members");
    }
}