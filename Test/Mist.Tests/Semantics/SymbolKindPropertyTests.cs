// Feature: mist-language-v1, Property 4: Declaration-to-SymbolKind mapping
// **Validates: Requirements 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10**

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
/// Property-based tests verifying that the semantic model assigns the correct
/// <see cref="SymbolKind"/> to every declaration form.
///
/// Property 4: For any valid Mist program containing namespace, class, struct,
/// field, constructor, method, parameter, and local variable declarations, the
/// semantic model SHALL assign each declaration a symbol whose SymbolKind matches
/// the declaration form.
/// </summary>
public class SymbolKindPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

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
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    /// <summary>
    /// Creates a minimal expression AST node (integer literal "0") suitable for
    /// use as a variable initializer. The expression chain is:
    /// ExpressionNode → AssignmentNode → LogicalOrNode → LogicalAndNode →
    /// EqualityNode → RelationalNode → AdditiveNode → MultiplicativeNode →
    /// PostfixExpressionUnaryNode → PostfixNode → IntegerLiteralExpressionPrimaryNode
    /// </summary>
    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
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
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    // ─── FsCheck generator for declaration scenarios ─────────────────────

    /// <summary>
    /// Represents a single declaration scenario to test. Each scenario describes
    /// what kind of declaration to include in the AST and the expected SymbolKind.
    /// </summary>
#pragma warning disable CA1034 // Nested types should not be visible
    public record DeclarationScenario(string Label, SymbolKind ExpectedKind)
    {
        public override string ToString() => Label;
    }
#pragma warning restore CA1034

    /// <summary>
    /// All eight declaration forms that the semantic model must handle.
    /// </summary>
    private static readonly DeclarationScenario[] AllScenarios = new[]
    {
        new DeclarationScenario("Namespace", SymbolKind.Namespace),
        new DeclarationScenario("Class", SymbolKind.Class),
        new DeclarationScenario("Struct", SymbolKind.Struct),
        new DeclarationScenario("Field", SymbolKind.Field),
        new DeclarationScenario("Constructor", SymbolKind.Constructor),
        new DeclarationScenario("Method", SymbolKind.Method),
        new DeclarationScenario("Parameter", SymbolKind.Parameter),
        new DeclarationScenario("LocalVariable", SymbolKind.LocalVariable),
    };

    /// <summary>
    /// FsCheck Arbitrary that generates non-empty subsets of declaration scenarios.
    /// Each generated test case is a combination of declaration forms to include
    /// in a single program, verifying the property holds for arbitrary combinations.
    /// </summary>
    public static Arbitrary<DeclarationScenario[]> ArbitraryScenarioSubsets()
    {
        var gen = Gen.Choose(1, (1 << AllScenarios.Length) - 1)
            .Select(mask =>
            {
                var subset = new List<DeclarationScenario>();
                for(var i = 0; i < AllScenarios.Length; i++)
                {
                    if((mask & (1 << i)) != 0)
                        subset.Add(AllScenarios[i]);
                }
                return subset.ToArray();
            });
        return gen.ToArbitrary();
    }

    // ─── AST builders for each declaration form ──────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode containing all the declaration forms specified
    /// in the given scenarios. Returns the AST, the source string, and a list of
    /// (expectedSymbolName, expectedSymbolKind) pairs for verification.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source, List<(string Name, SymbolKind Kind)> Expected)
        BuildAstForScenarios(DeclarationScenario[] scenarios)
    {
        var expected = new List<(string Name, SymbolKind Kind)>();
        var source = "namespace TestNs; class TestClass { }";

        var hasNamespace = scenarios.Any(s => s.Label == "Namespace");
        var hasClass = scenarios.Any(s => s.Label == "Class");
        var hasStruct = scenarios.Any(s => s.Label == "Struct");
        var hasField = scenarios.Any(s => s.Label == "Field");
        var hasConstructor = scenarios.Any(s => s.Label == "Constructor");
        var hasMethod = scenarios.Any(s => s.Label == "Method");
        var hasParameter = scenarios.Any(s => s.Label == "Parameter");
        var hasLocalVariable = scenarios.Any(s => s.Label == "LocalVariable");

        // We always need at least a class or struct to host members.
        // If only namespace is requested, we still create a minimal class.
        var needsClass = hasClass || hasField || hasConstructor || hasMethod || hasParameter || hasLocalVariable;
        var needsStruct = hasStruct;

        // Build class members
        var classMembers = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        if(hasField)
        {
            var fieldDecl = new FieldDeclarationNode
            {
                Name = MakeToken("_testField"),
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
            };
            classMembers.Add(new FieldMemberMemberDeclarationNode { Field = fieldDecl });
            expected.Add(("_testField", SymbolKind.Field));
        }

        if(hasConstructor)
        {
            var ctorParams = hasParameter
                ? new ParameterListNode
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
                        Name = MakeToken("ctorParam"),
                    },
                }
                : null;

            var ctorBody = new BlockNode();
            if(hasLocalVariable)
            {
                ctorBody = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new VariableDeclarationStatementStatementNode
                        {
                            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                            {
                                Type = new TypeReferenceNode
                                {
                                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                                    {
                                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                                    }
                                },
                                Name = MakeToken("localInCtor"),
                                Initializer = MakeIntLiteralExpression("42"),
                            },
                        }),
                };
                expected.Add(("localInCtor", SymbolKind.LocalVariable));
            }

            var ctorDecl = new ConstructorDeclarationNode
            {
                Name = MakeToken("TestClass"),
                Parameters = ctorParams,
                Body = ctorBody.AsMethodBody(),
            };
            classMembers.Add(new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecl });
            expected.Add(("TestClass", SymbolKind.Constructor));

            if(hasParameter)
            {
                expected.Add(("ctorParam", SymbolKind.Parameter));
            }
        }

        if(hasMethod)
        {
            var methodParams = (!hasConstructor && hasParameter)
                ? new ParameterListNode
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
                        Name = MakeToken("methodParam"),
                    },
                }
                : null;

            var methodBody = new BlockNode();
            if(!hasConstructor && hasLocalVariable)
            {
                methodBody = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new VariableDeclarationStatementStatementNode
                        {
                            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                            {
                                Type = new TypeReferenceNode
                                {
                                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                                    {
                                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                                    }
                                },
                                Name = MakeToken("localInMethod"),
                                Initializer = MakeIntLiteralExpression("99"),
                            },
                        }),
                };
                expected.Add(("localInMethod", SymbolKind.LocalVariable));
            }

            var methodDecl = new MethodDeclarationNode
            {
                Name = MakeToken("TestMethod"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = methodParams,
                Body = methodBody.AsMethodBody(),
            };
            classMembers.Add(new MethodMemberMemberDeclarationNode { Method = methodDecl });
            expected.Add(("TestMethod", SymbolKind.Method));

            if(!hasConstructor && hasParameter)
            {
                expected.Add(("methodParam", SymbolKind.Parameter));
            }
        }

        // If parameter or local variable is requested but no constructor or method hosts them,
        // create a method to host them.
        if((hasParameter || hasLocalVariable) && !hasConstructor && !hasMethod)
        {
            var hostParams = hasParameter
                ? new ParameterListNode
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
                        Name = MakeToken("hostParam"),
                    },
                }
                : null;

            var hostBody = new BlockNode();
            if(hasLocalVariable)
            {
                hostBody = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new VariableDeclarationStatementStatementNode
                        {
                            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                            {
                                Type = new TypeReferenceNode
                                {
                                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                                    {
                                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                                    }
                                },
                                Name = MakeToken("hostLocal"),
                                Initializer = MakeIntLiteralExpression("1"),
                            },
                        }),
                };
                expected.Add(("hostLocal", SymbolKind.LocalVariable));
            }

            var hostMethod = new MethodDeclarationNode
            {
                Name = MakeToken("HostMethod"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = hostParams,
                Body = hostBody.AsMethodBody(),
            };
            classMembers.Add(new MethodMemberMemberDeclarationNode { Method = hostMethod });

            if(hasParameter)
            {
                expected.Add(("hostParam", SymbolKind.Parameter));
            }
        }

        // Build type declarations
        var typeDecls = ImmutableArray.CreateBuilder<TypeDeclarationNode>();

        if(needsClass || (!needsStruct && !hasNamespace))
        {
            var classDecl = new ClassDeclarationNode
            {
                Name = MakeToken("TestClass"),
                Members = classMembers.ToImmutable(),
            };
            typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
            expected.Add(("TestClass", SymbolKind.Class));
        }

        if(needsStruct)
        {
            var structMembers = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
            // Add a field to the struct so it's not empty
            var structField = new FieldDeclarationNode
            {
                Name = MakeToken("StructValue"),
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
            };
            structMembers.Add(new FieldMemberMemberDeclarationNode { Field = structField });

            var structDecl = new StructDeclarationNode
            {
                Name = MakeToken("TestStruct"),
                Members = structMembers.ToImmutable(),
            };
            typeDecls.Add(new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });
            expected.Add(("TestStruct", SymbolKind.Struct));
        }

        // Build the compilation unit
        CompilationUnitNode ast;

        if(hasNamespace)
        {
            source = "namespace TestNs;";
            var qualifiedId = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken("TestNs", start: 10),
                Span = new Storm.Runtime.SourceSpan(10, 6),
            };
            var nsNode = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = qualifiedId,
                Body = typeDecls.ToImmutable().AsNamespaceBodyItems(),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            };
            ast = new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
            };
            expected.Add(("TestNs", SymbolKind.Namespace));
        }
        else
        {
            ast = new CompilationUnitNode
            {
                Items = typeDecls.ToImmutable().AsTopLevelItems(),
            };
        }

        return (ast, source, expected);
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 4: For any combination of declaration forms, the semantic model
    /// assigns each declaration a symbol whose SymbolKind matches the declaration form.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(SymbolKindPropertyTests) }, MaxTest = 50)]
    public bool DeclarationToSymbolKind_AllDeclarationsGetCorrectKind(DeclarationScenario[] scenarios)
    {
        var (ast, source, expectedSymbols) = BuildAstForScenarios(scenarios);
        var model = BuildModel(ast, source);
        var allSymbols = model.AllSymbols;

        foreach(var (name, expectedKind) in expectedSymbols)
        {
            var matchingSymbol = allSymbols.FirstOrDefault(s => s.Name == name && s.Kind == expectedKind);
            if(matchingSymbol is null)
            {
                return false;
            }
        }

        return true;
    }

    // ─── Targeted tests for each declaration form ────────────────────────
    // These ensure each individual mapping is tested even if FsCheck
    // doesn't generate a scenario that includes it.

    [Fact]
    public void Namespace_ProducesNamespaceSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("Namespace", SymbolKind.Namespace),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.Namespace && s.Name == "TestNs");
    }

    [Fact]
    public void Class_ProducesClassSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("Class", SymbolKind.Class),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.Class && s.Name == "TestClass");
    }

    [Fact]
    public void Struct_ProducesStructSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("Struct", SymbolKind.Struct),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.Struct && s.Name == "TestStruct");
    }

    [Fact]
    public void Field_ProducesFieldSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("Field", SymbolKind.Field),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.Field && s.Name == "_testField");
    }

    [Fact]
    public void Constructor_ProducesConstructorSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("Constructor", SymbolKind.Constructor),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.Constructor);
    }

    [Fact]
    public void Method_ProducesMethodSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("Method", SymbolKind.Method),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.Method && s.Name == "TestMethod");
    }

    [Fact]
    public void Parameter_ProducesParameterSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("Parameter", SymbolKind.Parameter),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.Parameter);
    }

    [Fact]
    public void LocalVariable_ProducesLocalVariableSymbolKind()
    {
        var (ast, source, expected) = BuildAstForScenarios(new[]
        {
            new DeclarationScenario("LocalVariable", SymbolKind.LocalVariable),
        });
        var model = BuildModel(ast, source);
        Assert.Contains(model.AllSymbols, s => s.Kind == SymbolKind.LocalVariable);
    }

    [Fact]
    public void AllDeclarationForms_ProduceCorrectSymbolKinds()
    {
        var (ast, source, expected) = BuildAstForScenarios(AllScenarios);
        var model = BuildModel(ast, source);
        var allSymbols = model.AllSymbols;

        foreach(var (name, expectedKind) in expected)
        {
            Assert.Contains(allSymbols, s => s.Name == name && s.Kind == expectedKind);
        }
    }
}