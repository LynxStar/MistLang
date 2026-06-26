// Feature: mist-language-v1, Property 11: Entry point validation

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for entry point validation in the Mist type checker.
///
/// Property 11: Entry point validation.
/// The type checker SHALL verify that exactly one class contains a valid entry point
/// (static void Main() or static void Main(string[] args)). Zero entry points
/// produces MIST_TC_0017, multiple entry points produces MIST_TC_0018.
///
/// **Validates: Requirements 10.1, 10.2, 10.3**
/// </summary>
public class EntryPointPropertyTests
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

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── AST builders ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a valid static void Main() method declaration.
    /// </summary>
    private static MethodDeclarationNode MakeMainMethod(bool withStringArrayParam = false)
    {
        ParameterListNode? parameters = null;
        if(withStringArrayParam)
        {
            parameters = new ParameterListNode
            {
                Parameter = new ParameterNode
                {
                    Type = new TypeReferenceNode
                    {
                        First = new PrimitiveSingleTypeRefSingleTypeRefNode
                        {
                            PrimitiveType = new StringTypePrimitiveTypeNode(),
                            ArrayRank = new UnsizedArrayRankArrayRankSuffixNode(),
                        }
                    },
                    Name = MakeToken("args"),
                },
            };
        }

        return new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates a class with the given name and members.
    /// </summary>
    private static ClassDeclarationNode MakeClass(string name, params MemberDeclarationNode[] members)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray.Create(members),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Entry point validation
    // **Validates: Requirements 10.1, 10.2, 10.3**
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034
    public record EntryPointScenario(
        string Label,
        int EntryPointCount,
        Func<CompilationUnitNode> AstBuilder,
        string? ExpectedDiagnosticId)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    private static readonly EntryPointScenario[] EntryPointScenarios =
    [
        // Exactly one entry point: static void Main() — no error
        new("OneEntryPoint_NoParams", 1, () =>
        {
            var classDecl = MakeClass("Program",
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() });
            return new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
            };
        }, null),

        // Exactly one entry point: static void Main(string[] args) — no error
        new("OneEntryPoint_WithArgs", 1, () =>
        {
            var classDecl = MakeClass("Program",
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod(withStringArrayParam: true) });
            return new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
            };
        }, null),

        // Zero entry points — MIST_TC_0017
        new("NoEntryPoint", 0, () =>
        {
            var method = new MethodDeclarationNode
            {
                Name = MakeToken("Run"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode()),
            };
            var classDecl = MakeClass("Program",
                new MethodMemberMemberDeclarationNode { Method = method });
            return new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
            };
        }, "MIST_TC_0017"),

        // Multiple entry points — MIST_TC_0018
        new("MultipleEntryPoints", 2, () =>
        {
            var classA = MakeClass("ClassA",
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() });
            var classB = MakeClass("ClassB",
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() });
            return new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classA },
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classB }).AsTopLevelItems(),
            };
        }, "MIST_TC_0018"),

        // Non-static Main is not an entry point — MIST_TC_0017
        new("NonStaticMain_NotEntryPoint", 0, () =>
        {
            var method = new MethodDeclarationNode
            {
                Name = MakeToken("Main"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode()),
            };
            var classDecl = MakeClass("Program",
                new MethodMemberMemberDeclarationNode { Method = method });
            return new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
            };
        }, "MIST_TC_0017"),

        // Main with wrong return type is not an entry point — MIST_TC_0017
        new("MainWithIntReturn_NotEntryPoint", 0, () =>
        {
            var method = new MethodDeclarationNode
            {
                Name = MakeToken("Main"),
                ReturnType = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                } },
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(),
                    new StaticModifierModifierNode()),
            };
            var classDecl = MakeClass("Program",
                new MethodMemberMemberDeclarationNode { Method = method });
            return new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
            };
        }, "MIST_TC_0017"),
    ];

    public static Arbitrary<EntryPointScenario> ArbitraryEntryPointScenarios()
    {
        var gen = Gen.Elements(EntryPointScenarios);
        return gen.ToArbitrary();
    }

    [Property(Arbitrary = new[] { typeof(EntryPointPropertyTests) }, MaxTest = 18)]
    public bool EntryPointValidation_MatchesExpectation(EntryPointScenario scenario)
    {
        var ast = scenario.AstBuilder();
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        if(scenario.ExpectedDiagnosticId is null)
        {
            // No entry point error expected
            return !diagnostics.Any(d =>
                d.Id.Value is "MIST_TC_0017" or "MIST_TC_0018");
        }
        else
        {
            return diagnostics.Any(d => d.Id.Value == scenario.ExpectedDiagnosticId);
        }
    }

    // ─── Targeted entry point tests ──────────────────────────────────────

    [Fact]
    public void OneEntryPoint_StaticVoidMain_NoError()
    {
        var classDecl = MakeClass("Program",
            new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() });
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d =>
            d.Id.Value is "MIST_TC_0017" or "MIST_TC_0018");
    }

    [Fact]
    public void OneEntryPoint_StaticVoidMainWithArgs_NoError()
    {
        var classDecl = MakeClass("Program",
            new MethodMemberMemberDeclarationNode { Method = MakeMainMethod(withStringArrayParam: true) });
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d =>
            d.Id.Value is "MIST_TC_0017" or "MIST_TC_0018");
    }

    [Fact]
    public void NoEntryPoint_ProducesDiagnostic()
    {
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("Run"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var classDecl = MakeClass("Program",
            new MethodMemberMemberDeclarationNode { Method = method });
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0017");
    }

    [Fact]
    public void MultipleEntryPoints_ProducesDiagnostic()
    {
        var classA = MakeClass("ClassA",
            new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() });
        var classB = MakeClass("ClassB",
            new MethodMemberMemberDeclarationNode { Method = MakeMainMethod() });
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classA },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classB }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0018");
    }

    [Fact]
    public void NonStaticMain_NotRecognizedAsEntryPoint()
    {
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode()),
        };
        var classDecl = MakeClass("Program",
            new MethodMemberMemberDeclarationNode { Method = method });
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0017");
    }

    [Fact]
    public void MainWithWrongReturnType_NotRecognizedAsEntryPoint()
    {
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
        var classDecl = MakeClass("Program",
            new MethodMemberMemberDeclarationNode { Method = method });
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0017");
    }
}