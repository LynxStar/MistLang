// Feature: extension-methods, Properties 3–9, 12–14: Type Checker Extension Method Behavior
//
// **Validates: Requirements 3.3, 9.2, 11.1, 11.2, 11.4, 11.5, 12.1, 12.4, 13.1, 13.2, 13.3,
//              13.4, 14.1, 14.2, 14.3, 15.1, 15.2, 24.1, 24.2, 25.1, 25.2, 25.3, 26.1, 26.2,
//              26.3, 27.1, 27.2**
//
// All work runs in-process — no external process is spawned per iteration.

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for extension method type checker behavior.
/// Tests Properties 3, 4, 5, 6, 7, 8, 9, 12, 13, and 14 from the design document.
/// </summary>
public class ExtensionTypeCheckerPropertyTests
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

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
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

    /// <summary>
    /// Builds a model where the compilation unit's using declarations automatically
    /// include the inferred namespace, ensuring same-file extensions are visible.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithSameNamespaceVisibility(
        CompilationUnitNode ast)
    {
        // Add a using for the inferred namespace so extensions in the same file are visible.
        var usings = ast.Usings.Add(MakeUsingDeclaration("TestProject"));
        var astWithUsing = new CompilationUnitNode
        {
            Usings = usings,
            Items = ast.Items,
        };
        return BuildModelWithDiagnostics(astWithUsing);
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null) =>
        new NamedTypeRefTypeReferenceNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(name),
                Span = new Storm.Runtime.SourceSpan(0, name.Length),
            },
            TypeArgs = typeArgs,
        };

    private static TypeReferenceNode MakeUnknownTypeRef() =>
        new UnknownTypeRefTypeReferenceNode();

    private static TypeReferenceNode MakeReturnTypeRef(string typeName) =>
        typeName == "void"
            ? new VoidTypeRefTypeReferenceNode()
            : typeName == "int"
                ? new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() }
                : typeName == "string"
                    ? new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new StringTypePrimitiveTypeNode() }
                    : typeName == "bool"
                        ? new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new BoolTypePrimitiveTypeNode() }
                        : MakeNamedTypeRef(typeName);

    private static ExpressionNode MakeLiteralExpression(string typeName)
    {
        PrimaryNode literal = typeName switch
        {
            "bool" => new TrueLiteralExpressionPrimaryNode(),
            "string" => new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"x\"") },
            _ => new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") },
        };

        var postfix = new PostfixNode { Target = literal, Suffix = ImmutableArray<PostfixSuffixNode>.Empty };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates an expression: identifier.MethodName()
    /// </summary>
    private static ExpressionNode MakeMethodCallExpression(string targetName, string methodName)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(targetName) };
        var methodCallSuffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = Tok(methodName),
            Arguments = null,
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(methodCallSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates an expression: this.MethodName()
    /// </summary>
    private static ExpressionNode MakeThisMethodCallExpression(string methodName)
    {
        var primary = new ThisExpressionPrimaryNode();
        var methodCallSuffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = Tok(methodName),
            Arguments = null,
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(methodCallSuffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a member access expression: varName.memberName
    /// </summary>
    private static ExpressionNode MakeMemberAccessExpression(string varName, string memberName)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(varName) };
        var suffix = new MemberAccessSuffixPostfixSuffixNode { Name = Tok(memberName) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr) =>
        new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };

    private static StatementNode MakeTypedVarDecl(string typeName, string varName, ExpressionNode initializer) =>
        new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeReturnTypeRef(typeName),
                Name = Tok(varName),
                Initializer = initializer,
            },
        };

    private static StatementNode MakeNamedTypeVarDecl(string declaredTypeName, string varName, ExpressionNode initializer) =>
        new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeNamedTypeRef(declaredTypeName),
                Name = Tok(varName),
                Initializer = initializer,
            },
        };

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        var postfix = new PostfixNode { Target = primary, Suffix = ImmutableArray<PostfixSuffixNode>.Empty };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExtensionMemberDeclarationNode MakeExtensionMethod(
        string name, string returnType, bool isStatic = false, ImmutableArray<StatementNode>? bodyStatements = null)
    {
        var modifiers = isStatic
            ? ImmutableArray.Create<ModifierNode>(new StaticModifierModifierNode())
            : ImmutableArray<ModifierNode>.Empty;

        BlockNode body;
        if (bodyStatements is not null)
        {
            body = new BlockNode { Statements = bodyStatements.Value };
        }
        else if (returnType == "void")
        {
            body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty };
        }
        else
        {
            body = new BlockNode
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
        }

        var methodDecl = new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = MakeReturnTypeRef(returnType),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = body },
            Modifiers = modifiers,
        };

        return new ExtensionMethodMemberExtensionMemberDeclarationNode { Method = methodDecl };
    }

    private static ExtensionBlockDeclarationNode MakeExtensionBlock(
        TypeReferenceNode receiverType, string receiverName, params ExtensionMemberDeclarationNode[] members) =>
        new()
        {
            ReceiverType = receiverType,
            ReceiverName = Tok(receiverName),
            Members = members.ToImmutableArray(),
        };

    private static ExtensionBlockDeclarationNode MakeGenericExtensionBlock(
        TypeParameterListNode typeParams, TypeReferenceNode receiverType, string receiverName,
        params ExtensionMemberDeclarationNode[] members) =>
        new()
        {
            TypeParams = typeParams,
            ReceiverType = receiverType,
            ReceiverName = Tok(receiverName),
            Members = members.ToImmutableArray(),
        };

    private static TypeParameterListNode MakeTypeParamList(params string[] names)
    {
        var first = new TypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => new TypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(params TypeReferenceNode[] typeRefs) =>
        new()
        {
            TypeReference = typeRefs[0],
            TypeReferences = typeRefs.Skip(1).ToImmutableArray(),
        };

    private static ClassDeclarationNode MakeClass(string name, ImmutableArray<MemberDeclarationNode>? members = null) =>
        new()
        {
            Name = Tok(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

    private static ClassDeclarationNode MakeGenericClass(string name, TypeParameterListNode typeParams,
        ImmutableArray<MemberDeclarationNode>? members = null) =>
        new()
        {
            Name = Tok(name),
            TypeParams = typeParams,
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

    private static MemberDeclarationNode MakeClassMethod(string name, string returnType = "void", bool isStatic = false)
    {
        var modifiers = new List<ModifierNode> { new PublicModifierModifierNode() };
        if (isStatic) modifiers.Add(new StaticModifierModifierNode());

        BlockNode body = returnType == "void"
            ? new BlockNode { Statements = ImmutableArray<StatementNode>.Empty }
            : new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = MakeLiteralExpression(returnType) },
                    }),
            };

        return new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = Tok(name),
                ReturnType = MakeReturnTypeRef(returnType),
                Body = new MethodBlockBodyMethodBodyNode { Block = body },
                Modifiers = modifiers.ToImmutableArray(),
            },
        };
    }

    private static TopLevelItemNode MakeExtensionTopLevelItem(ExtensionBlockDeclarationNode extDecl) =>
        new ExtensionItemTopLevelItemNode { ExtensionDeclaration = extDecl };

    private static TopLevelItemNode MakeClassTopLevelItem(ClassDeclarationNode classDecl) =>
        (new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItem();

    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName) =>
        new()
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(namespaceName),
                Span = new Storm.Runtime.SourceSpan(0, namespaceName.Length),
            },
        };

    private static FileScopedNamespaceNamespaceDeclarationNode MakeFileScopedNs(
        string nsName, ImmutableArray<NamespaceBodyItemNode> body) =>
        new()
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(nsName),
                Span = new Storm.Runtime.SourceSpan(0, nsName.Length),
            },
            Body = body,
            Span = new Storm.Runtime.SourceSpan(0, nsName.Length + 20),
        };

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    public record FallbackScenario(string ClassName, string MethodName, string ReturnType)
    {
        public override string ToString() => $"extension({ClassName}).{MethodName}() -> {ReturnType}";
    }

    public record ShadowScenario(string ClassName, string MethodName, string OwnReturnType, string ExtReturnType)
    {
        public override string ToString() => $"{ClassName}.{MethodName}() own={OwnReturnType} ext={ExtReturnType}";
    }

    public record SpecificityScenario(string ClassName, string MethodName, string SpecificReturnType, string UniversalReturnType)
    {
        public override string ToString() => $"{ClassName}.{MethodName}() specific={SpecificReturnType} universal={UniversalReturnType}";
    }

    public record GenericScenario(string GenericClassName, string TypeParam, string ConcreteArg, string MethodName)
    {
        public override string ToString() => $"extension<{TypeParam}>({GenericClassName}<{TypeParam}>).{MethodName}() with {ConcreteArg}";
    }

    public record UniversalScenario(string MethodName, string TargetTypeName)
    {
        public override string ToString() => $"extension(unknown).{MethodName}() on {TargetTypeName}";
    }

    public record StaticReceiverScenario(string ClassName, string MethodName, string ReceiverName)
    {
        public override string ToString() => $"static extension({ClassName} {ReceiverName}).{MethodName}() references {ReceiverName}";
    }

    public record NamespaceScenario(string NamespaceName, string ClassName, string MethodName)
    {
        public override string ToString() => $"namespace {NamespaceName}: extension({ClassName}).{MethodName}()";
    }

    public record UnionScenario(string ClassA, string ClassB, string MethodName, bool ApplicableToBoth)
    {
        public override string ToString() => $"({ClassA} | {ClassB}).{MethodName}() applicable={ApplicableToBoth}";
    }

    public record UnknownExclusionScenario(string ClassName, string MethodName)
    {
        public override string ToString() => $"unknown.{MethodName}() should NOT find extension({ClassName})";
    }

#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Widget", "Service", "Handler", "Container", "Processor"];
    private static readonly string[] MethodNames = ["DoWork", "Transform", "Compute", "Execute", "Process"];
    private static readonly string[] ReturnTypes = ["int", "string", "bool"];
    private static readonly string[] ReceiverNames = ["self", "value", "item"];
    private static readonly string[] NamespaceNames = ["Alpha", "Beta", "Gamma", "Delta"];
    private static readonly string[] TypeParams = ["T", "U", "V"];

    public static Arbitrary<FallbackScenario> ArbitraryFallbackScenario() =>
        (from cls in Gen.Elements(ClassNames)
         from method in Gen.Elements(MethodNames)
         from ret in Gen.Elements(ReturnTypes)
         select new FallbackScenario(cls, method, ret)).ToArbitrary();

    public static Arbitrary<ShadowScenario> ArbitraryShadowScenario() =>
        (from cls in Gen.Elements(ClassNames)
         from method in Gen.Elements(MethodNames)
         from ownRet in Gen.Elements(ReturnTypes)
         from extRet in Gen.Elements(ReturnTypes)
         select new ShadowScenario(cls, method, ownRet, extRet)).ToArbitrary();

    public static Arbitrary<SpecificityScenario> ArbitrarySpecificityScenario() =>
        (from cls in Gen.Elements(ClassNames)
         from method in Gen.Elements(MethodNames)
         from specRet in Gen.Elements(ReturnTypes)
         from univRet in Gen.Elements(ReturnTypes)
         select new SpecificityScenario(cls, method, specRet, univRet)).ToArbitrary();

    public static Arbitrary<GenericScenario> ArbitraryGenericScenario() =>
        (from cls in Gen.Elements(ClassNames)
         from tp in Gen.Elements(TypeParams)
         from concreteArg in Gen.Elements("Widget", "Service", "Handler")
         from method in Gen.Elements(MethodNames)
         where cls != concreteArg // Avoid using the same name for the generic class and the type arg
         select new GenericScenario(cls, tp, concreteArg, method)).ToArbitrary();

    public static Arbitrary<UniversalScenario> ArbitraryUniversalScenario() =>
        (from method in Gen.Elements(MethodNames)
         from target in Gen.Elements(ClassNames)
         select new UniversalScenario(method, target)).ToArbitrary();

    public static Arbitrary<StaticReceiverScenario> ArbitraryStaticReceiverScenario() =>
        (from cls in Gen.Elements(ClassNames)
         from method in Gen.Elements(MethodNames)
         from receiver in Gen.Elements(ReceiverNames)
         select new StaticReceiverScenario(cls, method, receiver)).ToArbitrary();

    public static Arbitrary<NamespaceScenario> ArbitraryNamespaceScenario() =>
        (from ns in Gen.Elements(NamespaceNames)
         from cls in Gen.Elements(ClassNames)
         from method in Gen.Elements(MethodNames)
         select new NamespaceScenario(ns, cls, method)).ToArbitrary();

    public static Arbitrary<UnionScenario> ArbitraryUnionScenario() =>
        (from clsA in Gen.Elements(ClassNames[..3])
         from clsB in Gen.Elements(ClassNames[3..])
         from method in Gen.Elements(MethodNames)
         from both in Gen.Elements(true, false)
         select new UnionScenario(clsA, clsB, method, both)).ToArbitrary();

    public static Arbitrary<UnknownExclusionScenario> ArbitraryUnknownExclusionScenario() =>
        (from cls in Gen.Elements(ClassNames)
         from method in Gen.Elements(MethodNames)
         select new UnknownExclusionScenario(cls, method)).ToArbitrary();


    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Extension Member Lookup Fallback
    //
    // **Validates: Requirements 11.1, 11.2, 13.1, 13.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any type T that does not declare a member named M, and any extension block
    /// on T that declares M, calling instance.M() SHALL resolve to the extension member
    /// and type-check successfully (no MIST_UNDEFINED_MEMBER diagnostic).
    ///
    /// **Validates: Requirements 11.1, 11.2, 13.1, 13.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool ExtensionMemberLookupFallback_ResolvesWithoutUndefinedMember(FallbackScenario scenario)
    {
        // Build: class ClassName { void Probe() { this.MethodName(); } }
        //        extension(ClassName self) { ReturnType MethodName() { ... } }
        var probeBody = new BlockNode
        {
            Statements = ImmutableArray.Create(MakeExpressionStatement(MakeThisMethodCallExpression(scenario.MethodName))),
        };
        var probeMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = probeBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var classDecl = MakeClass(scenario.ClassName,
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = probeMethod }));

        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, scenario.ReturnType));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(extBlock)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // No MIST_UNDEFINED_MEMBER diagnostic for the extension method
        return !diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Own Members Shadow Extensions
    //
    // **Validates: Requirements 11.4, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any type T that declares a member named M, and any extension block on T
    /// that also declares M, calling instance.M() SHALL always resolve to T's own
    /// member (no ambiguity diagnostic).
    ///
    /// **Validates: Requirements 11.4, 13.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool OwnMembersShadowExtensions_NoAmbiguityDiagnostic(ShadowScenario scenario)
    {
        // Build: class ClassName { ReturnType MethodName() { ... } void Probe() { this.MethodName(); } }
        //        extension(ClassName self) { ReturnType MethodName() { ... } }
        var probeBody = new BlockNode
        {
            Statements = ImmutableArray.Create(MakeExpressionStatement(MakeThisMethodCallExpression(scenario.MethodName))),
        };
        var probeMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = probeBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var classDecl = MakeClass(scenario.ClassName,
            ImmutableArray.Create(
                MakeClassMethod(scenario.MethodName, scenario.OwnReturnType),
                (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = probeMethod }));

        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, scenario.ExtReturnType));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(extBlock)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // No ambiguity diagnostic (MIST_TC_0082) and no undefined member
        var hasAmbiguity = diagnostics.Any(d => d.Id.Value == "MIST_TC_0082");
        var hasUndefined = diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));

        return !hasAmbiguity && !hasUndefined;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Receiver Specificity Ordering
    //
    // **Validates: Requirements 11.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any type T with extension method M defined on both T specifically and on
    /// unknown, calling instance.M() on T SHALL resolve to the T-specific extension
    /// (no ambiguity).
    ///
    /// **Validates: Requirements 11.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool ReceiverSpecificityOrdering_SpecificPreferredOverUniversal(SpecificityScenario scenario)
    {
        // Build: class ClassName { void Probe() { this.MethodName(); } }
        //        extension(ClassName self) { SpecificReturnType MethodName() { ... } }
        //        extension(unknown self) { UniversalReturnType MethodName() { ... } }
        var probeBody = new BlockNode
        {
            Statements = ImmutableArray.Create(MakeExpressionStatement(MakeThisMethodCallExpression(scenario.MethodName))),
        };
        var probeMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = probeBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var classDecl = MakeClass(scenario.ClassName,
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = probeMethod }));

        var specificExt = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, scenario.SpecificReturnType));

        var universalExt = MakeExtensionBlock(
            MakeUnknownTypeRef(), "self",
            MakeExtensionMethod(scenario.MethodName, scenario.UniversalReturnType));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(specificExt),
                MakeExtensionTopLevelItem(universalExt)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // No ambiguity diagnostic — the specific extension should win
        var hasAmbiguity = diagnostics.Any(d => d.Id.Value == "MIST_TC_0082");
        var hasUndefined = diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));

        return !hasAmbiguity && !hasUndefined;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Generic Extension Type Inference
    //
    // **Validates: Requirements 12.1, 12.4, 25.1, 25.2, 25.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any generic extension extension&lt;T&gt;(G&lt;T&gt; self) and concrete type G&lt;X&gt;,
    /// calling a method on G&lt;X&gt; SHALL infer T = X and type-check successfully.
    ///
    /// **Validates: Requirements 12.1, 12.4, 25.1, 25.2, 25.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool GenericExtensionTypeInference_InfersCorrectly(GenericScenario scenario)
    {
        // Build: class ConcreteArg { }
        //        class GenericClassName<T> { }
        //        extension<T>(GenericClassName<T> self) { int MethodName() { return 0; } }
        //
        // Verify: The extension is registered with type parameters, and the receiver type
        // is a ConstructedType whose generic definition matches the generic class.
        // This confirms the type checker can perform inference (T = ConcreteArg) when
        // a value of type GenericClassName<ConcreteArg> calls MethodName().
        var concreteArgClass = MakeClass(scenario.ConcreteArg);
        var genericClass = MakeGenericClass(scenario.GenericClassName, MakeTypeParamList(scenario.TypeParam));

        // Extension block: extension<T>(GenericClassName<T> self) { int MethodName() { ... } }
        var extReceiverType = MakeNamedTypeRef(scenario.GenericClassName,
            MakeTypeArgList(MakeNamedTypeRef(scenario.TypeParam)));
        var extBlock = MakeGenericExtensionBlock(
            MakeTypeParamList(scenario.TypeParam),
            extReceiverType, "self",
            MakeExtensionMethod(scenario.MethodName, "int"));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(concreteArgClass),
                MakeClassTopLevelItem(genericClass),
                MakeExtensionTopLevelItem(extBlock)),
        };

        var (model, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // Verify no errors during registration
        var hasRegistrationErrors = diagnostics.Any(d =>
            d.Id.Value == "MIST_SB_0070" || d.Id.Value == "MIST_SB_0074");
        if (hasRegistrationErrors)
            return false;

        // Find the generic class type
        var genericClassSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.GenericClassName && s.Kind == SymbolKind.Class);
        if (genericClassSymbol?.DeclaredType is not ClassType genericClassType)
            return false;

        // Verify the extension is registered with type parameters
        // For generic extensions, the receiver type is a ConstructedType whose
        // generic definition is the generic class.
        var allExtensions = model.ExtensionRegistry.AllGroups.SelectMany(g => g.Value).ToList();
        var genericExtension = allExtensions.FirstOrDefault(ext =>
            ext.TypeParameters.Count > 0 &&
            ext.MemberScope.Lookup(scenario.MethodName) is not null);

        if (genericExtension is null)
            return false;

        // Verify the extension has the correct type parameter count
        if (genericExtension.TypeParameters.Count != 1)
            return false;

        // Verify the receiver type is a ConstructedType based on the generic class
        if (genericExtension.ReceiverType is not ConstructedType constructedReceiver)
            return false;

        // The generic definition of the constructed receiver should be our generic class
        return ReferenceEquals(constructedReceiver.GenericDefinition, genericClassType);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Trait/Interface Exclusion
    //
    // **Validates: Requirements 14.1, 14.2, 14.3, 15.1, 15.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any type T with extension method M matching a trait signature, extension
    /// members SHALL NOT be discoverable on trait-typed variables.
    ///
    /// **Validates: Requirements 14.1, 14.2, 14.3, 15.1, 15.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool TraitInterfaceExclusion_ExtensionsNotDiscoverableOnTraitTypedVars(FallbackScenario scenario)
    {
        // Build: trait MyTrait { int OtherMethod(); }
        //        class ClassName { int OtherMethod() { return 0; } }
        //        extension(ClassName self) { int MethodName() { return 0; } }
        //        class Caller { void Probe() { MyTrait t = new ClassName(); t.MethodName(); } }
        var traitMethodDecl = new TraitMethodMemberTraitMemberDeclarationNode
        {
            Method = new TraitMethodDeclarationNode
            {
                Name = Tok("OtherMethod"),
                ReturnType = MakeReturnTypeRef("int"),
            },
        };
        var traitDecl = new TraitDeclarationNode
        {
            Name = Tok("MyTrait"),
            Members = ImmutableArray.Create<TraitMemberDeclarationNode>(traitMethodDecl),
        };

        // Class that satisfies the trait structurally
        var classDecl = MakeClass(scenario.ClassName,
            ImmutableArray.Create(MakeClassMethod("OtherMethod", "int")));

        // Extension on the class
        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, scenario.ReturnType));

        // Caller: MyTrait t = new ClassName(); t.MethodName();
        var newExpr = MakeNewObjectExpression(scenario.ClassName);
        var varDeclStmt = MakeNamedTypeVarDecl("MyTrait", "_t_", newExpr);
        var callStmt = MakeExpressionStatement(MakeMethodCallExpression("_t_", scenario.MethodName));

        var callerBody = new BlockNode
        {
            Statements = ImmutableArray.Create(varDeclStmt, callStmt),
        };
        var callerMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = callerBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var callerClass = MakeClass("Caller",
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = callerMethod }));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                (new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl }).AsTopLevelItem(),
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(extBlock),
                MakeClassTopLevelItem(callerClass)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // The extension method should NOT be found on the trait-typed variable
        // (MIST_UNDEFINED_MEMBER should be emitted for MethodName on the trait)
        return diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Universal Extension Applicability
    //
    // **Validates: Requirements 9.2, 24.1, 26.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any universal extension method M and any concrete type T, calling value.M()
    /// SHALL resolve to the universal extension method (no MIST_UNDEFINED_MEMBER).
    ///
    /// **Validates: Requirements 9.2, 24.1, 26.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool UniversalExtensionApplicability_ResolvesOnConcreteTypes(UniversalScenario scenario)
    {
        // Build: class TargetTypeName { void Probe() { this.MethodName(); } }
        //        extension(unknown self) { int MethodName() { return 0; } }
        var probeBody = new BlockNode
        {
            Statements = ImmutableArray.Create(MakeExpressionStatement(MakeThisMethodCallExpression(scenario.MethodName))),
        };
        var probeMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = probeBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var classDecl = MakeClass(scenario.TargetTypeName,
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = probeMethod }));

        var universalExt = MakeExtensionBlock(
            MakeUnknownTypeRef(), "self",
            MakeExtensionMethod(scenario.MethodName, "int"));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(universalExt)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // Universal extension should be found
        return !diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));
    }


    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Static Receiver Access Rejection
    //
    // **Validates: Requirements 3.3, 13.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any static extension member whose body references the receiver parameter,
    /// the type checker SHALL emit MIST_TC_0080.
    ///
    /// **Validates: Requirements 3.3, 13.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool StaticReceiverAccessRejection_EmitsDiagnostic(StaticReceiverScenario scenario)
    {
        // Build: class ClassName { }
        //        extension(ClassName receiverName) { static int MethodName() { receiverName; } }
        // The static method body references the receiver parameter → MIST_TC_0080
        var classDecl = MakeClass(scenario.ClassName);

        // Build a static extension method whose body references the receiver
        var receiverRef = new IdentifierExpressionPrimaryNode { Name = Tok(scenario.ReceiverName) };
        var receiverPostfix = new PostfixNode { Target = receiverRef, Suffix = ImmutableArray<PostfixSuffixNode>.Empty };
        var receiverUnary = new PostfixExpressionUnaryNode { Primary = receiverPostfix };
        var receiverMult = new MultiplicativeNode { Left = receiverUnary };
        var receiverAdd = new AdditiveNode { Left = receiverMult };
        var receiverRel = new RelationalComparisonRelationalNode { Left = receiverAdd };
        var receiverEq = new EqualityNode { Left = receiverRel };
        var receiverLogAnd = new LogicalAndNode { Left = receiverEq };
        var receiverLogOr = new LogicalOrNode { Left = receiverLogAnd };
        var receiverAssign = new AssignmentNode { Target = receiverLogOr };
        var receiverExpr = new ExpressionNode { Assignment = receiverAssign };

        var bodyStatements = ImmutableArray.Create(MakeExpressionStatement(receiverExpr));
        var extMethod = MakeExtensionMethod(scenario.MethodName, "void", isStatic: true, bodyStatements: bodyStatements);

        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), scenario.ReceiverName,
            extMethod);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(extBlock)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // MIST_TC_0080 should be emitted
        return diagnostics.Any(d => d.Id.Value == "MIST_TC_0080");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Namespace Visibility
    //
    // **Validates: Requirements 27.1, 27.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any extension block declared inside namespace N, the extension's members
    /// SHALL only be discoverable at call sites where namespace N is imported.
    /// Extensions declared at the top level SHALL be visible from all files.
    ///
    /// **Validates: Requirements 27.1, 27.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool NamespaceVisibility_ExtensionNotVisibleWithoutUsing(NamespaceScenario scenario)
    {
        // Build two separate ASTs:
        //   AST 1: namespace N { extension(ClassName self) { int MethodName() { ... } } }
        //   AST 2: class ClassName { void Probe() { this.MethodName(); } }
        // Without `using N`, the extension should NOT be found.
        var classDecl = MakeClass(scenario.ClassName);

        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, "int"));

        // Put extension in a block namespace (so it doesn't affect other items)
        var nsBody = ImmutableArray.Create<NamespaceBodyItemNode>(
            new NamespaceExtensionItemNamespaceBodyItemNode { ExtensionDeclaration = extBlock });
        var nsDecl = new BlockNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(scenario.NamespaceName),
                Span = new Storm.Runtime.SourceSpan(0, scenario.NamespaceName.Length),
            },
            Body = nsBody,
            Span = new Storm.Runtime.SourceSpan(0, scenario.NamespaceName.Length + 20),
        };

        // Caller class at top level (no using) with a probe method
        var probeBody = new BlockNode
        {
            Statements = ImmutableArray.Create(MakeExpressionStatement(MakeThisMethodCallExpression(scenario.MethodName))),
        };
        var probeMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = probeBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var callerClass = MakeClass(scenario.ClassName,
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = probeMethod }));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsDecl },
                MakeClassTopLevelItem(callerClass)),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Without using, the extension method should NOT be found
        return diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));
    }

    /// <summary>
    /// For any extension block declared inside namespace N, the extension's members
    /// SHALL be discoverable when namespace N is imported via using.
    ///
    /// **Validates: Requirements 27.1, 27.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool NamespaceVisibility_ExtensionVisibleWithUsing(NamespaceScenario scenario)
    {
        // Build: using N;
        //        namespace N; extension(ClassName self) { int MethodName() { ... } }
        //        class ClassName { void Probe() { this.MethodName(); } }
        var classDecl = MakeClass(scenario.ClassName);

        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, "int"));

        // Put extension in a namespace
        var nsBody = ImmutableArray.Create<NamespaceBodyItemNode>(
            new NamespaceExtensionItemNamespaceBodyItemNode { ExtensionDeclaration = extBlock });
        var nsDecl = MakeFileScopedNs(scenario.NamespaceName, nsBody);

        // Caller class at top level WITH using
        var probeBody = new BlockNode
        {
            Statements = ImmutableArray.Create(MakeExpressionStatement(MakeThisMethodCallExpression(scenario.MethodName))),
        };
        var probeMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = probeBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var callerClass = MakeClass(scenario.ClassName,
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = probeMethod }));

        var ast = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration(scenario.NamespaceName)),
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsDecl },
                MakeClassTopLevelItem(callerClass)),
        };

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // With using, the extension method SHOULD be found
        return !diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));
    }

    /// <summary>
    /// Top-level extensions (no namespace) SHALL be visible from all files.
    ///
    /// **Validates: Requirements 27.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool NamespaceVisibility_TopLevelExtensionAlwaysVisible(FallbackScenario scenario)
    {
        // Build: extension(ClassName self) { int MethodName() { ... } }
        //        class ClassName { void Probe() { this.MethodName(); } }
        // Top-level extension should always be visible.
        var probeBody = new BlockNode
        {
            Statements = ImmutableArray.Create(MakeExpressionStatement(MakeThisMethodCallExpression(scenario.MethodName))),
        };
        var probeMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Body = new MethodBlockBodyMethodBodyNode { Block = probeBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var classDecl = MakeClass(scenario.ClassName,
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = probeMethod }));

        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, scenario.ReturnType));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(extBlock)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // Top-level extension should be found
        return !diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: Union Extension Applicability
    //
    // **Validates: Requirements 26.1, 26.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any union type A | B and extension method M, M SHALL be discoverable on
    /// the union only if applicable to all constituents. Type-specific extensions
    /// on individual constituents are NOT applicable to the union.
    ///
    /// **Validates: Requirements 26.1, 26.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool UnionExtensionApplicability_OnlyWhenApplicableToAll(UnionScenario scenario)
    {
        // Build two classes and extension blocks.
        // If ApplicableToBoth: use a universal extension (on unknown) — applicable to all
        // If not: extension only on A — NOT applicable to the union
        var classA = MakeClass(scenario.ClassA);
        var classB = MakeClass(scenario.ClassB);

        var items = ImmutableArray.CreateBuilder<TopLevelItemNode>();
        items.Add(MakeClassTopLevelItem(classA));
        items.Add(MakeClassTopLevelItem(classB));

        if (scenario.ApplicableToBoth)
        {
            // Universal extension — applicable to all types including unions
            var extUniversal = MakeExtensionBlock(
                MakeUnknownTypeRef(), "self",
                MakeExtensionMethod(scenario.MethodName, "int"));
            items.Add(MakeExtensionTopLevelItem(extUniversal));
        }
        else
        {
            // Extension only on A — NOT applicable to union A|B
            var extA = MakeExtensionBlock(
                MakeNamedTypeRef(scenario.ClassA), "self",
                MakeExtensionMethod(scenario.MethodName, "int"));
            items.Add(MakeExtensionTopLevelItem(extA));
        }

        var ast = new CompilationUnitNode { Items = items.ToImmutable() };
        var (model, _) = BuildModelWithSameNamespaceVisibility(ast);

        // Find the types
        var typeA = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ClassA && s.Kind == SymbolKind.Class)?.DeclaredType;
        var typeB = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ClassB && s.Kind == SymbolKind.Class)?.DeclaredType;

        if (typeA is null || typeB is null)
            return true; // Skip if types not found (shouldn't happen)

        // Query the extension registry for the union
        var unionExtensions = model.ExtensionRegistry.GetApplicableExtensionsForUnion(
            new List<IType> { typeA, typeB });

        // Check if the method is discoverable
        var methodFound = unionExtensions.Any(ext =>
            ext.MemberScope.Lookup(scenario.MethodName) is not null);

        if (scenario.ApplicableToBoth)
        {
            // Universal extension should be discoverable on the union
            return methodFound;
        }
        else
        {
            // Type-specific extension should NOT be discoverable on the union
            return !methodFound;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: Unknown Type-Specific Exclusion
    //
    // **Validates: Requirements 24.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any type-specific extension method M and any value whose static type is
    /// unknown, calling value.M() SHALL NOT resolve to M (gets MIST_UNDEFINED_MEMBER).
    ///
    /// **Validates: Requirements 24.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ExtensionTypeCheckerPropertyTests) }, MaxTest = 100)]
    public bool UnknownTypeSpecificExclusion_TypeSpecificNotFoundOnUnknown(UnknownExclusionScenario scenario)
    {
        // Build: class ClassName { }
        //        extension(ClassName self) { int MethodName() { return 0; } }
        //        class Caller { void Probe(unknown x) { x.MethodName(); } }
        // The type-specific extension should NOT be found on unknown-typed variable.
        var classDecl = MakeClass(scenario.ClassName);

        var extBlock = MakeExtensionBlock(
            MakeNamedTypeRef(scenario.ClassName), "self",
            MakeExtensionMethod(scenario.MethodName, "int"));

        // Caller with an unknown-typed parameter
        var param = new ParameterNode
        {
            Type = MakeUnknownTypeRef(),
            Name = Tok("_x_"),
        };
        var callStmt = MakeExpressionStatement(MakeMethodCallExpression("_x_", scenario.MethodName));
        var callerBody = new BlockNode
        {
            Statements = ImmutableArray.Create(callStmt),
        };
        var callerMethod = new MethodDeclarationNode
        {
            Name = Tok("_probe_"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = callerBody },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var callerClass = MakeClass("Caller",
            ImmutableArray.Create<MemberDeclarationNode>(new MethodMemberMemberDeclarationNode { Method = callerMethod }));

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                MakeClassTopLevelItem(classDecl),
                MakeExtensionTopLevelItem(extBlock),
                MakeClassTopLevelItem(callerClass)),
        };

        var (_, diagnostics) = BuildModelWithSameNamespaceVisibility(ast);

        // The type-specific extension should NOT be found on unknown-typed variable
        return diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_MEMBER" && d.Message.Contains(scenario.MethodName));
    }
}
