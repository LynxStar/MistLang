using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests for constraint clause parsing.
/// Validates that the parser produces correct AST nodes for each constraint variant,
/// handles multiple constraints and where clauses, and works across all declaration types.
///
/// Requirements: 1.9, 1.10, 9.2
/// </summary>
public class ConstraintClauseParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    private static CompilationUnitNode ParseWithoutErrors(string source)
    {
        var result = ParseSource(source);
        Assert.Empty(result.Diagnostics);
        var cu = result.Root as CompilationUnitNode;
        Assert.NotNull(cu);
        return cu!;
    }

    private static ClassDeclarationNode GetClassDeclaration(CompilationUnitNode cu)
    {
        var types = cu.GetTypes();
        Assert.NotEmpty(types);
        var classType = types[0] as ClassTypeTypeDeclarationNode;
        Assert.NotNull(classType);
        return classType!.ClassDeclaration;
    }

    private static StructDeclarationNode GetStructDeclaration(CompilationUnitNode cu)
    {
        var types = cu.GetTypes();
        Assert.NotEmpty(types);
        var structType = types[0] as StructTypeTypeDeclarationNode;
        Assert.NotNull(structType);
        return structType!.StructDeclaration;
    }

    private static AbstractDeclarationNode GetAbstractDeclaration(CompilationUnitNode cu)
    {
        var types = cu.GetTypes();
        Assert.NotEmpty(types);
        var abstractType = types[0] as AbstractTypeTypeDeclarationNode;
        Assert.NotNull(abstractType);
        return abstractType!.AbstractDeclaration;
    }

    private static MethodDeclarationNode GetMethodDeclaration(CompilationUnitNode cu)
    {
        var classDecl = GetClassDeclaration(cu);
        Assert.NotEmpty(classDecl.Members);
        var methodMember = classDecl.Members[0] as MethodMemberMemberDeclarationNode;
        Assert.NotNull(methodMember);
        return methodMember!.Method;
    }

    private static AbstractMethodDeclarationNode GetAbstractMethodDeclaration(CompilationUnitNode cu)
    {
        var abstractDecl = GetAbstractDeclaration(cu);
        Assert.NotEmpty(abstractDecl.Members);
        var methodMember = abstractDecl.Members[0] as AbstractMethodMemberAbstractMemberDeclarationNode;
        Assert.NotNull(methodMember);
        return methodMember!.Method;
    }

    private static TypeAliasDeclarationNode GetTypeAliasDeclaration(CompilationUnitNode cu)
    {
        var item = cu.Items.FirstOrDefault();
        Assert.NotNull(item);
        var aliasItem = item as TypeAliasItemTopLevelItemNode;
        Assert.NotNull(aliasItem);
        return aliasItem!.TypeAliasDeclaration;
    }

    /// <summary>Gets all constraints from a GenericConstraintClauseWhereClauseNode as a flat list.</summary>
    private static List<ConstraintNode> GetConstraints(GenericConstraintClauseWhereClauseNode clause)
    {
        var list = new List<ConstraintNode> { clause.Constraints.Constraint };
        list.AddRange(clause.Constraints.Constraints);
        return list;
    }

    /// <summary>Casts a WhereClauseNode to GenericConstraintClauseWhereClauseNode for test assertions.</summary>
    private static GenericConstraintClauseWhereClauseNode AsGenericConstraint(WhereClauseNode node)
    {
        var generic = node as GenericConstraintClauseWhereClauseNode;
        Assert.NotNull(generic);
        return generic!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Single constraint clause with each constraint variant
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassConstraint_ParsesCorrectly()
    {
        var source = "class Box<T> where T : class { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        var clause = AsGenericConstraint(classDecl.Constraints[0]);
        Assert.Equal("T", clause.Name.Lexeme);

        var constraints = GetConstraints(clause);
        Assert.Single(constraints);
        Assert.IsType<ClassConstraintConstraintNode>(constraints[0]);
    }

    [Fact]
    public void StructConstraint_ParsesCorrectly()
    {
        var source = "class Box<T> where T : struct { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        var clause = AsGenericConstraint(classDecl.Constraints[0]);
        Assert.Equal("T", clause.Name.Lexeme);

        var constraints = GetConstraints(clause);
        Assert.Single(constraints);
        Assert.IsType<StructConstraintConstraintNode>(constraints[0]);
    }

    [Fact]
    public void ParameterlessConstructorConstraint_ParsesCorrectly()
    {
        var source = "class Factory<T> where T : new() { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        var clause = AsGenericConstraint(classDecl.Constraints[0]);
        Assert.Equal("T", clause.Name.Lexeme);

        var constraints = GetConstraints(clause);
        Assert.Single(constraints);
        var ctorConstraint = Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
        Assert.Null(ctorConstraint.ParamTypes);
    }

    [Fact]
    public void ParameterizedConstructorConstraint_SingleParam_ParsesCorrectly()
    {
        var source = "class Factory<T> where T : new(int) { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        var clause = AsGenericConstraint(classDecl.Constraints[0]);
        Assert.Equal("T", clause.Name.Lexeme);

        var constraints = GetConstraints(clause);
        Assert.Single(constraints);
        var ctorConstraint = Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
        Assert.NotNull(ctorConstraint.ParamTypes);

        // Single parameter type: int
        var paramTypes = ctorConstraint.ParamTypes!;
        Assert.Empty(paramTypes.TypeReferences); // Only the first TypeReference, no additional ones
        var firstType = paramTypes.TypeReference.First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(firstType);
        Assert.IsType<IntTypePrimitiveTypeNode>(firstType!.PrimitiveType);
    }

    [Fact]
    public void ParameterizedConstructorConstraint_MultipleParams_ParsesCorrectly()
    {
        var source = "class Factory<T> where T : new(int, string) { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Single(constraints);
        var ctorConstraint = Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
        Assert.NotNull(ctorConstraint.ParamTypes);

        // Two parameter types: int, string
        var paramTypes = ctorConstraint.ParamTypes!;
        Assert.Single(paramTypes.TypeReferences); // One additional type beyond the first

        var firstType = paramTypes.TypeReference.First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(firstType);
        Assert.IsType<IntTypePrimitiveTypeNode>(firstType!.PrimitiveType);

        var secondType = paramTypes.TypeReferences[0].First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(secondType);
        Assert.IsType<StringTypePrimitiveTypeNode>(secondType!.PrimitiveType);
    }

    [Fact]
    public void TypeReferenceConstraint_ParsesCorrectly()
    {
        var source = "class Sorter<T> where T : IComparable { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        var clause = AsGenericConstraint(classDecl.Constraints[0]);
        Assert.Equal("T", clause.Name.Lexeme);

        var constraints = GetConstraints(clause);
        Assert.Single(constraints);
        var typeConstraint = Assert.IsType<TypeConstraintConstraintNode>(constraints[0]);
        var namedType = typeConstraint.Type.First as NamedSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(namedType);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiple constraints in a single clause
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleConstraints_InSingleClause_ParsesCorrectly()
    {
        var source = "class Sorter<T> where T : class, IComparable, new() { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        var clause = AsGenericConstraint(classDecl.Constraints[0]);
        Assert.Equal("T", clause.Name.Lexeme);

        var constraints = GetConstraints(clause);
        Assert.Equal(3, constraints.Count);
        Assert.IsType<ClassConstraintConstraintNode>(constraints[0]);
        Assert.IsType<TypeConstraintConstraintNode>(constraints[1]);
        Assert.IsType<ConstructorConstraintConstraintNode>(constraints[2]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiple where clauses on a single declaration
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleWhereClauses_ParsesCorrectly()
    {
        var source = "class Pair<K, V> where K : class where V : struct { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Equal(2, classDecl.Constraints.Length);

        Assert.Equal("K", AsGenericConstraint(classDecl.Constraints[0]).Name.Lexeme);
        var kConstraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Single(kConstraints);
        Assert.IsType<ClassConstraintConstraintNode>(kConstraints[0]);

        Assert.Equal("V", AsGenericConstraint(classDecl.Constraints[1]).Name.Lexeme);
        var vConstraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[1]));
        Assert.Single(vConstraints);
        Assert.IsType<StructConstraintConstraintNode>(vConstraints[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constraint clauses on each declaration type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassDeclaration_WithConstraints_ParsesCorrectly()
    {
        var source = "class Container<T> where T : new() { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Single(classDecl.Constraints);
        Assert.Equal("T", AsGenericConstraint(classDecl.Constraints[0]).Name.Lexeme);
    }

    [Fact]
    public void StructDeclaration_WithConstraints_ParsesCorrectly()
    {
        var source = "struct Wrapper<T> where T : class { }";
        var cu = ParseWithoutErrors(source);
        var structDecl = GetStructDeclaration(cu);

        Assert.Single(structDecl.Constraints);
        Assert.Equal("T", AsGenericConstraint(structDecl.Constraints[0]).Name.Lexeme);
        var constraints = GetConstraints(AsGenericConstraint(structDecl.Constraints[0]));
        Assert.IsType<ClassConstraintConstraintNode>(constraints[0]);
    }

    [Fact]
    public void AbstractDeclaration_WithConstraints_ParsesCorrectly()
    {
        var source = "abstract IRepository<T> where T : class { }";
        var cu = ParseWithoutErrors(source);
        var abstractDecl = GetAbstractDeclaration(cu);

        Assert.Single(abstractDecl.Constraints);
        Assert.Equal("T", AsGenericConstraint(abstractDecl.Constraints[0]).Name.Lexeme);
        var constraints = GetConstraints(AsGenericConstraint(abstractDecl.Constraints[0]));
        Assert.IsType<ClassConstraintConstraintNode>(constraints[0]);
    }

    [Fact]
    public void MethodDeclaration_WithConstraints_ParsesCorrectly()
    {
        var source = "class Service { public T Create<T>() where T : new() { } }";
        var cu = ParseWithoutErrors(source);
        var method = GetMethodDeclaration(cu);

        Assert.Single(method.Constraints);
        Assert.Equal("T", AsGenericConstraint(method.Constraints[0]).Name.Lexeme);
        var constraints = GetConstraints(AsGenericConstraint(method.Constraints[0]));
        Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
    }

    [Fact]
    public void AbstractMethodDeclaration_WithConstraints_ParsesCorrectly()
    {
        var source = "abstract IFactory { T Create<T>() where T : new(); }";
        var cu = ParseWithoutErrors(source);
        var method = GetAbstractMethodDeclaration(cu);

        Assert.Single(method.Constraints);
        Assert.Equal("T", AsGenericConstraint(method.Constraints[0]).Name.Lexeme);
        var constraints = GetConstraints(AsGenericConstraint(method.Constraints[0]));
        Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
    }

    [Fact]
    public void TypeAliasDeclaration_WithConstraints_ParsesCorrectly()
    {
        var source = "type StructList<T> = List<T> where T : struct;";
        var cu = ParseWithoutErrors(source);
        var alias = GetTypeAliasDeclaration(cu);

        Assert.Single(alias.Constraints);
        Assert.Equal("T", AsGenericConstraint(alias.Constraints[0]).Name.Lexeme);
        var constraints = GetConstraints(AsGenericConstraint(alias.Constraints[0]));
        Assert.IsType<StructConstraintConstraintNode>(constraints[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Backward compatibility: declarations without constraints still parse
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassDeclaration_WithoutConstraints_ParsesCorrectly()
    {
        var source = "class Box<T> { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        Assert.Empty(classDecl.Constraints);
    }

    [Fact]
    public void StructDeclaration_WithoutConstraints_ParsesCorrectly()
    {
        var source = "struct Point<T> { }";
        var cu = ParseWithoutErrors(source);
        var structDecl = GetStructDeclaration(cu);

        Assert.Empty(structDecl.Constraints);
    }

    [Fact]
    public void AbstractDeclaration_WithoutConstraints_ParsesCorrectly()
    {
        var source = "abstract IReadable<T> { }";
        var cu = ParseWithoutErrors(source);
        var abstractDecl = GetAbstractDeclaration(cu);

        Assert.Empty(abstractDecl.Constraints);
    }

    [Fact]
    public void MethodDeclaration_WithoutConstraints_ParsesCorrectly()
    {
        var source = "class Service { public T Get<T>() { } }";
        var cu = ParseWithoutErrors(source);
        var method = GetMethodDeclaration(cu);

        Assert.Empty(method.Constraints);
    }

    [Fact]
    public void AbstractMethodDeclaration_WithoutConstraints_ParsesCorrectly()
    {
        var source = "abstract IService { T Get<T>(); }";
        var cu = ParseWithoutErrors(source);
        var method = GetAbstractMethodDeclaration(cu);

        Assert.Empty(method.Constraints);
    }

    [Fact]
    public void TypeAliasDeclaration_WithoutConstraints_ParsesCorrectly()
    {
        var source = "type StringList<T> = List<T>;";
        var cu = ParseWithoutErrors(source);
        var alias = GetTypeAliasDeclaration(cu);

        Assert.Empty(alias.Constraints);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor constraints at any position in the constraint list
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstructorConstraint_AtBeginning_ParsesCorrectly()
    {
        var source = "class Factory<T> where T : new(), IComparable, class { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Equal(3, constraints.Count);
        Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
        Assert.IsType<TypeConstraintConstraintNode>(constraints[1]);
        Assert.IsType<ClassConstraintConstraintNode>(constraints[2]);
    }

    [Fact]
    public void ConstructorConstraint_InMiddle_ParsesCorrectly()
    {
        var source = "class Factory<T> where T : class, new(), IComparable { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Equal(3, constraints.Count);
        Assert.IsType<ClassConstraintConstraintNode>(constraints[0]);
        Assert.IsType<ConstructorConstraintConstraintNode>(constraints[1]);
        Assert.IsType<TypeConstraintConstraintNode>(constraints[2]);
    }

    [Fact]
    public void ConstructorConstraint_AtEnd_ParsesCorrectly()
    {
        var source = "class Factory<T> where T : class, IComparable, new() { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Equal(3, constraints.Count);
        Assert.IsType<ClassConstraintConstraintNode>(constraints[0]);
        Assert.IsType<TypeConstraintConstraintNode>(constraints[1]);
        Assert.IsType<ConstructorConstraintConstraintNode>(constraints[2]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Parameterized constructor constraints with multiple params
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParameterizedConstructorConstraint_ThreeParams_ParsesCorrectly()
    {
        var source = "class Factory<T> where T : new(int, int, string) { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Single(constraints);
        var ctorConstraint = Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
        Assert.NotNull(ctorConstraint.ParamTypes);

        // Three parameter types: int, int, string
        var paramTypes = ctorConstraint.ParamTypes!;
        Assert.Equal(2, paramTypes.TypeReferences.Length); // 2 additional beyond the first

        var firstType = paramTypes.TypeReference.First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(firstType);
        Assert.IsType<IntTypePrimitiveTypeNode>(firstType!.PrimitiveType);

        var secondType = paramTypes.TypeReferences[0].First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(secondType);
        Assert.IsType<IntTypePrimitiveTypeNode>(secondType!.PrimitiveType);

        var thirdType = paramTypes.TypeReferences[1].First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(thirdType);
        Assert.IsType<StringTypePrimitiveTypeNode>(thirdType!.PrimitiveType);
    }

    [Fact]
    public void MultipleConstructorConstraints_ParsesCorrectly()
    {
        var source = "class MultiFactory<T> where T : new(), new(int), new(bool) { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Equal(3, constraints.Count);

        // First: new()
        var ctor1 = Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
        Assert.Null(ctor1.ParamTypes);

        // Second: new(int)
        var ctor2 = Assert.IsType<ConstructorConstraintConstraintNode>(constraints[1]);
        Assert.NotNull(ctor2.ParamTypes);
        var ctor2Type = ctor2.ParamTypes!.TypeReference.First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(ctor2Type);
        Assert.IsType<IntTypePrimitiveTypeNode>(ctor2Type!.PrimitiveType);

        // Third: new(bool)
        var ctor3 = Assert.IsType<ConstructorConstraintConstraintNode>(constraints[2]);
        Assert.NotNull(ctor3.ParamTypes);
        var ctor3Type = ctor3.ParamTypes!.TypeReference.First as PrimitiveSingleTypeRefSingleTypeRefNode;
        Assert.NotNull(ctor3Type);
        Assert.IsType<BoolTypePrimitiveTypeNode>(ctor3Type!.PrimitiveType);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constraints in any order are accepted
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstraintsInAnyOrder_NewFirst_ParsesCorrectly()
    {
        var source = "class C<T> where T : new(int), IComparable, class { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Equal(3, constraints.Count);
        Assert.IsType<ConstructorConstraintConstraintNode>(constraints[0]);
        Assert.IsType<TypeConstraintConstraintNode>(constraints[1]);
        Assert.IsType<ClassConstraintConstraintNode>(constraints[2]);
    }

    [Fact]
    public void ConstraintsInAnyOrder_StructBeforeTypeRef_ParsesCorrectly()
    {
        var source = "class C<T> where T : struct, IDisposable { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.Equal(2, constraints.Count);
        Assert.IsType<StructConstraintConstraintNode>(constraints[0]);
        Assert.IsType<TypeConstraintConstraintNode>(constraints[1]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Class with base clause and constraints
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassWithBaseClause_AndConstraints_ParsesCorrectly()
    {
        var source = "class Derived<T> : Base where T : class { }";
        var cu = ParseWithoutErrors(source);
        var classDecl = GetClassDeclaration(cu);

        // Has constraints
        Assert.Single(classDecl.Constraints);
        Assert.Equal("T", AsGenericConstraint(classDecl.Constraints[0]).Name.Lexeme);
        var constraints = GetConstraints(AsGenericConstraint(classDecl.Constraints[0]));
        Assert.IsType<ClassConstraintConstraintNode>(constraints[0]);

        // Verify the class name and type params are correct (base clause was parsed)
        Assert.Equal("Derived", classDecl.Name.Lexeme);
        Assert.NotNull(classDecl.TypeParams);
    }

    [Fact]
    public void StructWithAbstractList_AndConstraints_ParsesCorrectly()
    {
        var source = "struct Wrapper<T> : IDisposable where T : struct { }";
        var cu = ParseWithoutErrors(source);
        var structDecl = GetStructDeclaration(cu);

        // Has constraints
        Assert.Single(structDecl.Constraints);
        Assert.Equal("T", AsGenericConstraint(structDecl.Constraints[0]).Name.Lexeme);

        // Verify the struct name and type params are correct (abstract list was parsed)
        Assert.Equal("Wrapper", structDecl.Name.Lexeme);
        Assert.NotNull(structDecl.TypeParams);
    }

    [Fact]
    public void AbstractWithBaseAbstracts_AndConstraints_ParsesCorrectly()
    {
        var source = "abstract IRepository<T> : IDisposable where T : class { }";
        var cu = ParseWithoutErrors(source);
        var abstractDecl = GetAbstractDeclaration(cu);

        // Has constraints
        Assert.Single(abstractDecl.Constraints);
        Assert.Equal("T", AsGenericConstraint(abstractDecl.Constraints[0]).Name.Lexeme);

        // Verify the abstract name and type params are correct (base abstracts were parsed)
        Assert.Equal("IRepository", abstractDecl.Name.Lexeme);
        Assert.NotNull(abstractDecl.TypeParams);
    }
}