// Feature: abstracts, Property 1: Abstract declaration round-trip
// **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for abstract declaration round-trip.
///
/// Property 1: For any valid Mist AST containing abstract declarations (with varying
/// method counts, parameter lists, and abstract inheritance lists), class declarations
/// with abstract lists (with or without a base class), and struct declarations with
/// abstract lists, pretty-printing the AST and then parsing the result and pretty-printing
/// again SHALL produce identical output.
///
/// **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**
/// </summary>
public class AbstractRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Token helper ────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = Tok(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a generated test case: a compilation unit containing a mix of
    /// abstract, class, and struct declarations with abstract-related features.
    /// </summary>
#pragma warning disable CA1034
    public record AbstractRoundTripTestCase(
        string Description,
        CompilationUnitNode Ast)
    {
        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

    // ─── AST construction helpers ────────────────────────────────────────

    private static AbstractMethodDeclarationNode MakeAbstractMethod(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new AbstractMethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = returnType ?? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = parameters,
        };
    }

    private static AbstractDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<AbstractMethodDeclarationNode> methods,
        string? parentAbstractName = null)
    {
        var members = methods.Select(m =>
            (AbstractMemberDeclarationNode)new AbstractMethodMemberAbstractMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new AbstractDeclarationNode
        {
            Name = Tok(name),
            Members = members,
        };

        if(parentAbstractName is not null)
        {
            var _abstractList = MakeBaseList(parentAbstractName);
            decl.AbstractList = _abstractList;
            decl.Abstracts = _abstractList;
        }

        return decl;
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        ImmutableArray<MemberDeclarationNode> members,
        string? baseOrAbstractName = null,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = Tok(name),
            Members = members,
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };

        if(baseOrAbstractName is not null)
        {
            var _baseList = MakeBaseList(baseOrAbstractName);
            decl.BaseClause = _baseList;
            decl.BaseList = _baseList;
        }

        return decl;
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        ImmutableArray<MemberDeclarationNode> members,
        string? abstractName = null,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = Tok(name),
            Members = members,
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };

        if(abstractName is not null)
        {
            var _abstractList = MakeBaseList(abstractName);
            decl.AbstractList = _abstractList;
            decl.Abstracts = _abstractList;
        }

        return decl;
    }

    private static MethodDeclarationNode MakeMethod(
        string name,
        TypeReferenceNode returnType,
        ImmutableArray<StatementNode> body,
        ImmutableArray<ModifierNode>? modifiers = null,
        ParameterListNode? parameters = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = returnType,
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };
    }

    private static FieldDeclarationNode MakeField(
        string name,
        TypeReferenceNode type,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        return new FieldDeclarationNode
        {
            Name = Tok(name),
            Type = type,
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    // ─── Type reference helpers ──────────────────────────────────────────

    private static readonly TypeReferenceNode VoidType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() };
    private static readonly TypeReferenceNode IntType = new TypeReferenceNode
    {
        First = new PrimitiveSingleTypeRefSingleTypeRefNode
        {
            PrimitiveType = new IntTypePrimitiveTypeNode(),
        }
    };

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static readonly string[] AbstractNames =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];
    private static readonly string[] ClassNames =
        ["Animal", "Dog", "Cat", "Vehicle", "Shape", "Widget"];
    private static readonly string[] StructNames =
        ["Point", "Vector", "Color", "Rect", "Size", "Coord"];
    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];
    private static readonly string[] ParamNames =
        ["x", "y", "value", "count", "name", "flag"];

    /// <summary>
    /// Generates a test case with a mix of abstract, class, and struct declarations.
    /// </summary>
    private static Gen<AbstractRoundTripTestCase> GenTestCase()
    {
        // Strategy: generate 1-3 abstracts, 0-2 classes, 0-1 structs.
        // Some abstracts may inherit from earlier abstracts.
        // Classes and structs may reference an abstract.
        var gen1 = from seed in Gen.Choose(0, 100000)
                   from abstractCount in Gen.Choose(1, 3)
                   from classCount in Gen.Choose(0, 2)
                   from structCount in Gen.Choose(0, 1)
                   select BuildTestCase(seed, abstractCount, classCount, structCount);

        return gen1;
    }

    private static AbstractRoundTripTestCase BuildTestCase(
        int seed,
        int abstractCount,
        int classCount,
        int structCount)
    {
        var rng = new Random(seed);
        var typeDecls = new List<TypeDeclarationNode>();
        var declaredAbstractNames = new List<string>();
        var descParts = new List<string>();

        // Generate abstracts
        for(var i = 0; i < abstractCount; i++)
        {
            var abstractName = AbstractNames[i % AbstractNames.Length];
            // Ensure unique names
            if(declaredAbstractNames.Contains(abstractName))
            {
                abstractName = abstractName + (i + 1);
            }

            declaredAbstractNames.Add(abstractName);

            // Decide method count (0-3)
            var methodCount = rng.Next(0, 4);
            var methods = ImmutableArray.CreateBuilder<AbstractMethodDeclarationNode>();
            var usedMethodNames = new HashSet<string>();

            for(var m = 0; m < methodCount; m++)
            {
                var methodName = MethodNames[m % MethodNames.Length];
                if(!usedMethodNames.Add(methodName))
                {
                    continue;
                }

                var retType = PickReturnType(rng);
                ParameterListNode? parameters = null;
                if(rng.Next(2) == 0)
                {
                    parameters = new ParameterListNode
                    {
                        Parameter = new ParameterNode
                        {
                            Type = PickParamType(rng),
                            Name = Tok(ParamNames[rng.Next(ParamNames.Length)]),
                        },
                    };
                }

                methods.Add(new AbstractMethodDeclarationNode
                {
                    Name = Tok(methodName),
                    ReturnType = retType,
                    Parameters = parameters,
                });
            }

            // Decide if this abstract inherits from a previously declared abstract
            string? parentName = null;
            if(i > 0 && rng.Next(3) == 0)
            {
                parentName = declaredAbstractNames[rng.Next(i)];
            }

            var abstractDecl = MakeAbstractDecl(abstractName, methods.ToImmutable(), parentName);
            typeDecls.Add(new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl });

            var parentPart = parentName is not null ? $" : {parentName}" : "";
            descParts.Add($"abstract {abstractName}{parentPart}({methods.Count} methods)");
        }

        // Generate classes (some may reference an abstract)
        for(var c = 0; c < classCount; c++)
        {
            var className = ClassNames[c % ClassNames.Length];
            var mods = rng.Next(2) == 0
                ? ImmutableArray<ModifierNode>.Empty
                : ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode());

            // Decide if this class references an abstract (via BaseClause)
            string? baseOrAbstractName = null;
            if(declaredAbstractNames.Count > 0 && rng.Next(2) == 0)
            {
                baseOrAbstractName = declaredAbstractNames[rng.Next(declaredAbstractNames.Count)];
            }

            // Add 0-2 members (fields and methods)
            var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
            var memberCount = rng.Next(0, 3);
            for(var m = 0; m < memberCount; m++)
            {
                if(rng.Next(2) == 0)
                {
                    members.Add(new FieldMemberMemberDeclarationNode
                    {
                        Field = MakeField("field" + m, PickParamType(rng)),
                    });
                }
                else
                {
                    members.Add(new MethodMemberMemberDeclarationNode
                    {
                        Method = MakeMethod(
                            MethodNames[rng.Next(MethodNames.Length)],
                            new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                            ImmutableArray<StatementNode>.Empty,
                            mods),
                    });
                }
            }

            var classDecl = MakeClassDecl(className, members.ToImmutable(), baseOrAbstractName, mods);
            typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

            var basePart = baseOrAbstractName is not null ? $" : {baseOrAbstractName}" : "";
            descParts.Add($"class {className}{basePart}");
        }

        // Generate structs (some may reference an abstract)
        for(var s = 0; s < structCount; s++)
        {
            var structName = StructNames[s % StructNames.Length];
            var mods = rng.Next(2) == 0
                ? ImmutableArray<ModifierNode>.Empty
                : ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode());

            // Decide if this struct references an abstract
            string? abstractName = null;
            if(declaredAbstractNames.Count > 0 && rng.Next(2) == 0)
            {
                abstractName = declaredAbstractNames[rng.Next(declaredAbstractNames.Count)];
            }

            // Add 0-1 field members
            var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
            if(rng.Next(2) == 0)
            {
                members.Add(new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("val", PickParamType(rng)),
                });
            }

            var structDecl = MakeStructDecl(structName, members.ToImmutable(), abstractName, mods);
            typeDecls.Add(new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });

            var abstractPart = abstractName is not null ? $" : {abstractName}" : "";
            descParts.Add($"struct {structName}{abstractPart}");
        }

        var description = string.Join(", ", descParts);
        var ast = MakeCompilationUnit(typeDecls.ToArray());
        return new AbstractRoundTripTestCase(description, ast);
    }

    private static TypeReferenceNode PickReturnType(Random rng)
    {
        return (rng.Next(4)) switch
        {
            0 => new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            1 => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            2 => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
            _ => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } },
        };
    }

    private static TypeReferenceNode PickParamType(Random rng)
    {
        return (rng.Next(3)) switch
        {
            0 => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            1 => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
            _ => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } },
        };
    }

    /// <summary>FsCheck Arbitrary registration for AbstractRoundTripTestCase.</summary>
    public static Arbitrary<AbstractRoundTripTestCase> ArbitraryAbstractRoundTripTestCase()
    {
        return GenTestCase().ToArbitrary();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 1: Abstract declaration round-trip
    //
    // **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4,
    //              2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**
    //
    // For any valid Mist AST containing abstract declarations, class
    // declarations with abstract lists, and struct declarations with
    // abstract lists, pretty-printing → parsing → pretty-printing
    // SHALL produce identical source text.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AbstractRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePreservesAbstractDeclarations(AbstractRoundTripTestCase testCase)
    {
        // Step 1: Pretty-print the generated AST to source text
        var printed1 = MistPrettyPrinter.Print(testCase.Ast);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // Step 2: Parse the printed source text back to an AST
        var sourceText = new Mist.Common.Text.SourceText("test.mist", printed1);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Parse errors: {string.Join("; ", errors.Select(e => e.Message))}\nSource:\n{printed1}");
        }

        var reparsed = result.Root as CompilationUnitNode;
        if(reparsed is null)
        {
            return false.ToProperty().Label($"Reparsed root was null.\nSource:\n{printed1}");
        }

        // Step 3: Pretty-print the reparsed AST
        var printed2 = MistPrettyPrinter.Print(reparsed);

        // Step 4: Compare the two printed outputs — they must be identical
        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EmptyAbstract()
    {
        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode
            {
                AbstractDeclaration = MakeAbstractDecl("IEmpty",
                    ImmutableArray<AbstractMethodDeclarationNode>.Empty),
            });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_AbstractWithMethods()
    {
        var methods = ImmutableArray.Create(
            MakeAbstractMethod("DoWork"),
            MakeAbstractMethod("GetValue", IntType),
            MakeAbstractMethod("Process", VoidType,
                new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = IntType,
                        Name = Tok("x"),
                    },
                }));

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode
            {
                AbstractDeclaration = MakeAbstractDecl("IWorker", methods),
            });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_AbstractInheritance()
    {
        var baseAbstract = MakeAbstractDecl("IBase",
            ImmutableArray.Create(MakeAbstractMethod("BaseMethod")));
        var derivedAbstract = MakeAbstractDecl("IDerived",
            ImmutableArray.Create(MakeAbstractMethod("DerivedMethod")),
            parentAbstractName: "IBase");

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = baseAbstract },
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = derivedAbstract });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_ClassImplementingAbstract()
    {
        var abstractDecl = MakeAbstractDecl("IRunnable",
            ImmutableArray.Create(MakeAbstractMethod("Run")));

        var classDecl = MakeClassDecl("Runner",
            ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode
                {
                    Method = MakeMethod("Run", VoidType, ImmutableArray<StatementNode>.Empty),
                }),
            baseOrAbstractName: "IRunnable");

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_StructImplementingAbstract()
    {
        var abstractDecl = MakeAbstractDecl("IComparable",
            ImmutableArray.Create(MakeAbstractMethod("CompareTo", IntType)));

        var structDecl = MakeStructDecl("Point",
            ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("x", IntType),
                }),
            abstractName: "IComparable");

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl },
            new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_MixedDeclarations()
    {
        // Abstract with methods
        var abstractDecl = MakeAbstractDecl("IShape",
            ImmutableArray.Create(
                MakeAbstractMethod("Area", new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                }),
                MakeAbstractMethod("Draw")));

        // Class implementing abstract
        var classDecl = MakeClassDecl("Circle",
            ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("radius", IntType),
                }),
            baseOrAbstractName: "IShape",
            modifiers: ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()));

        // Struct implementing abstract
        var structDecl = MakeStructDecl("Square",
            ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("side", IntType),
                }),
            abstractName: "IShape");

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
            new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });

        AssertRoundTrip(ast);
    }

    // ─── Round-trip assertion helper ─────────────────────────────────────

    private static void AssertRoundTrip(CompilationUnitNode ast)
    {
        // Step 1: Pretty-print the AST
        var printed1 = MistPrettyPrinter.Print(ast);
        Assert.False(string.IsNullOrWhiteSpace(printed1), "Pretty-printed output should not be empty");

        // Step 2: Parse the printed source
        var sourceText = new Mist.Common.Text.SourceText("test.mist", printed1);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        var reparsed = result.Root as CompilationUnitNode;
        Assert.NotNull(reparsed);

        // Step 3: Pretty-print the reparsed AST
        var printed2 = MistPrettyPrinter.Print(reparsed);

        // Step 4: Compare
        Assert.Equal(printed1, printed2);
    }
}