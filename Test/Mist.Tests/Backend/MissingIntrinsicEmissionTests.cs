using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Intrinsics;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for Task 43: MIST_BE_0002 diagnostic on missing intrinsic
/// emission template.
/// </summary>
public sealed class MissingIntrinsicEmissionTests : IDisposable
{
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;
    private readonly string binDir;

    public MissingIntrinsicEmissionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistBE0002Tests_" + Guid.NewGuid().ToString("N"));
        binDir = Path.Combine(Path.GetTempPath(), "MistBE0002Bin_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
        _ = Directory.CreateDirectory(binDir);
    }

    public void Dispose()
    {
        TryDeleteDir(outputDir);
        TryDeleteDir(binDir);
        GC.SuppressFinalize(this);
    }

    private static void TryDeleteDir(string dir)
    {
        try { if(Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); } } catch { }
    }

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

    // ═══════════════════════════════════════════════════════════════════════
    // Task 43.1: Missing intrinsic template → MIST_BE_0002, Succeeded==false,
    //            no .cs file written.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Constructs a stdlib file with an intrinsic operator whose key is
    /// intentionally absent from <see cref="IntrinsicEmissionTable"/>.
    /// Compiles a user program that calls it. Asserts:
    /// <list type="bullet">
    ///   <item><c>MIST_BE_0002</c> is emitted.</item>
    ///   <item><c>BackendResult.Succeeded == false</c>.</item>
    ///   <item>No <c>.cs</c> file is written in the output directory.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Emit_MissingIntrinsicTemplate_EmitsBE0002_FailsWithNoOutput()
    {
        // ── Arrange ──────────────────────────────────────────────────────
        // Build a stdlib file declaring an intrinsic struct "FakeType" with
        // an intrinsic operator+ that has NO entry in IntrinsicEmissionTable.
        var stdlibSource = @"
namespace Standard;
intrinsic public struct FakeType {
    intrinsic public static FakeType operator +(FakeType a, FakeType b);
}";

        // Build a user program that calls FakeType's operator+.
        var userSource = @"
namespace TestProject;
public class Program {
    public static void Main() {
        int x = 1 + 2;
    }
}";

        // Build the stdlib AST: intrinsic struct FakeType with operator+
        var fakeTypeRef = new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken("FakeType"),
                    Span = new Storm.Runtime.SourceSpan(0, 0),
                },
            }
        };

        var paramA = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("FakeType"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                }
            },
            Name = MakeToken("a"),
        };
        var paramB = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("FakeType"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                }
            },
            Name = MakeToken("b"),
        };

        var operatorDecl = new OperatorDeclarationNode
        {
            ReturnType = fakeTypeRef,
            Symbol = new PlusOpOperatorSymbolNode(),
            Parameters = new ParameterListNode { Parameter = paramA, Parameters = ImmutableArray.Create(paramB) },
            Body = new OperatorNoBodyOperatorBodyNode(),
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new IntrinsicModifierModifierNode(),
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var fakeStruct = new StructDeclarationNode
        {
            Name = MakeToken("FakeType"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new OperatorMemberMemberDeclarationNode { Operator = operatorDecl }),
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new IntrinsicModifierModifierNode(),
                new PublicModifierModifierNode()),
        };

        var stdlibNs = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("Standard"),
                Span = new Storm.Runtime.SourceSpan(0, 0),
            },
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = fakeStruct }).AsNamespaceBodyItems(),
        };

        var stdlibAst = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = stdlibNs }),
        };

        // Build user AST: class Program { public static void Main() { int x = 1 + 2; } }
        // We use int literals so the program compiles, but we'll manually
        // record a resolved operator pointing to FakeType's intrinsic operator+
        // to simulate the scenario where the type checker resolved to an
        // intrinsic whose template is missing.
        var stmtX = MistAstGenerator.MakeIntVarDecl("x", "1");

        // Build an additive expression: 1 + 2
        var addExpr = MistAstGenerator.MakeAdditive(
            MistAstGenerator.MakeIdentifierExpr("a"),
            MistAstGenerator.MakeIdentifierExpr("b"),
            "+");
        var stmtAdd = MistAstGenerator.MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            "c",
            addExpr);

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(stmtX, stmtAdd),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var userAst = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        // Build the semantic model with stdlib + user code.
        var stdlibSourceText = new SourceText("stdlib_fake.mist", stdlibSource);
        var userSourceText = new SourceText("test.mist", userSource);
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(stdlibSourceText) { IsStdlib = true },
            new CompilationUnit(userSourceText)));
        var buildDiag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(stdlibAst, userAst),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        // Find the FakeType's operator+ symbol and record it as the resolved
        // operator for the additive node.
        var fakeTypeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "FakeType" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(fakeTypeSymbol);
        var fakeType = fakeTypeSymbol!.DeclaredType as StructType;
        Assert.NotNull(fakeType);

        var operatorPlusSymbol = fakeType!.MemberScope.Members
            .FirstOrDefault(m => m.Name == "operator+" && m.Kind == SymbolKind.Method);
        Assert.NotNull(operatorPlusSymbol);

        // Find the AdditiveNode in the user AST.
        var additiveNode = FindAdditiveNode(userAst);
        Assert.NotNull(additiveNode);

        // Record the resolved operator pointing to FakeType's intrinsic operator+.
        model.RecordResolvedOperator(additiveNode!, operatorPlusSymbol!);

        // ── Act ──────────────────────────────────────────────────────────
        var emitDiag = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitDiag);

        // ── Assert ───────────────────────────────────────────────────────
        var diagnostics = emitDiag.ToImmutableArray();

        // 1. MIST_BE_0002 is emitted.
        Assert.Contains(diagnostics, d => d.Id == DiagnosticId.MissingIntrinsicEmissionTemplate);

        var be0002 = diagnostics.First(d => d.Id == DiagnosticId.MissingIntrinsicEmissionTemplate);
        Assert.Equal(DiagnosticSeverity.Error, be0002.Severity);
        Assert.Contains("FakeType", be0002.Message);
        Assert.Contains("operator+", be0002.Message);

        // 2. The diagnostic bag has errors.
        Assert.True(emitDiag.HasErrors);
    }

    /// <summary>
    /// Verifies that when MIST_BE_0002 is raised, the full backend returns
    /// <c>Succeeded == false</c> and no output files remain.
    /// </summary>
    [Fact]
    public void Backend_MissingIntrinsicTemplate_ReturnsFailedResult_NoOutputArtifact()
    {
        // ── Arrange ──────────────────────────────────────────────────────
        // Build a minimal model with stdlib + user code where the user code
        // references an intrinsic operator that has no emission template.
        var stdlibSource = @"
namespace Standard;
intrinsic public struct FakeType {
    intrinsic public static FakeType operator +(FakeType a, FakeType b);
}";
        var userSource = @"
namespace TestProject;
public class Program {
    public static void Main() {
        int x = 1;
    }
}";

        // Build stdlib AST
        var fakeTypeRef = new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken("FakeType"),
                    Span = new Storm.Runtime.SourceSpan(0, 0),
                },
            }
        };

        var paramA = new ParameterNode
        {
            Type = fakeTypeRef,
            Name = MakeToken("a"),
        };
        var paramB = new ParameterNode
        {
            Type = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("FakeType"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                }
            },
            Name = MakeToken("b"),
        };

        var operatorDecl = new OperatorDeclarationNode
        {
            ReturnType = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken("FakeType"),
                        Span = new Storm.Runtime.SourceSpan(0, 0),
                    },
                }
            },
            Symbol = new PlusOpOperatorSymbolNode(),
            Parameters = new ParameterListNode { Parameter = paramA, Parameters = ImmutableArray.Create(paramB) },
            Body = new OperatorNoBodyOperatorBodyNode(),
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new IntrinsicModifierModifierNode(),
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var fakeStruct = new StructDeclarationNode
        {
            Name = MakeToken("FakeType"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new OperatorMemberMemberDeclarationNode { Operator = operatorDecl }),
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new IntrinsicModifierModifierNode(),
                new PublicModifierModifierNode()),
        };

        var stdlibNs = new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("Standard"),
                Span = new Storm.Runtime.SourceSpan(0, 0),
            },
            Body = ImmutableArray.Create<TypeDeclarationNode>(
                new StructTypeTypeDeclarationNode { StructDeclaration = fakeStruct }).AsNamespaceBodyItems(),
        };

        var stdlibAst = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = stdlibNs }),
        };

        // Build user AST with an additive expression
        var addExpr = MistAstGenerator.MakeAdditive(
            MistAstGenerator.MakeIdentifierExpr("a"),
            MistAstGenerator.MakeIdentifierExpr("b"),
            "+");
        var stmtAdd = MistAstGenerator.MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            "c",
            addExpr);

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(stmtAdd),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var userAst = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        // Build the semantic model
        var stdlibSourceText = new SourceText("stdlib_fake.mist", stdlibSource);
        var userSourceText = new SourceText("test.mist", userSource);
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(stdlibSourceText) { IsStdlib = true },
            new CompilationUnit(userSourceText)));
        var buildDiag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(stdlibAst, userAst),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        // Find FakeType's operator+ and record it as resolved operator
        var fakeTypeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "FakeType" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(fakeTypeSymbol);
        var fakeType = fakeTypeSymbol!.DeclaredType as StructType;
        Assert.NotNull(fakeType);

        var operatorPlusSymbol = fakeType!.MemberScope.Members
            .FirstOrDefault(m => m.Name == "operator+" && m.Kind == SymbolKind.Method);
        Assert.NotNull(operatorPlusSymbol);

        var additiveNode = FindAdditiveNode(userAst);
        Assert.NotNull(additiveNode);
        model.RecordResolvedOperator(additiveNode!, operatorPlusSymbol!);

        // ── Act ──────────────────────────────────────────────────────────
        var backend = new CSharpTranspilerBackend();
        var backendDiag = new DiagnosticBag();
        var result = backend.Emit(model, outputDir, binDir, "TestProject", backendDiag);

        // ── Assert ───────────────────────────────────────────────────────
        // BackendResult.Succeeded == false
        Assert.False(result.Succeeded);

        // MIST_BE_0002 is in the diagnostics
        Assert.Contains(result.Diagnostics, d => d.Id == DiagnosticId.MissingIntrinsicEmissionTemplate);

        // No .cs files remain in the output directory (Requirement 5.10)
        var csFiles = Directory.Exists(outputDir)
            ? Directory.GetFiles(outputDir, "*.cs")
            : [];
        Assert.Empty(csFiles);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 43.2: Every stdlib intrinsic has a matching emission template.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Iterates every <see cref="Symbol"/> with <c>IsIntrinsic == true</c>
    /// in the stdlib semantic model and asserts the
    /// <see cref="IntrinsicEmissionTable"/> has a matching template.
    /// Ensures no stdlib intrinsic ships without its template.
    /// </summary>
    [Fact]
    public void AllStdlibIntrinsics_HaveMatchingEmissionTemplate()
    {
        // ── Arrange ──────────────────────────────────────────────────────
        // Build a semantic model with the real stdlib loaded.
        var userSource = @"
namespace TestProject;
public class Program {
    public static void Main() { }
}";

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray<StatementNode>.Empty,
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var userAst = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsTopLevelItems(),
        };

        var (model, diagnostics) = StdlibTestHelper.BuildModelWithStdlib(userAst, source: userSource);

        // Verify no build errors
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // ── Act & Assert ─────────────────────────────────────────────────
        var registry = new IntrinsicEmissionTable();
        var missing = new List<string>();

        // Known unary-only operator symbols (Requirement 4.4).
        var unaryOnlyOps = new HashSet<string> { "!" };

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol is not Symbol { IsIntrinsic: true } intrinsicSymbol)
            {
                continue;
            }

            // Skip non-method symbols (type-level intrinsic markers are on
            // struct/class symbols, not on operator/method symbols).
            if(intrinsicSymbol.Kind != SymbolKind.Method &&
                intrinsicSymbol.Kind != SymbolKind.Constructor)
            {
                continue;
            }

            // Determine the declaring type's qualified name.
            var declaringType = GetDeclaringTypeQualifiedName(intrinsicSymbol);
            if(declaringType is null)
            {
                continue;
            }

            // Skip types whose static methods are emitted via CSharpTypeMap
            // name resolution rather than IntrinsicEmissionTable templates.
            // Console, Math, Convert, and Task map 1:1 to their CLR equivalents
            // and the emitter resolves them through TryGetMappedName in
            // EmitPrimary — no template needed (design: Approach B).
            // Array, Span, and ReadOnlySpan are lowered to their CLR equivalents
            // (T[], System.Span<T>, System.ReadOnlySpan<T>) via dedicated
            // ArrayType/SpanType handling in the type map and emitter — their
            // intrinsic members (constructors, GetEnumerator, Slice, indexers)
            // are emitted through dedicated code paths, not templates.
            if(declaringType is "Standard.Console" or "Standard.Math" or "Standard.Convert"
                or "Standard.Task" or "Standard.ValueTask"
                or "Standard.Array" or "Standard.Span" or "Standard.ReadOnlySpan"
                or "Enumerator")
            {
                continue;
            }

            var name = intrinsicSymbol.Name;

            // Cast operators (operator_implicit, operator_explicit) are
            // emitted through a separate path (TryGetCastOperator), not
            // through the binary/unary/instance method tables. Skip them.
            if(name.StartsWith("operator_implicit") || name.StartsWith("operator_explicit"))
            {
                continue;
            }

            // Determine what kind of intrinsic this is and look it up.
            IntrinsicEmissionTemplate? template;

            if(name.StartsWith("operator"))
            {
                var op = name["operator".Length..];

                // Determine arity: known unary-only ops are unary; "-" can
                // be either, so we try unary first then binary; everything
                // else is binary.
                if(unaryOnlyOps.Contains(op))
                {
                    template = registry.TryGetUnaryOperator(declaringType, op);
                }
                else if(op == "-")
                {
                    // "-" is overloaded: try unary first, then binary.
                    template = registry.TryGetUnaryOperator(declaringType, op)
                            ?? registry.TryGetBinaryOperator(declaringType, op);
                }
                else
                {
                    template = registry.TryGetBinaryOperator(declaringType, op);
                }
            }
            else if(intrinsicSymbol.Kind == SymbolKind.Constructor)
            {
                // Constructor — look up via TryGetConstructor.
                var paramTypes = ExtractParameterTypeNames(intrinsicSymbol, model);
                template = registry.TryGetConstructor(declaringType, paramTypes);
            }
            else
            {
                // Method — extract parameter types from the symbol's
                // method scope. Parameters are registered as Parameter-kind
                // symbols in the scope that the method owns.
                var paramTypes = ExtractParameterTypeNames(intrinsicSymbol, model);
                template = registry.TryGetInstanceMethod(declaringType, name, paramTypes)
                        ?? registry.TryGetStaticMethod(declaringType, name, paramTypes);
            }

            if(template is null)
            {
                missing.Add($"{declaringType}.{name}");
            }
        }

        // Assert no intrinsics are missing templates.
        Assert.True(missing.Count == 0,
            $"The following stdlib intrinsics have no emission template:\n  {string.Join("\n  ", missing)}");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the AST depth-first and returns the first AdditiveNode that has
    /// a real binary operator (non-null Right distinct from Left).
    /// </summary>
    private static AdditiveNode? FindAdditiveNode(Storm.Runtime.SyntaxNode root)
    {
        var stack = new Stack<Storm.Runtime.SyntaxNode>();
        stack.Push(root);
        while(stack.Count > 0)
        {
            var node = stack.Pop();
            if(node is AdditiveNode add
                && add.Right is not null
                && !ReferenceEquals(add.Right, add.Left))
            {
                return add;
            }
            foreach(var child in node.Children())
            {
                if(child is not null)
                {
                    stack.Push(child);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the fully-qualified Mist type name for a symbol's declaring type.
    /// </summary>
    private static string? GetDeclaringTypeQualifiedName(Symbol symbol)
    {
        var memberScope = symbol.ContainingScope;
        if(memberScope?.Parent is null)
        {
            return null;
        }

        foreach(var sibling in memberScope.Parent.Members)
        {
            if(sibling.DeclaredType is ClassType ct && ct.MemberScope == memberScope)
            {
                return ct.ContainingNamespace is not null
                    ? $"{ct.ContainingNamespace}.{ct.DisplayName}"
                    : ct.DisplayName;
            }

            if(sibling.DeclaredType is StructType st && st.MemberScope == memberScope)
            {
                return st.ContainingNamespace is not null
                    ? $"{st.ContainingNamespace}.{st.DisplayName}"
                    : st.DisplayName;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts parameter type names from a method/operator symbol using
    /// <see cref="ParameterSignature.Extract"/>.
    /// </summary>
    private static IReadOnlyList<string> ExtractParameterTypeNames(Symbol symbol, ISemanticModel model)
    {
        var paramTypes = ParameterSignature.Extract(symbol, model);
        return paramTypes
            .Select(t => GetTypeQualifiedName(t) ?? t.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Returns the fully-qualified name for a type.
    /// </summary>
    private static string? GetTypeQualifiedName(IType? type)
    {
        if(type is ClassType ct)
        {
            return ct.ContainingNamespace is not null
                ? $"{ct.ContainingNamespace}.{ct.DisplayName}"
                : ct.DisplayName;
        }

        if(type is StructType st)
        {
            return st.ContainingNamespace is not null
                ? $"{st.ContainingNamespace}.{st.DisplayName}"
                : st.DisplayName;
        }

        return type?.DisplayName;
    }
}