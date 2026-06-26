// Feature: struct-inheritance, Task 7.3: Unit tests for struct constructor chain validation
// **Validates: Requirements 7.1, 7.2, 7.3, 7.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying struct base constructor chain validation in the semantic model builder
/// and type checker. These tests cover:
/// - `: base(args)` accepted with matching constructor
/// - `: base(args)` rejected with wrong argument types
/// - Missing `: base(...)` when base has no parameterless constructor
/// - `: base()` accepted when base has parameterless constructor
/// </summary>
public class StructInheritanceConstructorTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

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
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
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

        // Run the type checker to get constructor chain validation diagnostics.
        var analyzerBag = new DiagnosticBag();
        analyzer.Analyze(model, analyzerBag);

        var allDiags = diagnostics.ToImmutableArray().AddRange(analyzerBag.ToImmutableArray());
        return (model, allDiags);
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        string? baseStructName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseStructName is not null)
        {
            var baseList = MakeBaseList(baseStructName);
            decl.AbstractList = baseList;
            decl.Abstracts = baseList;
        }

        return decl;
    }

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl)
    {
        return new StructTypeTypeDeclarationNode { StructDeclaration = decl };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Creates a constructor member declaration with optional parameters and chain.
    /// </summary>
    private static MemberDeclarationNode MakeConstructorMember(
        string structName,
        ParameterListNode? parameters = null,
        ConstructorChainNode? chain = null)
    {
        var ctorDecl = new ConstructorDeclarationNode
        {
            Name = MakeToken(structName),
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
    /// Creates a ParameterListNode with two int parameters.
    /// </summary>
    private static ParameterListNode MakeTwoIntParameters(string paramName1, string paramName2)
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
                Name = MakeToken(paramName1),
            },
            Parameters = ImmutableArray.Create(new ParameterNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken(paramName2),
            }),
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
    /// Creates an ArgumentListNode with two integer literal arguments.
    /// </summary>
    private static ArgumentListNode MakeTwoIntArguments(string value1 = "0", string value2 = "0")
    {
        var arg1 = MakeIntArgumentNode(value1);
        var arg2 = MakeIntArgumentNode(value2);

        return new ArgumentListNode
        {
            Argument = arg1,
            Arguments = ImmutableArray.Create<ArgumentNode>(arg2),
        };
    }

    /// <summary>
    /// Creates a single ValueArgumentArgumentNode with an integer literal.
    /// </summary>
    private static ValueArgumentArgumentNode MakeIntArgumentNode(string value)
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

        return new ValueArgumentArgumentNode { Expression = expr };
    }

    /// <summary>
    /// Creates an ArgumentListNode with a string literal expression.
    /// </summary>
    private static ArgumentListNode MakeStringArgument(string value = "\"hello\"")
    {
        var primary = new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = MakeToken(value),
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

    // ─── Test 1: `: base(x, y)` with matching base constructor compiles ─

    /// <summary>
    /// Requirement 7.1, 7.2: When a derived struct constructor uses `: base(x, y)`
    /// and the base struct has a constructor accepting two int parameters,
    /// no MIST_STRUCT_BASE_CTOR_MISMATCH or MIST_STRUCT_BASE_CTOR_REQUIRED
    /// diagnostic should be emitted.
    /// </summary>
    [Fact]
    public void BaseChain_MatchingConstructor_Accepted()
    {
        // struct Base { Base(int x, int y) { } }
        // struct Derived : Base { Derived(int a, int b) : base(0, 0) { } }
        var baseMembers = ImmutableArray.Create(
            MakeConstructorMember("Base", parameters: MakeTwoIntParameters("x", "y")));
        var baseDecl = MakeStructDecl("Base", members: baseMembers);

        var derivedMembers = ImmutableArray.Create(
            MakeConstructorMember(
                "Derived",
                parameters: MakeTwoIntParameters("a", "b"),
                chain: MakeBaseChain(MakeTwoIntArguments("0", "0"))));
        var derivedDecl = MakeStructDecl("Derived", baseStructName: "Base", members: derivedMembers);

        var ast = MakeCompilationUnit(WrapStruct(baseDecl), WrapStruct(derivedDecl));

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_STRUCT_BASE_CTOR_MISMATCH");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_STRUCT_BASE_CTOR_REQUIRED");
    }

    // ─── Test 2: `: base("hello")` when base expects int → MIST_STRUCT_BASE_CTOR_MISMATCH ─

    /// <summary>
    /// Requirement 7.2, 7.3: When a derived struct constructor uses `: base("hello")`
    /// and the base struct has a constructor expecting int, the type checker
    /// shall emit MIST_STRUCT_BASE_CTOR_MISMATCH.
    /// </summary>
    [Fact]
    public void BaseChain_WrongArgTypes_Rejected()
    {
        // struct Base { Base(int x) { } }
        // struct Derived : Base { Derived() : base("hello") { } }
        var baseMembers = ImmutableArray.Create(
            MakeConstructorMember("Base", parameters: MakeIntParameter("x")));
        var baseDecl = MakeStructDecl("Base", members: baseMembers);

        var derivedMembers = ImmutableArray.Create(
            MakeConstructorMember(
                "Derived",
                chain: MakeBaseChain(MakeStringArgument("\"hello\""))));
        var derivedDecl = MakeStructDecl("Derived", baseStructName: "Base", members: derivedMembers);

        var ast = MakeCompilationUnit(WrapStruct(baseDecl), WrapStruct(derivedDecl));

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_STRUCT_BASE_CTOR_MISMATCH");
    }

    // ─── Test 3: Missing `: base(...)` when base has no parameterless ctor ─

    /// <summary>
    /// Requirement 7.4: When the base struct has only a parameterized constructor
    /// (no parameterless constructor) and the derived struct constructor does not
    /// include `: base(...)`, the semantic model builder shall emit
    /// MIST_STRUCT_BASE_CTOR_REQUIRED.
    /// </summary>
    [Fact]
    public void MissingBaseChain_NoParameterlessCtor_Rejected()
    {
        // struct Base { Base(int x) { } }
        // struct Derived : Base { Derived(int a) { } }
        var baseMembers = ImmutableArray.Create(
            MakeConstructorMember("Base", parameters: MakeIntParameter("x")));
        var baseDecl = MakeStructDecl("Base", members: baseMembers);

        var derivedMembers = ImmutableArray.Create(
            MakeConstructorMember("Derived", parameters: MakeIntParameter("a")));
        var derivedDecl = MakeStructDecl("Derived", baseStructName: "Base", members: derivedMembers);

        var ast = MakeCompilationUnit(WrapStruct(baseDecl), WrapStruct(derivedDecl));

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_STRUCT_BASE_CTOR_REQUIRED");
    }

    // ─── Test 4: `: base()` accepted when base has parameterless constructor ─

    /// <summary>
    /// Requirement 7.1, 7.4: When the base struct has a parameterless constructor
    /// and the derived struct constructor uses `: base()`, no diagnostic should
    /// be emitted.
    /// </summary>
    [Fact]
    public void BaseChain_ParameterlessCtor_Accepted()
    {
        // struct Base { Base() { } }
        // struct Derived : Base { Derived(int a) : base() { } }
        var baseMembers = ImmutableArray.Create(
            MakeConstructorMember("Base"));
        var baseDecl = MakeStructDecl("Base", members: baseMembers);

        var derivedMembers = ImmutableArray.Create(
            MakeConstructorMember(
                "Derived",
                parameters: MakeIntParameter("a"),
                chain: MakeBaseChain()));
        var derivedDecl = MakeStructDecl("Derived", baseStructName: "Base", members: derivedMembers);

        var ast = MakeCompilationUnit(WrapStruct(baseDecl), WrapStruct(derivedDecl));

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_STRUCT_BASE_CTOR_MISMATCH");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_STRUCT_BASE_CTOR_REQUIRED");
    }
}