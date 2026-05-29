// Quick debug test to understand what diagnostics are emitted
using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;
using Xunit;
using Xunit.Abstractions;

namespace Mist.Tests.Semantics;

public class TraitAssignabilityDebugTest
{
    private readonly ITestOutputHelper _output;
    private readonly SemanticModelBuilder _builder = new();

    public TraitAssignabilityDebugTest(ITestOutputHelper output) => _output = output;

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName) =>
        new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() };

    [Fact]
    public void Debug_UnsatisfiedAssignment()
    {
        // trait Printable { int value; int count; }
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("Printable"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakePrimitiveTypeRef("int"),
                        Name = Tok("value"),
                    },
                },
                new TraitFieldMemberTraitMemberDeclarationNode
                {
                    Field = new TraitFieldDeclarationNode
                    {
                        Type = MakePrimitiveTypeRef("int"),
                        Name = Tok("count"),
                    },
                }),
        };

        // class Alpha { public int value; }  -- missing 'count'
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Alpha"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = new FieldDeclarationNode
                    {
                        Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                        Type = MakePrimitiveTypeRef("int"),
                        Name = Tok("value"),
                    },
                }),
        };

        // Main: Printable x = new Alpha();
        var newExpr = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok("Alpha"),
                Span = new Storm.Runtime.SourceSpan(0, 5),
            },
            Arguments = null,
        };
        var postfix = new PostfixNode { Target = newExpr, Suffix = ImmutableArray<PostfixSuffixNode>.Empty };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var initExpr = new ExpressionNode { Assignment = assign };

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new NamedTypeRefTypeReferenceNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok("Printable"),
                        Span = new Storm.Runtime.SourceSpan(0, 9),
                    },
                },
                Name = Tok("x"),
                Initializer = initExpr,
            },
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(varDecl) },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
                },
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
                },
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass },
                }),
        };

        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        var allDiags = diagnostics.ToImmutableArray();
        _output.WriteLine($"Total diagnostics: {allDiags.Length}");
        foreach (var d in allDiags)
        {
            _output.WriteLine($"  [{d.Id.Value}] {d.Message}");
        }

        var traitDiags = allDiags.Where(d => d.Id.Value.Contains("TRAIT")).ToList();
        _output.WriteLine($"Trait-related diagnostics: {traitDiags.Count}");
    }
}
