// Feature: associated-types, Property 2: Parser disambiguation between abstract slots and concrete aliases
// **Validates: Requirements 1.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario for disambiguation between associated type slots
/// (using ':') and concrete type aliases (using '=').
/// </summary>
public sealed class DisambiguationTypeScenario
{
    /// <summary>The type name used in both forms.</summary>
    public string TypeName { get; }

    /// <summary>The constraint used in the ':' form (e.g., "IFoo").</summary>
    public string Constraint { get; }

    /// <summary>The type expression used in the '=' form (e.g., "int").</summary>
    public string TypeExpr { get; }

    public DisambiguationTypeScenario(string typeName, string constraint, string typeExpr)
    {
        TypeName = typeName;
        Constraint = constraint;
        TypeExpr = typeExpr;
    }

    public override string ToString() =>
        $"TypeName={TypeName}, Constraint={Constraint}, TypeExpr={TypeExpr}";
}

/// <summary>
/// Property-based tests for parser disambiguation between abstract type slots
/// and concrete type aliases.
///
/// Property 2: For any valid type name and constraint list, a declaration
/// <c>type Name : ConstraintList;</c> in an abstract body SHALL parse as an
/// <c>AssociatedTypeSlotDeclarationNode</c>, while a declaration
/// <c>type Name = TypeExpr;</c> in the same position (top-level, since type aliases
/// are top-level items) SHALL parse as a <c>TypeAliasDeclarationNode</c>.
/// The two forms SHALL never produce the same node type.
/// </summary>
public class AssociatedTypeDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Generator ──────────────────────────────────────────────────────────

    private static readonly string[] ValidTypeNames =
        ["Enumerator", "Element", "Output", "Key", "Value", "Item", "Result", "State", "Node", "Entry"];

    private static readonly string[] ValidConstraints =
        ["IFoo", "IBar", "IBaz", "IComparable", "IDisposable", "ICloneable", "ISerializable", "IEquatable", "IEnumerable", "ICollection"];

    private static readonly string[] ValidTypeExprs =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    public static Arbitrary<DisambiguationTypeScenario> ArbitraryScenario()
    {
        var gen =
            from nameIdx in Gen.Choose(0, ValidTypeNames.Length - 1)
            from constraintIdx in Gen.Choose(0, ValidConstraints.Length - 1)
            from typeExprIdx in Gen.Choose(0, ValidTypeExprs.Length - 1)
            select new DisambiguationTypeScenario(
                ValidTypeNames[nameIdx],
                ValidConstraints[constraintIdx],
                ValidTypeExprs[typeExprIdx]);

        return gen.ToArbitrary();
    }

    // ─── Parsing helpers ────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            return null;

        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Builds source for an associated type slot declaration inside an abstract body.
    /// </summary>
    private static string BuildAssociatedTypeSlotSource(string name, string constraint)
    {
        return $"abstract ITest {{ type {name} : {constraint}; }}";
    }

    /// <summary>
    /// Builds source for a concrete type alias declaration (top-level).
    /// </summary>
    private static string BuildTypeAliasSource(string name, string typeExpr)
    {
        return $"type {name} = {typeExpr};";
    }

    /// <summary>
    /// Extracts the first abstract member declaration from a parsed abstract.
    /// </summary>
    private static AbstractMemberDeclarationNode? ExtractAbstractMember(CompilationUnitNode cu)
    {
        var typeItem = cu.Items.FirstOrDefault() as TypeItemTopLevelItemNode;
        if (typeItem is null) return null;

        var abstractDecl = typeItem.TypeDeclaration as AbstractTypeTypeDeclarationNode;
        if (abstractDecl is null) return null;

        return abstractDecl.AbstractDeclaration.Members.FirstOrDefault();
    }

    /// <summary>
    /// Extracts the TypeAliasDeclarationNode from a parsed top-level type alias.
    /// </summary>
    private static TypeAliasDeclarationNode? ExtractTypeAlias(CompilationUnitNode cu)
    {
        var item = cu.Items.FirstOrDefault() as TypeAliasItemTopLevelItemNode;
        return item?.TypeAliasDeclaration;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2a: type Name : Constraint; in abstract produces
    //              AssociatedTypeSlotDeclarationNode
    //
    // For any valid type name and constraint, parsing `type Name : Constraint;`
    // inside an abstract body SHALL produce an
    // AbstractAssociatedTypeMemberAbstractMemberDeclarationNode containing
    // an AssociatedTypeSlotDeclarationNode.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AssociatedTypeDisambiguationPropertyTests) })]
    public Property AssociatedTypeSlot_ProducesAssociatedTypeSlotDeclarationNode(DisambiguationTypeScenario scenario)
    {
        var source = BuildAssociatedTypeSlotSource(scenario.TypeName, scenario.Constraint);
        var root = ParseSource(source);

        if (root is null)
            return false.ToProperty().Label($"Failed to parse: {source}");

        var member = ExtractAbstractMember(root);
        if (member is null)
            return false.ToProperty().Label($"Could not extract abstract member from: {source}");

        var isAssociatedTypeSlot = member is AbstractAssociatedTypeMemberAbstractMemberDeclarationNode;

        return isAssociatedTypeSlot
            .ToProperty()
            .Label($"Expected AbstractAssociatedTypeMemberAbstractMemberDeclarationNode but got {member.GetType().Name} for: {source}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2b: type Name = TypeExpr; produces TypeAliasDeclarationNode
    //
    // For any valid type name and type expression, parsing `type Name = TypeExpr;`
    // SHALL produce a TypeAliasDeclarationNode (via TypeAliasItemTopLevelItemNode).
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AssociatedTypeDisambiguationPropertyTests) })]
    public Property TypeAlias_ProducesTypeAliasDeclarationNode(DisambiguationTypeScenario scenario)
    {
        var source = BuildTypeAliasSource(scenario.TypeName, scenario.TypeExpr);
        var root = ParseSource(source);

        if (root is null)
            return false.ToProperty().Label($"Failed to parse: {source}");

        var aliasDecl = ExtractTypeAlias(root);

        return (aliasDecl is not null)
            .ToProperty()
            .Label($"Expected TypeAliasDeclarationNode but did not find one in: {source}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2c: The two forms never produce the same node type
    //
    // For any valid type name, parsing `type Name : Constraint;` in an abstract
    // and `type Name = TypeExpr;` at the top level SHALL produce different
    // underlying declaration node types (AssociatedTypeSlotDeclarationNode vs
    // TypeAliasDeclarationNode).
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AssociatedTypeDisambiguationPropertyTests) })]
    public Property SlotAndAlias_ProduceDifferentNodeTypes(DisambiguationTypeScenario scenario)
    {
        var slotSource = BuildAssociatedTypeSlotSource(scenario.TypeName, scenario.Constraint);
        var aliasSource = BuildTypeAliasSource(scenario.TypeName, scenario.TypeExpr);

        var slotRoot = ParseSource(slotSource);
        var aliasRoot = ParseSource(aliasSource);

        if (slotRoot is null)
            return false.ToProperty().Label($"Failed to parse slot source: {slotSource}");
        if (aliasRoot is null)
            return false.ToProperty().Label($"Failed to parse alias source: {aliasSource}");

        var member = ExtractAbstractMember(slotRoot);
        if (member is null)
            return false.ToProperty().Label($"Could not extract abstract member from: {slotSource}");

        var aliasDecl = ExtractTypeAlias(aliasRoot);
        if (aliasDecl is null)
            return false.ToProperty().Label($"Could not extract type alias from: {aliasSource}");

        // Extract the inner declaration node from the abstract member
        var slotMember = member as AbstractAssociatedTypeMemberAbstractMemberDeclarationNode;
        if (slotMember is null)
            return false.ToProperty().Label($"Abstract member is not an associated type slot: {member.GetType().Name}");

        var slotDeclType = slotMember.AssociatedType.GetType();
        var aliasDeclType = aliasDecl.GetType();

        var areDifferent = slotDeclType != aliasDeclType;

        return areDifferent
            .ToProperty()
            .Label($"Expected different node types but both are {slotDeclType.Name}")
            .Label($"Slot source: {slotSource}")
            .Label($"Alias source: {aliasSource}");
    }
}
