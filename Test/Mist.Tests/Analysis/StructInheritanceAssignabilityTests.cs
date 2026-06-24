// Feature: struct-inheritance, Assignability rules
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying the type checker's struct inheritance assignability rules.
/// Derived→base assignment is accepted (direct and transitive), base→derived is rejected,
/// unrelated struct assignment is rejected, generic struct assignability works with matching
/// type args, and abstract type implementability walks the ancestor chain.
/// </summary>
public class StructInheritanceAssignabilityTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static BaseListNode MakeBaseList(BaseTypeRefNode[] entries)
    {
        return new BaseListNode
        {
            First = entries[0],
            Rest = entries[0],
            BaseTypeRefs = entries.Skip(1).ToImmutableArray(),
        };
    }

    private static BaseTypeRefNode MakeBaseTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new BaseTypeRefNode { Name = MakeToken(name), TypeArgs = typeArgs };
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Expression builders ─────────────────────────────────────────────

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeNewGenericObjectExpression(string typeName, TypeArgumentListNode typeArgs)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            TypeArgs = typeArgs,
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        return WrapPrimaryAsExpression(primary);
    }

    // ─── Statement builders ──────────────────────────────────────────────

    private static StatementNode MakeTypedVarDecl(TypeReferenceNode typeRef, string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = MakeToken(varName),
                Initializer = initializer,
            },
        };
    }

    private static StatementNode MakeNamedTypeVarDecl(string declaredTypeName, string varName, string initializedTypeName)
    {
        return MakeTypedVarDecl(
            MakeNamedTypeRef(declaredTypeName),
            varName,
            MakeNewObjectExpression(initializedTypeName));
    }

    // ─── Type reference builders ─────────────────────────────────────────

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(name) },
            TypeArgs = typeArgs,
        } };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
    }

    private static TypeParameterListNode MakeTypeParamList(string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = MakeToken(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = MakeToken(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
    }

    // ─── Declaration builders ────────────────────────────────────────────

    private static StructDeclarationNode MakeStructDecl(
        string name,
        string? baseStructName = null,
        TypeArgumentListNode? baseTypeArgs = null,
        TypeParameterListNode? typeParams = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (typeParams is not null)
        {
            decl.TypeParams = typeParams;
        }

        if (baseStructName is not null)
        {
            var baseList = MakeBaseList([MakeBaseTypeRef(baseStructName, baseTypeArgs)]);
            decl.AbstractList = baseList;
            decl.Abstracts = baseList;
        }

        return decl;
    }

    private static AbstractDeclarationNode MakeAbstractDecl(string name)
    {
        return new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };
    }

    private static ClassDeclarationNode MakeProgramClass(ImmutableArray<StatementNode> mainStatements)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        return new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
    }

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl) =>
        new StructTypeTypeDeclarationNode { StructDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static TypeDeclarationNode WrapAbstract(AbstractDeclarationNode decl) =>
        new AbstractTypeTypeDeclarationNode { AbstractDeclaration = decl };

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Derived struct assigned to base struct variable is accepted (direct inheritance).
    /// struct Base { }
    /// struct Derived : Base { }
    /// class Program { static void Main() { Base b = new Derived(); } }
    ///
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Fact]
    public void DerivedToBase_DirectAssignment_Accepted()
    {
        var typeDecls = new List<TypeDeclarationNode>
        {
            WrapStruct(MakeStructDecl("Base")),
            WrapStruct(MakeStructDecl("Derived", baseStructName: "Base")),
        };

        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Base", "b", "Derived"));
        typeDecls.Add(WrapClass(MakeProgramClass(mainStatements)));

        var cu = new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
        var model = BuildModel(cu);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    /// <summary>
    /// Derived struct assigned to grandparent base struct variable is accepted (transitive).
    /// struct A { }
    /// struct B : A { }
    /// struct C : B { }
    /// class Program { static void Main() { A a = new C(); } }
    ///
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Fact]
    public void DerivedToBase_TransitiveAssignment_Accepted()
    {
        var typeDecls = new List<TypeDeclarationNode>
        {
            WrapStruct(MakeStructDecl("A")),
            WrapStruct(MakeStructDecl("B", baseStructName: "A")),
            WrapStruct(MakeStructDecl("C", baseStructName: "B")),
        };

        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("A", "a", "C"));
        typeDecls.Add(WrapClass(MakeProgramClass(mainStatements)));

        var cu = new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
        var model = BuildModel(cu);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    /// <summary>
    /// Base struct assigned to derived struct variable is rejected.
    /// struct Base { }
    /// struct Derived : Base { }
    /// class Program { static void Main() { Derived d = new Base(); } }
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Fact]
    public void BaseToDerived_Assignment_Rejected()
    {
        var typeDecls = new List<TypeDeclarationNode>
        {
            WrapStruct(MakeStructDecl("Base")),
            WrapStruct(MakeStructDecl("Derived", baseStructName: "Base")),
        };

        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Derived", "d", "Base"));
        typeDecls.Add(WrapClass(MakeProgramClass(mainStatements)));

        var cu = new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
        var model = BuildModel(cu);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    /// <summary>
    /// Grandparent struct assigned to grandchild struct variable is rejected (transitive).
    /// struct A { }
    /// struct B : A { }
    /// struct C : B { }
    /// class Program { static void Main() { C c = new A(); } }
    ///
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Fact]
    public void BaseToDerived_TransitiveAssignment_Rejected()
    {
        var typeDecls = new List<TypeDeclarationNode>
        {
            WrapStruct(MakeStructDecl("A")),
            WrapStruct(MakeStructDecl("B", baseStructName: "A")),
            WrapStruct(MakeStructDecl("C", baseStructName: "B")),
        };

        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("C", "c", "A"));
        typeDecls.Add(WrapClass(MakeProgramClass(mainStatements)));

        var cu = new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
        var model = BuildModel(cu);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    /// <summary>
    /// Unrelated struct assigned to another unrelated struct variable is rejected.
    /// struct Foo { }
    /// struct Bar { }
    /// class Program { static void Main() { Foo f = new Bar(); } }
    ///
    /// **Validates: Requirements 4.6**
    /// </summary>
    [Fact]
    public void UnrelatedStructs_Assignment_Rejected()
    {
        var typeDecls = new List<TypeDeclarationNode>
        {
            WrapStruct(MakeStructDecl("Foo")),
            WrapStruct(MakeStructDecl("Bar")),
        };

        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("Foo", "f", "Bar"));
        typeDecls.Add(WrapClass(MakeProgramClass(mainStatements)));

        var cu = new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
        var model = BuildModel(cu);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    /// <summary>
    /// Generic derived struct assigned to generic base struct with matching type args is accepted.
    /// struct Base&lt;T&gt; { }
    /// struct Derived&lt;T&gt; : Base&lt;T&gt; { }
    /// class Program { static void Main() { Derived&lt;int&gt; d = new Derived&lt;int&gt;(); Base&lt;int&gt; b = d; } }
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Fact]
    public void GenericStructAssignability_MatchingTypeArgs_Accepted()
    {
        // struct Base<T> { }
        var baseStruct = MakeStructDecl("Base", typeParams: MakeTypeParamList(["T"]));

        // struct Derived<T> : Base<T> { }
        var baseTypeArgs = MakeTypeArgList([MakeNamedTypeRef("T")]);
        var derivedStruct = MakeStructDecl("Derived",
            baseStructName: "Base",
            baseTypeArgs: baseTypeArgs,
            typeParams: MakeTypeParamList(["T"]));

        var typeDecls = new List<TypeDeclarationNode>
        {
            WrapStruct(baseStruct),
            WrapStruct(derivedStruct),
        };

        // Derived<int> d = new Derived<int>();
        var derivedTypeArgs = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var derivedTypeRef = MakeNamedTypeRef("Derived", derivedTypeArgs);
        var newDerivedExpr = MakeNewGenericObjectExpression("Derived", derivedTypeArgs);
        var varDeclD = MakeTypedVarDecl(derivedTypeRef, "d", newDerivedExpr);

        // Base<int> b = d;
        var baseTypeArgs2 = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var baseTypeRef = MakeNamedTypeRef("Base", baseTypeArgs2);
        var varDeclB = MakeTypedVarDecl(baseTypeRef, "b", MakeIdentifierExpression("d"));

        var mainStatements = ImmutableArray.Create(varDeclD, varDeclB);
        typeDecls.Add(WrapClass(MakeProgramClass(mainStatements)));

        var cu = new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
        var model = BuildModel(cu);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    /// <summary>
    /// Abstract type implementability is inherited through the struct ancestor chain.
    /// abstract IFoo { }
    /// struct Base : IFoo { }
    /// struct Derived : Base { }
    /// class Program { static void Main() { IFoo f = new Derived(); } }
    ///
    /// **Validates: Requirements 4.2, 4.3**
    /// </summary>
    [Fact]
    public void AbstractImplementability_ThroughAncestorChain()
    {
        // abstract IFoo { }
        var abstractDecl = MakeAbstractDecl("IFoo");

        // struct Base : IFoo { }
        var baseStruct = new StructDeclarationNode
        {
            Name = MakeToken("Base"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var baseList = MakeBaseList([MakeBaseTypeRef("IFoo")]);
        baseStruct.AbstractList = baseList;
        baseStruct.Abstracts = baseList;

        // struct Derived : Base { }
        var derivedStruct = MakeStructDecl("Derived", baseStructName: "Base");

        var typeDecls = new List<TypeDeclarationNode>
        {
            WrapAbstract(abstractDecl),
            WrapStruct(baseStruct),
            WrapStruct(derivedStruct),
        };

        // IFoo f = new Derived();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl("IFoo", "f", "Derived"));
        typeDecls.Add(WrapClass(MakeProgramClass(mainStatements)));

        var cu = new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
        var model = BuildModel(cu);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }
}
