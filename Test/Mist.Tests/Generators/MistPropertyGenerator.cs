// Generators for Mist property declaration AST nodes.
// Produces random but structurally valid property declarations for property-based testing.
// Feature: properties, Property 1: Parse–Print–Parse Round Trip
// **Validates: Requirements 15.8, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 4.1, 4.2, 5.1, 7.1, 7.2, 7.3, 7.4, 8.1, 10.1, 11.1, 11.2, 15.1–15.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for Mist property declaration AST nodes.
/// Generates random property declarations with varying type names, property names,
/// modifier combinations, accessor combinations (get/set/init; auto/block/expression),
/// initializer expressions, and indexer parameters.
/// </summary>
public static class MistPropertyGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] PropertyNames =
        ["Name", "Value", "Count", "Size", "Width", "Height", "IsValid", "Total", "Label", "Score"];

    private static readonly string[] ClassNames =
        ["Widget", "Item", "Record", "Entry", "Node", "Element", "Config", "State"];

    private static readonly string[] IndexerParamNames =
        ["index", "key", "pos", "offset"];

    // ═══════════════════════════════════════════════════════════════════════
    // Modifier generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates a random property-level modifier list (public, private, static combinations).</summary>
    public static Gen<ImmutableArray<ModifierNode>> GenPropertyModifiers()
    {
        return Gen.Elements(
            ImmutableArray<ModifierNode>.Empty,
            ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
            ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(), new StaticModifierModifierNode()),
            ImmutableArray.Create<ModifierNode>(
                new PrivateModifierModifierNode(), new StaticModifierModifierNode()),
            ImmutableArray.Create<ModifierNode>(new StaticModifierModifierNode()));
    }

    /// <summary>
    /// Generates an optional accessor modifier that is more restrictive than the property modifier.
    /// Returns null (no modifier) or private.
    /// </summary>
    public static Gen<ModifierNode?> GenAccessorModifier()
    {
        return Gen.Elements<ModifierNode?>(
            null,
            new PrivateModifierModifierNode());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression helpers (for accessor bodies and initializers)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Creates a simple integer literal expression.</summary>
    public static ExpressionNode MakeIntLiteral(string value = "0")
    {
        return MistAstGenerator.WrapPrimary(new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value),
        });
    }

    /// <summary>Creates a simple bool literal expression.</summary>
    public static ExpressionNode MakeBoolLiteral(bool value)
    {
        return MistAstGenerator.WrapPrimary(value
            ? new TrueLiteralExpressionPrimaryNode()
            : new FalseLiteralExpressionPrimaryNode());
    }

    /// <summary>Creates a simple string literal expression.</summary>
    public static ExpressionNode MakeStringLiteral(string value = "\"hello\"")
    {
        return MistAstGenerator.WrapPrimary(new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = Tok(value),
        });
    }

    /// <summary>Creates a field keyword expression.</summary>
    public static ExpressionNode MakeFieldKeywordExpr()
    {
        return MistAstGenerator.WrapPrimary(new FieldKeywordExpressionPrimaryNode());
    }

    /// <summary>Creates an identifier expression.</summary>
    public static ExpressionNode MakeIdentifierExpr(string name)
    {
        return MistAstGenerator.MakeIdentifierExpr(name);
    }

    /// <summary>Generates a simple initializer expression matching a type.</summary>
    public static Gen<ExpressionNode> GenInitializerForType(TypeReferenceNode type)
    {
        return type.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode prim => prim.PrimitiveType switch
            {
                IntTypePrimitiveTypeNode => Gen.Choose(0, 100).Select(n => MakeIntLiteral(n.ToString())),
                BoolTypePrimitiveTypeNode => Gen.Elements(true, false).Select(MakeBoolLiteral),
                StringTypePrimitiveTypeNode => Gen.Elements("\"hello\"", "\"world\"", "\"test\"").Select(MakeStringLiteral),
                _ => Gen.Choose(0, 100).Select(n => MakeIntLiteral(n.ToString())),
            },
            _ => Gen.Choose(0, 100).Select(n => MakeIntLiteral(n.ToString())),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Accessor generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates an auto get accessor: [modifier] get;</summary>
    public static Gen<AccessorDeclarationNode> GenAutoGetAccessor()
    {
        return from mod in GenAccessorModifier()
               select (AccessorDeclarationNode)new AutoGetAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
               };
    }

    /// <summary>Generates a block get accessor: [modifier] get { return expr; }</summary>
    public static Gen<AccessorDeclarationNode> GenBlockGetAccessor(TypeReferenceNode type)
    {
        return from mod in GenAccessorModifier()
               from expr in GenInitializerForType(type)
               select (AccessorDeclarationNode)new BlockGetAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
                   Body = new BlockNode
                   {
                       Statements = ImmutableArray.Create<StatementNode>(
                           new ReturnStatementStatementNode
                           {
                               ReturnStatement = new ReturnStatementNode { Value = expr },
                           }),
                   },
               };
    }

    /// <summary>Generates an expression get accessor: [modifier] get => expr;</summary>
    public static Gen<AccessorDeclarationNode> GenExpressionGetAccessor(TypeReferenceNode type)
    {
        return from mod in GenAccessorModifier()
               from expr in GenInitializerForType(type)
               select (AccessorDeclarationNode)new ExpressionGetAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
                   Expression = expr,
               };
    }

    /// <summary>Generates an auto set accessor: [modifier] set;</summary>
    public static Gen<AccessorDeclarationNode> GenAutoSetAccessor()
    {
        return from mod in GenAccessorModifier()
               select (AccessorDeclarationNode)new AutoSetAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
               };
    }

    /// <summary>Generates a block set accessor: [modifier] set { field = value; }</summary>
    public static Gen<AccessorDeclarationNode> GenBlockSetAccessor()
    {
        return from mod in GenAccessorModifier()
               select (AccessorDeclarationNode)new BlockSetAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
                   Body = new BlockNode
                   {
                       Statements = ImmutableArray.Create(
                           MistAstGenerator.MakeExpressionStatement(
                               MakeFieldAssignment())),
                   },
               };
    }

    /// <summary>Generates an expression set accessor: [modifier] set => field = value;</summary>
    public static Gen<AccessorDeclarationNode> GenExpressionSetAccessor()
    {
        return from mod in GenAccessorModifier()
               select (AccessorDeclarationNode)new ExpressionSetAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
                   Expression = MakeFieldAssignment(),
               };
    }

    /// <summary>Generates an auto init accessor: [modifier] init;</summary>
    public static Gen<AccessorDeclarationNode> GenAutoInitAccessor()
    {
        return from mod in GenAccessorModifier()
               select (AccessorDeclarationNode)new AutoInitAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
               };
    }

    /// <summary>Generates a block init accessor: [modifier] init { field = value; }</summary>
    public static Gen<AccessorDeclarationNode> GenBlockInitAccessor()
    {
        return from mod in GenAccessorModifier()
               select (AccessorDeclarationNode)new BlockInitAccessorAccessorDeclarationNode
               {
                   AccessorModifier = mod,
                   Body = new BlockNode
                   {
                       Statements = ImmutableArray.Create(
                           MistAstGenerator.MakeExpressionStatement(
                               MakeFieldAssignment())),
                   },
               };
    }

    /// <summary>Creates a field = value assignment expression.</summary>
    private static ExpressionNode MakeFieldAssignment()
    {
        var fieldPrimary = new FieldKeywordExpressionPrimaryNode();
        var fieldPostfix = new PostfixNode
        {
            Target = fieldPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var fieldUnary = new PostfixExpressionUnaryNode { Primary = fieldPostfix };
        var fieldMult = new MultiplicativeNode { Left = fieldUnary };
        var fieldAdd = new AdditiveNode { Left = fieldMult };
        var fieldRel = new RelationalComparisonRelationalNode { Left = fieldAdd };
        var fieldEq = new EqualityNode { Left = fieldRel };
        var fieldLogAnd = new LogicalAndNode { Left = fieldEq };
        var fieldLogOr = new LogicalOrNode { Left = fieldLogAnd };

        var valueExpr = MakeIdentifierExpr("value");

        return new AssignmentFormExpressionNode
        {
            Assignment = new AssignmentNode
            {
                Target = new VoidCoalescingNode { Left = fieldLogOr },
                Value = valueExpr,
            },
        };
    }

    /// <summary>
    /// Generates a random get accessor (auto, block, or expression).
    /// </summary>
    public static Gen<AccessorDeclarationNode> GenGetAccessor(TypeReferenceNode type)
    {
        return Gen.OneOf(
            GenAutoGetAccessor(),
            GenBlockGetAccessor(type),
            GenExpressionGetAccessor(type));
    }

    /// <summary>
    /// Generates a random set accessor (auto, block, or expression).
    /// </summary>
    public static Gen<AccessorDeclarationNode> GenSetAccessor()
    {
        return Gen.OneOf(
            GenAutoSetAccessor(),
            GenBlockSetAccessor(),
            GenExpressionSetAccessor());
    }

    /// <summary>
    /// Generates a random init accessor (auto or block).
    /// </summary>
    public static Gen<AccessorDeclarationNode> GenInitAccessor()
    {
        return Gen.OneOf(
            GenAutoInitAccessor(),
            GenBlockInitAccessor());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Accessor combination generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a valid accessor list for a block property.
    /// Combinations: get-only, get+set, get+init, get+set (mixed forms).
    /// </summary>
    public static Gen<ImmutableArray<AccessorDeclarationNode>> GenAccessorList(TypeReferenceNode type)
    {
        // get-only (auto)
        var getOnly = GenAutoGetAccessor().Select(g =>
            ImmutableArray.Create(g));

        // get + set (both auto)
        var getSet = from g in GenAutoGetAccessor()
                     from s in GenAutoSetAccessor()
                     select ImmutableArray.Create(g, s);

        // get + init (both auto)
        var getInit = from g in GenAutoGetAccessor()
                      from i in GenAutoInitAccessor()
                      select ImmutableArray.Create(g, i);

        // get (block) + set (auto)
        var blockGetAutoSet = from g in GenBlockGetAccessor(type)
                              from s in GenAutoSetAccessor()
                              select ImmutableArray.Create(g, s);

        // get (expression) + set (auto)
        var exprGetAutoSet = from g in GenExpressionGetAccessor(type)
                             from s in GenAutoSetAccessor()
                             select ImmutableArray.Create(g, s);

        // get (auto) + set (block)
        var autoGetBlockSet = from g in GenAutoGetAccessor()
                              from s in GenBlockSetAccessor()
                              select ImmutableArray.Create(g, s);

        return Gen.OneOf(getOnly, getSet, getInit, blockGetAutoSet, exprGetAutoSet, autoGetBlockSet);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property declaration generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random block property declaration (with accessor list).
    /// </summary>
    public static Gen<PropertyDeclarationNode> GenBlockProperty()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from name in Gen.Elements(PropertyNames)
               from mods in GenPropertyModifiers()
               from accessors in GenAccessorList(type)
               from hasInit in Gen.Elements(true, false)
               from initExpr in GenInitializerForType(type)
               select (PropertyDeclarationNode)new BlockPropertyPropertyDeclarationNode
               {
                   Modifiers = mods,
                   Type = type,
                   Name = Tok(name),
                   Accessors = accessors,
                   Initializer = hasInit ? initExpr : null,
               };
    }

    /// <summary>
    /// Generates a random expression-bodied property declaration.
    /// </summary>
    public static Gen<PropertyDeclarationNode> GenExpressionBodiedProperty()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from name in Gen.Elements(PropertyNames)
               from mods in GenPropertyModifiers()
               from body in GenInitializerForType(type)
               select (PropertyDeclarationNode)new ExpressionBodiedPropertyPropertyDeclarationNode
               {
                   Modifiers = mods,
                   Type = type,
                   Name = Tok(name),
                   Body = body,
               };
    }

    /// <summary>
    /// Generates a random property declaration (block or expression-bodied).
    /// </summary>
    public static Gen<PropertyDeclarationNode> GenPropertyDeclaration()
    {
        return Gen.OneOf(GenBlockProperty(), GenExpressionBodiedProperty());
    }

    /// <summary>
    /// Generates a property member declaration node.
    /// </summary>
    public static Gen<MemberDeclarationNode> GenPropertyMember()
    {
        return GenPropertyDeclaration().Select(p =>
            (MemberDeclarationNode)new PropertyMemberMemberDeclarationNode { Property = p });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Indexer declaration generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random indexer declaration.
    /// </summary>
    public static Gen<IndexerDeclarationNode> GenIndexerDeclaration()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from mods in GenPropertyModifiers()
               from paramName in Gen.Elements(IndexerParamNames)
               from paramType in MistTypeGenerator.GenPrimitiveTypeRef()
               from accessors in GenAccessorList(type)
               select (new IndexerDeclarationNode
               {
                   Modifiers = mods,
                   Type = type,
                   THIS = Tok("this"),
                   Parameters = new ParameterListNode
                   {
                       Parameter = new ParameterNode
                       {
                           Type = paramType,
                           Name = Tok(paramName),
                       },
                   },
                   Body = new BlockIndexerBodyIndexerBodyNode
                   {
                       Accessors = accessors,
                   },
               });
    }

    /// <summary>
    /// Generates an indexer member declaration node.
    /// </summary>
    public static Gen<MemberDeclarationNode> GenIndexerMember()
    {
        return GenIndexerDeclaration().Select(i =>
            (MemberDeclarationNode)new IndexerMemberMemberDeclarationNode { Indexer = i });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Abstract type property declaration generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random abstract type property declaration.
    /// </summary>
    public static Gen<AbstractPropertyDeclarationNode> GenAbstractPropertyDeclaration()
    {
        var getOnly = Gen.Constant(ImmutableArray.Create<AbstractAccessorDeclarationNode>(
            new AbstractGetAccessorAbstractAccessorDeclarationNode()));

        var getSet = Gen.Constant(ImmutableArray.Create<AbstractAccessorDeclarationNode>(
            new AbstractGetAccessorAbstractAccessorDeclarationNode(),
            new AbstractSetAccessorAbstractAccessorDeclarationNode()));

        var setOnly = Gen.Constant(ImmutableArray.Create<AbstractAccessorDeclarationNode>(
            new AbstractSetAccessorAbstractAccessorDeclarationNode()));

        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from name in Gen.Elements(PropertyNames)
               from accessors in Gen.OneOf(getOnly, getSet, setOnly)
               select new AbstractPropertyDeclarationNode
               {
                   Type = type,
                   Name = Tok(name),
                   Accessors = accessors,
               };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Complete program generators (with properties)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a CompilationUnitNode with a class containing random property members.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassWithProperties()
    {
        return from className in Gen.Elements(ClassNames)
               from props in GenPropertyMember().ListOf()
                   .Where(list => list.Count is > 0 and <= 3)
               select MakeClassWithMembers(className, props.ToImmutableArray());
    }

    /// <summary>
    /// Generates a CompilationUnitNode with a class containing an indexer.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassWithIndexer()
    {
        return from className in Gen.Elements(ClassNames)
               from indexer in GenIndexerMember()
               select MakeClassWithMembers(className, ImmutableArray.Create(indexer));
    }

    /// <summary>
    /// Generates a CompilationUnitNode with an abstract type containing property declarations.
    /// </summary>
    public static Gen<CompilationUnitNode> GenAbstractWithProperties()
    {
        return from name in Gen.Elements("IReadable", "IWritable", "IConfigurable", "IEntity")
               from props in GenAbstractPropertyDeclaration().ListOf()
                   .Where(list => list.Count is > 0 and <= 2)
               select MakeAbstractUnit(name, props.ToImmutableArray());
    }

    /// <summary>
    /// Generates a random CompilationUnitNode with property-related declarations.
    /// </summary>
    public static Gen<CompilationUnitNode> GenPropertyProgram()
    {
        return Gen.OneOf(
            GenClassWithProperties(),
            GenClassWithIndexer(),
            GenAbstractWithProperties());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Creates a CompilationUnitNode with a single class containing the given members.</summary>
    public static CompilationUnitNode MakeClassWithMembers(
        string className,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = members,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>Creates a CompilationUnitNode with a single abstract type containing property declarations.</summary>
    public static CompilationUnitNode MakeAbstractUnit(
        string abstractName,
        ImmutableArray<AbstractPropertyDeclarationNode> properties)
    {
        var members = properties.Select(p =>
            (AbstractMemberDeclarationNode)new AbstractPropertyMemberAbstractMemberDeclarationNode
            {
                Property = p,
            }).ToImmutableArray();

        var abstractDecl = new AbstractDeclarationNode
        {
            Name = Tok(abstractName),
            Members = members,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Creates a CompilationUnitNode with a class containing a property and a Main method
    /// that accesses the property. Useful for semantic/type-checker tests.
    /// </summary>
    public static CompilationUnitNode MakeProgramWithProperty(
        PropertyDeclarationNode property,
        ImmutableArray<StatementNode>? mainStatements = null)
    {
        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
        members.Add(new PropertyMemberMemberDeclarationNode { Property = property });

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = mainStatements ?? ImmutableArray<StatementNode>.Empty,
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
        members.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = members.ToImmutable(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registrations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>FsCheck Arbitrary for property programs.</summary>
    public static Arbitrary<CompilationUnitNode> ArbitraryPropertyProgram()
    {
        return GenPropertyProgram().ToArbitrary();
    }
}