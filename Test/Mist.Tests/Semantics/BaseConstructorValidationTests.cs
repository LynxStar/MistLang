// Feature: class-inheritance, Task 8.2: Unit tests for constructor behavior
// **Validates: Requirements 8.1, 8.2, 8.3**

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying base constructor validation in the semantic model builder.
/// These tests cover:
/// - Parameterless derived class construction with parameterless base
/// - `: base(args)` constructor chaining
/// - MIST_BASE_CTOR_UNSATISFIED diagnostic when base constructor args are unsatisfied
/// </summary>
public class BaseConstructorValidationTests
{
    private readonly SemanticModelBuilder builder = new();

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

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseClassName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static CompilationUnitNode MakeCompilationUnit(params ClassDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
            (TypeDeclarationNode)new ClassTypeTypeDeclarationNode { ClassDeclaration = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = (types).AsTopLevelItems() };
    }

    /// <summary>
    /// Creates a constructor member declaration with optional parameters and chain.
    /// </summary>
    private static MemberDeclarationNode MakeConstructorMember(
        string className,
        ParameterListNode? parameters = null,
        ConstructorChainNode? chain = null)
    {
        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken(className),
            Parameters = parameters,
            Chain = chain,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
        };

        return new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl };
    }

    /// <summary>
    /// Creates a ParameterListNode with a single int parameter.
    /// </summary>
    private static ParameterListNode MakeIntParameter(string paramName)
    {
        return new ParameterListNode
        {
            Parameter = new ParameterNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken(paramName),
            },
        };
    }

    /// <summary>
    /// Creates a BaseChainConstructorChainNode (`: base(args)`).
    /// When arguments is null, creates `: base()`.
    /// </summary>
    private static BaseChainConstructorChainNode MakeBaseChain(ArgumentListNode? arguments = null)
    {
        return new BaseChainConstructorChainNode { Arguments = arguments };
    }

    /// <summary>
    /// Creates a simple ArgumentListNode with an integer literal expression.
    /// </summary>
    private static ArgumentListNode MakeIntArgument(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
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
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        return new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = expr } };
    }

    // ─── Test 1: Derived with no ctor, base with no ctor → no diagnostic ─

    /// <summary>
    /// Requirement 8.1: When a derived class does not declare any constructor
    /// and the base class has no explicit constructor (implicit parameterless),
    /// no MIST_BASE_CTOR_UNSATISFIED diagnostic should be emitted.
    /// </summary>
    [Fact]
    public void DerivedNoCtor_BaseNoCtor_NoDiagnostic()
    {
        // class Base { }
        // class Derived : Base { }
        var baseDecl = MakeClassDecl("Base");
        var derivedDecl = MakeClassDecl("Derived", baseClassName: "Base");
        var ast = MakeCompilationUnit(baseDecl, derivedDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_BASE_CTOR_UNSATISFIED");
    }

    // ─── Test 2: Derived with no ctor, base with parameterless ctor → no diagnostic ─

    /// <summary>
    /// Requirement 8.1: When a derived class does not declare any constructor
    /// and the base class has a parameterless constructor, no
    /// MIST_BASE_CTOR_UNSATISFIED diagnostic should be emitted.
    /// </summary>
    [Fact]
    public void DerivedNoCtor_BaseParameterlessCtor_NoDiagnostic()
    {
        // class Base { Base() { } }
        // class Derived : Base { }
        var baseMembers = ImmutableArray.Create(MakeConstructorMember("Base"));
        var baseDecl = MakeClassDecl("Base", members: baseMembers);
        var derivedDecl = MakeClassDecl("Derived", baseClassName: "Base");
        var ast = MakeCompilationUnit(baseDecl, derivedDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_BASE_CTOR_UNSATISFIED");
    }

    // ─── Test 3: Derived with no ctor, base requires params → diagnostic ─

    /// <summary>
    /// Requirement 8.3: When the base class requires constructor arguments
    /// and the derived class does not provide them, the semantic model builder
    /// shall emit MIST_BASE_CTOR_UNSATISFIED.
    /// </summary>
    [Fact]
    public void DerivedNoCtor_BaseRequiresParams_EmitsDiagnostic()
    {
        // class Base { Base(int x) { } }
        // class Derived : Base { }
        var baseMembers = ImmutableArray.Create(
            MakeConstructorMember("Base", parameters: MakeIntParameter("x")));
        var baseDecl = MakeClassDecl("Base", members: baseMembers);
        var derivedDecl = MakeClassDecl("Derived", baseClassName: "Base");
        var ast = MakeCompilationUnit(baseDecl, derivedDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_BASE_CTOR_UNSATISFIED");
    }

    // ─── Test 4: Derived with `: base(args)`, base requires params → no diagnostic ─

    /// <summary>
    /// Requirement 8.2: When a derived class constructor uses `: base(args)`
    /// to chain to the base constructor, no MIST_BASE_CTOR_UNSATISFIED
    /// diagnostic should be emitted.
    /// </summary>
    [Fact]
    public void DerivedWithBaseChain_BaseRequiresParams_NoDiagnostic()
    {
        // class Base { Base(int x) { } }
        // class Derived : Base { Derived(int y) : base(0) { } }
        var baseMembers = ImmutableArray.Create(
            MakeConstructorMember("Base", parameters: MakeIntParameter("x")));
        var baseDecl = MakeClassDecl("Base", members: baseMembers);

        var derivedMembers = ImmutableArray.Create(
            MakeConstructorMember(
                "Derived",
                parameters: MakeIntParameter("y"),
                chain: MakeBaseChain(MakeIntArgument("0"))));
        var derivedDecl = MakeClassDecl("Derived", baseClassName: "Base", members: derivedMembers);
        var ast = MakeCompilationUnit(baseDecl, derivedDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_BASE_CTOR_UNSATISFIED");
    }

    // ─── Test 5: Derived with ctor but no `: base(...)`, base requires params → diagnostic ─

    /// <summary>
    /// Requirement 8.3: When the derived class has a constructor but does NOT
    /// chain to the base via `: base(...)`, and the base class requires
    /// constructor arguments, MIST_BASE_CTOR_UNSATISFIED should be emitted.
    /// </summary>
    [Fact]
    public void DerivedCtorWithoutBaseChain_BaseRequiresParams_EmitsDiagnostic()
    {
        // class Base { Base(int x) { } }
        // class Derived : Base { Derived(int y) { } }
        var baseMembers = ImmutableArray.Create(
            MakeConstructorMember("Base", parameters: MakeIntParameter("x")));
        var baseDecl = MakeClassDecl("Base", members: baseMembers);

        var derivedMembers = ImmutableArray.Create(
            MakeConstructorMember("Derived", parameters: MakeIntParameter("y")));
        var derivedDecl = MakeClassDecl("Derived", baseClassName: "Base", members: derivedMembers);
        var ast = MakeCompilationUnit(baseDecl, derivedDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_BASE_CTOR_UNSATISFIED");
    }

    // ─── Test 6: Multi-level: A(int) ← B : base(0) ← C (no ctor) → no diagnostic ─

    /// <summary>
    /// Multi-level inheritance: A has a parameterized constructor, B : A chains
    /// via `: base(args)`, and C : B has no explicit constructor. Since B provides
    /// a constructor that chains to A, and B's constructor itself requires a
    /// parameter, C should get a diagnostic because B has no parameterless ctor.
    ///
    /// However, if B has a parameterless constructor that chains to A, then C
    /// should have no diagnostic. This test verifies the case where B has a
    /// parameterless constructor with `: base(0)`.
    /// </summary>
    [Fact]
    public void MultiLevel_BHasParameterlessCtorChainingToA_CNoCtor_NoDiagnostic()
    {
        // class A { A(int x) { } }
        // class B : A { B() : base(0) { } }
        // class C : B { }
        var aMembers = ImmutableArray.Create(
            MakeConstructorMember("A", parameters: MakeIntParameter("x")));
        var aDecl = MakeClassDecl("A", members: aMembers);

        var bMembers = ImmutableArray.Create(
            MakeConstructorMember(
                "B",
                chain: MakeBaseChain(MakeIntArgument("0"))));
        var bDecl = MakeClassDecl("B", baseClassName: "A", members: bMembers);

        var cDecl = MakeClassDecl("C", baseClassName: "B");
        var ast = MakeCompilationUnit(aDecl, bDecl, cDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // B has a parameterless constructor (no parameters), so C can implicitly
        // call it. No MIST_BASE_CTOR_UNSATISFIED for C.
        // B chains to A via `: base(0)`, so no diagnostic for B either.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_BASE_CTOR_UNSATISFIED");
        ;
    }

}