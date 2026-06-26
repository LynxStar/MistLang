// Feature: ref-parameters, Property 9: Semantic Model RefKind Recording
// **Validates: Requirements 7.1, 7.2, 7.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for semantic model RefKind recording.
///
/// Property 9: Semantic Model RefKind Recording
/// For any ref-annotated parameter, method return type, or local variable declaration,
/// the semantic model SHALL record the correct RefKind value (Ref or RefReadonly)
/// on the corresponding symbol.
///
/// **Validates: Requirements 7.1, 7.2, 7.3**
/// </summary>
public class RefKindRecordingPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token MakeToken(string lexeme)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation()
    {
        var sourceText = new SourceText("test.mist", "");
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private static ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static ParameterNode MakeRefParameter(string typeName, string paramName)
    {
        return new ParameterNode
        {
            RefModifier = MakeToken("ref"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = MakePrimitiveTypeNode(typeName),
                }
            },
            Name = MakeToken(paramName),
        };
    }

    private static ParameterNode MakeRefReadonlyParameter(string typeName, string paramName)
    {
        return new ParameterNode
        {
            Modifier = MakeToken("readonly"),
            RefModifier = MakeToken("ref"),
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = MakePrimitiveTypeNode(typeName),
                }
            },
            Name = MakeToken(paramName),
        };
    }

    private static ParameterNode MakeNonRefParameter(string typeName, string paramName)
    {
        return new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = MakePrimitiveTypeNode(typeName),
                }
            },
            Name = MakeToken(paramName),
        };
    }

    private static PrimitiveTypeNode MakePrimitiveTypeNode(string typeName)
    {
        return typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
    }

    private static MethodDeclarationNode MakeMethodWithRefReturn(
        string name,
        RefKind returnRefKind,
        ParameterNode? parameter = null)
    {
        OwnershipSpecifierNode? returnOwnership = returnRefKind switch
        {
            RefKind.Ref => new RefSpecifierOwnershipSpecifierNode(),
            RefKind.RefReadonly => new ReadonlyRefSpecifierOwnershipSpecifierNode(),
            _ => null,
        };

        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            ReturnOwnership = returnOwnership,
            Parameters = parameter is not null
                ? new ParameterListNode { Parameter = parameter }
                : null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    private static StatementNode MakeRefLocalDeclaration(
        string typeName,
        string varName,
        RefKind refKind)
    {
        OwnershipSpecifierNode? ownershipSpecifier = refKind switch
        {
            RefKind.Ref => new RefSpecifierOwnershipSpecifierNode(),
            RefKind.RefReadonly => new ReadonlyRefSpecifierOwnershipSpecifierNode(),
            _ => null,
        };

        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                OwnershipSpecifier = ownershipSpecifier,
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = MakePrimitiveTypeNode(typeName),
                    }
                },
                Name = MakeToken(varName),
                Initializer = MistAstGenerator.WrapPrimary(
                    new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = MakeToken("0") }),
            },
        };
    }

    private static CompilationUnitNode MakeClassWithMethod(MethodDeclarationNode method)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario for testing RefKind recording on parameters.
    /// </summary>
    public record RefParameterScenario(
        string TypeName,
        string ParamName,
        RefKind ExpectedRefKind)
    {
        public override string ToString()
        {
            return $"Parameter: {(ExpectedRefKind == RefKind.RefReadonly ? "readonly ref" : "ref")} {TypeName} {ParamName}";
        }
    }

    /// <summary>
    /// Scenario for testing ReturnRefKind recording on methods.
    /// </summary>
    public record RefReturnScenario(
        string MethodName,
        RefKind ExpectedReturnRefKind)
    {
        public override string ToString()
        {
            return $"Return: {(ExpectedReturnRefKind == RefKind.RefReadonly ? "ref readonly" : "ref")} int {MethodName}()";
        }
    }

    /// <summary>
    /// Scenario for testing RefKind recording on local variables.
    /// </summary>
    public record RefLocalScenario(
        string TypeName,
        string VarName,
        RefKind ExpectedRefKind)
    {
        public override string ToString()
        {
            return $"Local: {(ExpectedRefKind == RefKind.RefReadonly ? "ref readonly" : "ref")} {TypeName} {VarName}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "double", "float", "char", "long", "string"];

    private static readonly string[] ParamNames =
        ["x", "y", "z", "value", "input", "target", "arg", "item"];

    private static readonly string[] MethodNames =
        ["GetRef", "Lookup", "Access", "Find", "Retrieve", "Fetch"];

    private static readonly string[] VarNames =
        ["a", "b", "c", "local", "temp", "alias", "ptr", "handle"];

    public static Arbitrary<RefParameterScenario> ArbitraryRefParameterScenarios()
    {
        var gen =
            from typeName in Gen.Elements(PrimitiveTypes)
            from paramName in Gen.Elements(ParamNames)
            from refKind in Gen.Elements(RefKind.Ref, RefKind.RefReadonly)
            select new RefParameterScenario(typeName, paramName, refKind);

        return gen.ToArbitrary();
    }

    public static Arbitrary<RefReturnScenario> ArbitraryRefReturnScenarios()
    {
        var gen =
            from methodName in Gen.Elements(MethodNames)
            from refKind in Gen.Elements(RefKind.Ref, RefKind.RefReadonly)
            select new RefReturnScenario(methodName, refKind);

        return gen.ToArbitrary();
    }

    public static Arbitrary<RefLocalScenario> ArbitraryRefLocalScenarios()
    {
        // Only bare `ref` is a ref local. `readonly ref` on locals is an ownership lend
        // (ReadonlyRef tier) until the grammar supports disambiguation via ref on initializer.
        var gen =
            from typeName in Gen.Elements(PrimitiveTypes)
            from varName in Gen.Elements(VarNames)
            select new RefLocalScenario(typeName, varName, RefKind.Ref);

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9a: Parameter RefKind Recording
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any ref-annotated parameter declaration, the semantic model SHALL record
    /// the correct RefKind value (Ref or RefReadonly) on the parameter symbol.
    ///
    /// **Validates: Requirements 7.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(RefKindRecordingPropertyTests) }, MaxTest = 100)]
    public bool RefParameter_RecordsCorrectRefKind(RefParameterScenario scenario)
    {
        // Arrange: build a method with a ref parameter
        var param = scenario.ExpectedRefKind == RefKind.RefReadonly
            ? MakeRefReadonlyParameter(scenario.TypeName, scenario.ParamName)
            : MakeRefParameter(scenario.TypeName, scenario.ParamName);

        var method = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = MakeClassWithMethod(method);
        var model = BuildModel(ast);

        // Act: find the parameter symbol
        var paramSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == scenario.ParamName && s.Kind == SymbolKind.Parameter);

        if(paramSymbol is null)
        {
            return false;
        }

        // Assert: RefKind matches expected
        return paramSymbol.RefKind == scenario.ExpectedRefKind;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9b: Method Return RefKind Recording
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any method with a ref or ref readonly return type, the semantic model SHALL
    /// record the correct ReturnRefKind value on the method symbol.
    ///
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(RefKindRecordingPropertyTests) }, MaxTest = 100)]
    public bool RefReturn_RecordsCorrectReturnRefKind(RefReturnScenario scenario)
    {
        // Arrange: build a method with a ref return type
        var method = MakeMethodWithRefReturn(scenario.MethodName, scenario.ExpectedReturnRefKind);
        var ast = MakeClassWithMethod(method);
        var model = BuildModel(ast);

        // Act: find the method symbol
        var methodSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == scenario.MethodName && s.Kind == SymbolKind.Method);

        if(methodSymbol is null)
        {
            return false;
        }

        // Assert: ReturnRefKind matches expected
        return methodSymbol.ReturnRefKind == scenario.ExpectedReturnRefKind;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9c: Local Variable RefKind Recording
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any ref or ref readonly local variable declaration, the semantic model SHALL
    /// record the correct RefKind value on the local variable symbol.
    ///
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(RefKindRecordingPropertyTests) }, MaxTest = 100)]
    public bool RefLocal_RecordsCorrectRefKind(RefLocalScenario scenario)
    {
        // Arrange: build a method containing a ref local declaration
        var localDecl = MakeRefLocalDeclaration(
            scenario.TypeName, scenario.VarName, scenario.ExpectedRefKind);

        var method = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(localDecl),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = MakeClassWithMethod(method);
        var model = BuildModel(ast);

        // Act: find the local variable symbol
        var localSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == scenario.VarName && s.Kind == SymbolKind.LocalVariable);

        if(localSymbol is null)
        {
            return false;
        }

        // Assert: RefKind matches expected
        return localSymbol.RefKind == scenario.ExpectedRefKind;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9d: Non-ref declarations have RefKind.None
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any parameter without a ref modifier, the semantic model SHALL record
    /// RefKind.None on the parameter symbol.
    ///
    /// **Validates: Requirements 7.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(RefKindRecordingPropertyTests) }, MaxTest = 100)]
    public bool NonRefParameter_RecordsRefKindNone(RefParameterScenario scenario)
    {
        // Arrange: build a method with a non-ref parameter (reuse scenario for type/name)
        var param = MakeNonRefParameter(scenario.TypeName, scenario.ParamName);

        var method = new MethodDeclarationNode
        {
            Name = MakeToken("TestMethod"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var ast = MakeClassWithMethod(method);
        var model = BuildModel(ast);

        // Act: find the parameter symbol
        var paramSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == scenario.ParamName && s.Kind == SymbolKind.Parameter);

        if(paramSymbol is null)
        {
            return false;
        }

        // Assert: RefKind is None
        return paramSymbol.RefKind == RefKind.None;
    }
}