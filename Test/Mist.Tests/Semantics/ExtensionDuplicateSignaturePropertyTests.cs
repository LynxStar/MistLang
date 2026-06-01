// Feature: extension-methods, Property 10: Duplicate Signature Detection
//
// **Validates: Requirements 8.9, 20.4**
//
// For any two extension blocks on the same receiver type T that declare members
// with identical names and parameter type lists, the semantic model builder
// SHALL emit diagnostic `MIST_SB_0073`.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for duplicate extension member signature detection.
///
/// **Validates: Requirements 8.9, 20.4**
///
/// For any two extension blocks on the same receiver type T that declare members
/// with identical names and parameter type lists, the semantic model builder
/// SHALL emit diagnostic <c>MIST_SB_0073</c>.
/// </summary>
public class ExtensionDuplicateSignaturePropertyTests
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

    private static (Mist.Semantics.Contracts.ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics)
        BuildModelWithDiagnostics(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Describes a scenario where two extension blocks on the same receiver type
    /// declare a member with an identical signature.
    /// </summary>
#pragma warning disable CA1034
    public record DuplicateSignatureScenario(
        string ReceiverTypeName,
        string ReceiverParamName1,
        string ReceiverParamName2,
        DuplicateMemberKind MemberKind,
        string MemberName,
        string ReturnType,
        int ParameterCount)
    {
        public override string ToString() =>
            MemberKind == DuplicateMemberKind.Property
                ? $"extension({ReceiverTypeName}) {{ {ReturnType} {MemberName} }} x2"
                : $"extension({ReceiverTypeName}) {{ {ReturnType} {MemberName}({ParameterCount} params) }} x2";
    }

    public enum DuplicateMemberKind
    {
        Method,
        Property,
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] ReceiverTypeNames =
        ["MyClass", "Widget", "Container", "Service", "Handler"];

    private static readonly string[] ReceiverParamNames =
        ["self", "value", "item", "instance", "obj"];

    private static readonly string[] MemberNames =
        ["DoWork", "Transform", "Compute", "Execute", "Process",
         "Length", "Count", "IsEmpty", "Name", "Status"];

    private static readonly string[] ReturnTypes =
        ["int", "string", "bool", "double"];

    private static readonly string[] ParameterTypes =
        ["int", "string", "bool", "double"];

    private static Gen<DuplicateSignatureScenario> GenScenario()
    {
        return from receiverType in Gen.Elements(ReceiverTypeNames)
               from receiverParam1 in Gen.Elements(ReceiverParamNames)
               from receiverParam2 in Gen.Elements(ReceiverParamNames)
               from memberKind in Gen.Elements(DuplicateMemberKind.Method, DuplicateMemberKind.Property)
               from memberName in Gen.Elements(MemberNames)
               from returnType in Gen.Elements(ReturnTypes)
               from paramCount in Gen.Choose(0, 3)
               select new DuplicateSignatureScenario(
                   receiverType, receiverParam1, receiverParam2,
                   memberKind, memberName, returnType, paramCount);
    }

    public static Arbitrary<DuplicateSignatureScenario> ArbitraryDuplicateSignatureScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeReferenceNode MakeNamedTypeRef(string name) =>
        new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
        } };

    private static TypeReferenceNode MakeReturnTypeRef(string typeName) =>
        typeName == "void"
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
            : MakeNamedTypeRef(typeName);

    private static ExpressionNode MakeLiteralExpression(string typeName)
    {
        var literal = typeName switch
        {
            "bool" => (PrimaryNode)new TrueLiteralExpressionPrimaryNode(),
            "string" => new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"x\"") },
            _ => new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") },
        };

        var postfix = new PostfixNode
        {
            Target = literal,
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

    private static ParameterNode MakeParameter(string name, string typeName) =>
        new()
        {
            Name = Tok(name),
            Type = MakeNamedTypeRef(typeName),
        };

    private static ParameterListNode? MakeParameterList(int parameterCount)
    {
        if (parameterCount == 0)
            return null;

        var first = MakeParameter("p0", ParameterTypes[0]);
        var rest = Enumerable.Range(1, parameterCount - 1)
            .Select(i => MakeParameter($"p{i}", ParameterTypes[i % ParameterTypes.Length]))
            .ToImmutableArray();

        return new ParameterListNode
        {
            Parameter = first,
            Parameters = rest,
        };
    }

    private static ExtensionMemberDeclarationNode MakeMethodMember(
        string name, string returnType, int parameterCount)
    {
        var body = returnType == "void"
            ? new BlockNode { Statements = ImmutableArray<StatementNode>.Empty }
            : new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode
                        {
                            Value = MakeLiteralExpression(returnType),
                        },
                    }),
            };

        var methodDecl = new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = MakeReturnTypeRef(returnType),
            Parameters = MakeParameterList(parameterCount),
            Body = new MethodBlockBodyMethodBodyNode { Block = body },
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };

        return new ExtensionMethodMemberExtensionMemberDeclarationNode
        {
            Method = methodDecl,
        };
    }

    private static ExtensionMemberDeclarationNode MakePropertyMember(
        string name, string returnType)
    {
        var property = new ExpressionBodiedPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            Type = MakeNamedTypeRef(returnType),
            Name = Tok(name),
            Body = MakeLiteralExpression(returnType),
        };

        return new ExtensionPropertyMemberExtensionMemberDeclarationNode
        {
            Property = property,
        };
    }

    private static CompilationUnitNode BuildAst(DuplicateSignatureScenario scenario)
    {
        // Declare the receiver type as a class so it can be resolved
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.ReceiverTypeName),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var classTypeDecl = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl };

        // Build the duplicate member for each extension block
        var member1 = scenario.MemberKind == DuplicateMemberKind.Method
            ? MakeMethodMember(scenario.MemberName, scenario.ReturnType, scenario.ParameterCount)
            : MakePropertyMember(scenario.MemberName, scenario.ReturnType);

        var member2 = scenario.MemberKind == DuplicateMemberKind.Method
            ? MakeMethodMember(scenario.MemberName, scenario.ReturnType, scenario.ParameterCount)
            : MakePropertyMember(scenario.MemberName, scenario.ReturnType);

        // First extension block
        var extDecl1 = new ExtensionBlockDeclarationNode
        {
            ReceiverType = MakeNamedTypeRef(scenario.ReceiverTypeName),
            ReceiverName = Tok(scenario.ReceiverParamName1),
            Members = ImmutableArray.Create(member1),
        };

        // Second extension block with the same member signature
        var extDecl2 = new ExtensionBlockDeclarationNode
        {
            ReceiverType = MakeNamedTypeRef(scenario.ReceiverTypeName),
            ReceiverName = Tok(scenario.ReceiverParamName2),
            Members = ImmutableArray.Create(member2),
        };

        var extItem1 = new ExtensionItemTopLevelItemNode
        {
            ExtensionDeclaration = extDecl1,
        };

        var extItem2 = new ExtensionItemTopLevelItemNode
        {
            ExtensionDeclaration = extDecl2,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                classTypeDecl.AsTopLevelItem(),
                extItem1,
                extItem2),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Duplicate Signature Detection
    //
    // **Validates: Requirements 8.9, 20.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any two extension blocks on the same receiver type T that declare members
    /// with identical names and parameter type lists, the semantic model builder
    /// SHALL emit diagnostic <c>MIST_SB_0073</c>.
    ///
    /// **Validates: Requirements 8.9, 20.4**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExtensionDuplicateSignaturePropertyTests) },
        MaxTest = 100)]
    public Property TwoExtensionBlocks_WithIdenticalMemberSignature_EmitsMIST_SB_0073(
        DuplicateSignatureScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var hasExpectedDiagnostic = diagnostics.Any(d =>
            d.Id.Value == "MIST_SB_0073" &&
            d.Severity == DiagnosticSeverity.Error);

        return hasExpectedDiagnostic.ToProperty()
            .Label($"Expected MIST_SB_0073 diagnostic for {scenario}. " +
                   $"Got diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}:{d.Message}"))}]");
    }
}
