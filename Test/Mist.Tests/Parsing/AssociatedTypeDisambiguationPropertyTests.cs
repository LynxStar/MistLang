// Feature: associated-types, Property 2: Parser disambiguation between abstract slots and concrete aliases
// **Validates: Requirements 1.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

public sealed class DisambiguationTypeScenario
{
    public string TypeName { get; }

    public string Constraint { get; }

    public string TypeExpr { get; }

    public DisambiguationTypeScenario(string typeName, string constraint, string typeExpr)
    {
        TypeName = typeName;
        Constraint = constraint;
        TypeExpr = typeExpr;
    }

    public override string ToString()
    {
        return $"TypeName={TypeName}, Constraint={Constraint}, TypeExpr={TypeExpr}";
    }
}

public class AssociatedTypeDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

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

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return null;
        }

        return result.Root as CompilationUnitNode;
    }

    private static string BuildAssociatedTypeSlotSource(string name, string constraint)
    {
        return $"abstract ITest {{ type {name} : {constraint}; }}";
    }

    private static string BuildTypeAliasSource(string name, string typeExpr)
    {
        return $"type {name} = {typeExpr};";
    }

    private static MemberDeclarationNode? ExtractAbstractMember(CompilationUnitNode cu)
    {
        if(cu.Items.FirstOrDefault() is not TypeItemTopLevelItemNode typeItem)
        {
            return null;
        }

        if(typeItem.TypeDeclaration is not BodyTypeTypeDeclarationNode { TypeBody: { Keyword: AbstractKeywordTypeKeywordNode } typeBody })
        {
            return null;
        }

        return typeBody.Members.FirstOrDefault();
    }

    private static TypeAliasDeclarationNode? ExtractTypeAlias(CompilationUnitNode cu)
    {
        if(cu.Items.FirstOrDefault() is TypeItemTopLevelItemNode { TypeDeclaration: TypeAliasTypeTypeDeclarationNode aliasType })
        {
            return aliasType.TypeAliasDeclaration;
        }

        return null;
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AssociatedTypeDisambiguationPropertyTests) })]
    public Property AssociatedTypeSlot_ProducesAssociatedTypeSlotDeclarationNode(DisambiguationTypeScenario scenario)
    {
        var source = BuildAssociatedTypeSlotSource(scenario.TypeName, scenario.Constraint);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var member = ExtractAbstractMember(root);
        if(member is null)
        {
            return false.ToProperty().Label($"Could not extract abstract member from: {source}");
        }

        var isAssociatedTypeSlot = member is AssociatedTypeMemberMemberDeclarationNode;

        return isAssociatedTypeSlot
            .ToProperty()
            .Label($"Expected AssociatedTypeMemberMemberDeclarationNode but got {member.GetType().Name} for: {source}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AssociatedTypeDisambiguationPropertyTests) })]
    public Property TypeAlias_ProducesTypeAliasDeclarationNode(DisambiguationTypeScenario scenario)
    {
        var source = BuildTypeAliasSource(scenario.TypeName, scenario.TypeExpr);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var aliasDecl = ExtractTypeAlias(root);

        return (aliasDecl is not null)
            .ToProperty()
            .Label($"Expected TypeAliasDeclarationNode but did not find one in: {source}");
    }

    private static string BuildUnconstrainedAssociatedTypeSlotSource(string name)
    {
        return $"abstract ITest {{ type {name}; }}";
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AssociatedTypeDisambiguationPropertyTests) })]
    public Property UnconstrainedSlot_ProducesUnifiedNodeWithNullConstraintClause(DisambiguationTypeScenario scenario)
    {
        var source = BuildUnconstrainedAssociatedTypeSlotSource(scenario.TypeName);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var member = ExtractAbstractMember(root);
        if(member is null)
        {
            return false.ToProperty().Label($"Could not extract abstract member from: {source}");
        }

        if(member is not AssociatedTypeMemberMemberDeclarationNode slotMember)
        {
            return false.ToProperty().Label($"Expected AssociatedTypeMemberMemberDeclarationNode but got {member.GetType().Name} for: {source}");
        }

        var hasNullConstraints = slotMember.AssociatedType.Group0?.Constraints is null;

        return hasNullConstraints
            .ToProperty()
            .Label($"Expected null Constraints on unconstrained slot but it was non-null for: {source}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AssociatedTypeDisambiguationPropertyTests) })]
    public Property SlotAndAlias_ProduceDifferentNodeTypes(DisambiguationTypeScenario scenario)
    {
        var slotSource = BuildAssociatedTypeSlotSource(scenario.TypeName, scenario.Constraint);
        var aliasSource = BuildTypeAliasSource(scenario.TypeName, scenario.TypeExpr);

        var slotRoot = ParseSource(slotSource);
        var aliasRoot = ParseSource(aliasSource);

        if(slotRoot is null)
        {
            return false.ToProperty().Label($"Failed to parse slot source: {slotSource}");
        }

        if(aliasRoot is null)
        {
            return false.ToProperty().Label($"Failed to parse alias source: {aliasSource}");
        }

        var member = ExtractAbstractMember(slotRoot);
        if(member is null)
        {
            return false.ToProperty().Label($"Could not extract abstract member from: {slotSource}");
        }

        var aliasDecl = ExtractTypeAlias(aliasRoot);
        if(aliasDecl is null)
        {
            return false.ToProperty().Label($"Could not extract type alias from: {aliasSource}");
        }

        if(member is not AssociatedTypeMemberMemberDeclarationNode slotMember)
        {
            return false.ToProperty().Label($"Abstract member is not an associated type slot: {member.GetType().Name}");
        }

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