// Feature: interfaces, Property 1: Interface declaration round-trip
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
/// Property-based tests for interface declaration round-trip.
///
/// Property 1: For any valid Mist AST containing interface declarations (with varying
/// method counts, parameter lists, and interface inheritance lists), class declarations
/// with interface lists (with or without a base class), and struct declarations with
/// interface lists, pretty-printing the AST and then parsing the result and pretty-printing
/// again SHALL produce identical output.
///
/// **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**
/// </summary>
public class InterfaceRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Token helper ────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

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
    /// interface, class, and struct declarations with interface-related features.
    /// </summary>
#pragma warning disable CA1034
    public record InterfaceRoundTripTestCase(
        string Description,
        CompilationUnitNode Ast)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── AST construction helpers ────────────────────────────────────────

    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = returnType ?? new VoidTypeRefTypeReferenceNode(),
            Parameters = parameters,
        };
    }

    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods,
        string? parentInterfaceName = null)
    {
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new InterfaceDeclarationNode
        {
            Name = Tok(name),
            Members = members,
        };

        if (parentInterfaceName is not null)
        {
            var _ifaceList = MakeBaseList(parentInterfaceName);
            decl.InterfaceList = _ifaceList;
            decl.Interfaces = _ifaceList;
        }

        return decl;
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        ImmutableArray<MemberDeclarationNode> members,
        string? baseOrInterfaceName = null,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = Tok(name),
            Members = members,
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };

        if (baseOrInterfaceName is not null)
        {
            var _baseList = MakeBaseList(baseOrInterfaceName);
            decl.BaseClause = _baseList;
            decl.BaseList = _baseList;
        }

        return decl;
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        ImmutableArray<MemberDeclarationNode> members,
        string? interfaceName = null,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = Tok(name),
            Members = members,
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };

        if (interfaceName is not null)
        {
            var _ifaceList = MakeBaseList(interfaceName);
            decl.InterfaceList = _ifaceList;
            decl.Interfaces = _ifaceList;
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

    private static ParameterListNode MakeParameterList(params (TypeReferenceNode Type, string Name)[] parameters)
    {
        if (parameters.Length == 0)
            return null!;

        return new ParameterListNode
        {
            Parameter = new ParameterNode
            {
                Type = parameters[0].Type,
                Name = Tok(parameters[0].Name),
            },
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

    private static readonly TypeReferenceNode VoidType = new VoidTypeRefTypeReferenceNode();
    private static readonly TypeReferenceNode IntType = new PrimitiveTypeRefTypeReferenceNode
    {
        PrimitiveType = new IntTypePrimitiveTypeNode(),
    };

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static readonly string[] InterfaceNames =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];
    private static readonly string[] ClassNames =
        ["Animal", "Dog", "Cat", "Vehicle", "Shape", "Widget"];
    private static readonly string[] StructNames =
        ["Point", "Vector", "Color", "Rect", "Size", "Coord"];
    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];
    private static readonly string[] ParamNames =
        ["x", "y", "value", "count", "name", "flag"];

    private static Gen<TypeReferenceNode> GenReturnType()
    {
        return Gen.Elements<TypeReferenceNode>(
            new VoidTypeRefTypeReferenceNode(),
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new BoolTypePrimitiveTypeNode() },
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new StringTypePrimitiveTypeNode() });
    }

    private static Gen<TypeReferenceNode> GenParamType()
    {
        return Gen.Elements<TypeReferenceNode>(
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new BoolTypePrimitiveTypeNode() },
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new StringTypePrimitiveTypeNode() });
    }

    private static Gen<ImmutableArray<ModifierNode>> GenModifiers()
    {
        return Gen.Elements(
            ImmutableArray<ModifierNode>.Empty,
            ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()));
    }

    /// <summary>
    /// Generates an interface method declaration with a random return type and
    /// optionally a single parameter.
    /// </summary>
    private static Gen<InterfaceMethodDeclarationNode> GenInterfaceMethod(string methodName)
    {
        return from retType in GenReturnType()
               from hasParam in Gen.Elements(true, false)
               from paramType in GenParamType()
               from paramIdx in Gen.Choose(0, ParamNames.Length - 1)
               let parameters = hasParam
                   ? new ParameterListNode
                   {
                       Parameter = new ParameterNode
                       {
                           Type = paramType,
                           Name = Tok(ParamNames[paramIdx]),
                       },
                   }
                   : null
               select new InterfaceMethodDeclarationNode
               {
                   Name = Tok(methodName),
                   ReturnType = retType,
                   Parameters = parameters,
               };
    }

    /// <summary>
    /// Generates a test case with a mix of interface, class, and struct declarations.
    /// </summary>
    private static Gen<InterfaceRoundTripTestCase> GenTestCase()
    {
        // Strategy: generate 1-3 interfaces, 0-2 classes, 0-1 structs.
        // Some interfaces may inherit from earlier interfaces.
        // Classes and structs may reference an interface.
        var gen1 = from seed in Gen.Choose(0, 100000)
                   from interfaceCount in Gen.Choose(1, 3)
                   from classCount in Gen.Choose(0, 2)
                   from structCount in Gen.Choose(0, 1)
                   select BuildTestCase(seed, interfaceCount, classCount, structCount);

        return gen1;
    }

    private static InterfaceRoundTripTestCase BuildTestCase(
        int seed,
        int interfaceCount,
        int classCount,
        int structCount)
    {
        var rng = new Random(seed);
        var typeDecls = new List<TypeDeclarationNode>();
        var declaredInterfaceNames = new List<string>();
        var descParts = new List<string>();

        // Generate interfaces
        for (var i = 0; i < interfaceCount; i++)
        {
            var ifaceName = InterfaceNames[i % InterfaceNames.Length];
            // Ensure unique names
            if (declaredInterfaceNames.Contains(ifaceName))
                ifaceName = ifaceName + (i + 1);
            declaredInterfaceNames.Add(ifaceName);

            // Decide method count (0-3)
            var methodCount = rng.Next(0, 4);
            var methods = ImmutableArray.CreateBuilder<InterfaceMethodDeclarationNode>();
            var usedMethodNames = new HashSet<string>();

            for (var m = 0; m < methodCount; m++)
            {
                var methodName = MethodNames[m % MethodNames.Length];
                if (!usedMethodNames.Add(methodName))
                    continue;

                var retType = PickReturnType(rng);
                ParameterListNode? parameters = null;
                if (rng.Next(2) == 0)
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

                methods.Add(new InterfaceMethodDeclarationNode
                {
                    Name = Tok(methodName),
                    ReturnType = retType,
                    Parameters = parameters,
                });
            }

            // Decide if this interface inherits from a previously declared interface
            string? parentName = null;
            if (i > 0 && rng.Next(3) == 0)
            {
                parentName = declaredInterfaceNames[rng.Next(i)];
            }

            var interfaceDecl = MakeInterfaceDecl(ifaceName, methods.ToImmutable(), parentName);
            typeDecls.Add(new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = interfaceDecl });

            var parentPart = parentName is not null ? $" : {parentName}" : "";
            descParts.Add($"interface {ifaceName}{parentPart}({methods.Count} methods)");
        }

        // Generate classes (some may reference an interface)
        for (var c = 0; c < classCount; c++)
        {
            var className = ClassNames[c % ClassNames.Length];
            var mods = rng.Next(2) == 0
                ? ImmutableArray<ModifierNode>.Empty
                : ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode());

            // Decide if this class references an interface (via BaseClause)
            string? baseOrInterfaceName = null;
            if (declaredInterfaceNames.Count > 0 && rng.Next(2) == 0)
            {
                baseOrInterfaceName = declaredInterfaceNames[rng.Next(declaredInterfaceNames.Count)];
            }

            // Add 0-2 members (fields and methods)
            var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
            var memberCount = rng.Next(0, 3);
            for (var m = 0; m < memberCount; m++)
            {
                if (rng.Next(2) == 0)
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
                            new VoidTypeRefTypeReferenceNode(),
                            ImmutableArray<StatementNode>.Empty,
                            mods),
                    });
                }
            }

            var classDecl = MakeClassDecl(className, members.ToImmutable(), baseOrInterfaceName, mods);
            typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

            var basePart = baseOrInterfaceName is not null ? $" : {baseOrInterfaceName}" : "";
            descParts.Add($"class {className}{basePart}");
        }

        // Generate structs (some may reference an interface)
        for (var s = 0; s < structCount; s++)
        {
            var structName = StructNames[s % StructNames.Length];
            var mods = rng.Next(2) == 0
                ? ImmutableArray<ModifierNode>.Empty
                : ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode());

            // Decide if this struct references an interface
            string? interfaceName = null;
            if (declaredInterfaceNames.Count > 0 && rng.Next(2) == 0)
            {
                interfaceName = declaredInterfaceNames[rng.Next(declaredInterfaceNames.Count)];
            }

            // Add 0-1 field members
            var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
            if (rng.Next(2) == 0)
            {
                members.Add(new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("val", PickParamType(rng)),
                });
            }

            var structDecl = MakeStructDecl(structName, members.ToImmutable(), interfaceName, mods);
            typeDecls.Add(new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });

            var ifacePart = interfaceName is not null ? $" : {interfaceName}" : "";
            descParts.Add($"struct {structName}{ifacePart}");
        }

        var description = string.Join(", ", descParts);
        var ast = MakeCompilationUnit(typeDecls.ToArray());
        return new InterfaceRoundTripTestCase(description, ast);
    }

    private static TypeReferenceNode PickReturnType(Random rng)
    {
        return (rng.Next(4)) switch
        {
            0 => new VoidTypeRefTypeReferenceNode(),
            1 => new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            2 => new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new BoolTypePrimitiveTypeNode() },
            _ => new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new StringTypePrimitiveTypeNode() },
        };
    }

    private static TypeReferenceNode PickParamType(Random rng)
    {
        return (rng.Next(3)) switch
        {
            0 => new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            1 => new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new BoolTypePrimitiveTypeNode() },
            _ => new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new StringTypePrimitiveTypeNode() },
        };
    }

    /// <summary>FsCheck Arbitrary registration for InterfaceRoundTripTestCase.</summary>
    public static Arbitrary<InterfaceRoundTripTestCase> ArbitraryInterfaceRoundTripTestCase() =>
        GenTestCase().ToArbitrary();

    // ═════════════════════════════════════════════════════════════════════
    // Property 1: Interface declaration round-trip
    //
    // **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4,
    //              2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**
    //
    // For any valid Mist AST containing interface declarations, class
    // declarations with interface lists, and struct declarations with
    // interface lists, pretty-printing → parsing → pretty-printing
    // SHALL produce identical source text.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(InterfaceRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePreservesInterfaceDeclarations(InterfaceRoundTripTestCase testCase)
    {
        // Step 1: Pretty-print the generated AST to source text
        var printed1 = MistPrettyPrinter.Print(testCase.Ast);
        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 2: Parse the printed source text back to an AST
        var sourceText = new Mist.Common.Text.SourceText("test.mist", printed1);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            return false.ToProperty()
                .Label($"Parse errors: {string.Join("; ", errors.Select(e => e.Message))}\nSource:\n{printed1}");

        var reparsed = result.Root as CompilationUnitNode;
        if (reparsed is null)
            return false.ToProperty().Label($"Reparsed root was null.\nSource:\n{printed1}");

        // Step 3: Pretty-print the reparsed AST
        var printed2 = MistPrettyPrinter.Print(reparsed);

        // Step 4: Compare the two printed outputs — they must be identical
        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EmptyInterface()
    {
        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode
            {
                InterfaceDeclaration = MakeInterfaceDecl("IEmpty",
                    ImmutableArray<InterfaceMethodDeclarationNode>.Empty),
            });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_InterfaceWithMethods()
    {
        var methods = ImmutableArray.Create(
            MakeInterfaceMethod("DoWork"),
            MakeInterfaceMethod("GetValue", IntType),
            MakeInterfaceMethod("Process", VoidType,
                new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = IntType,
                        Name = Tok("x"),
                    },
                }));

        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode
            {
                InterfaceDeclaration = MakeInterfaceDecl("IWorker", methods),
            });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_InterfaceInheritance()
    {
        var baseInterface = MakeInterfaceDecl("IBase",
            ImmutableArray.Create(MakeInterfaceMethod("BaseMethod")));
        var derivedInterface = MakeInterfaceDecl("IDerived",
            ImmutableArray.Create(MakeInterfaceMethod("DerivedMethod")),
            parentInterfaceName: "IBase");

        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = baseInterface },
            new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = derivedInterface });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_ClassImplementingInterface()
    {
        var iface = MakeInterfaceDecl("IRunnable",
            ImmutableArray.Create(MakeInterfaceMethod("Run")));

        var classDecl = MakeClassDecl("Runner",
            ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode
                {
                    Method = MakeMethod("Run", VoidType, ImmutableArray<StatementNode>.Empty),
                }),
            baseOrInterfaceName: "IRunnable");

        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = iface },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_StructImplementingInterface()
    {
        var iface = MakeInterfaceDecl("IComparable",
            ImmutableArray.Create(MakeInterfaceMethod("CompareTo", IntType)));

        var structDecl = MakeStructDecl("Point",
            ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("x", IntType),
                }),
            interfaceName: "IComparable");

        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = iface },
            new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });

        AssertRoundTrip(ast);
    }

    [Fact]
    public void RoundTrip_MixedDeclarations()
    {
        // Interface with methods
        var iface = MakeInterfaceDecl("IShape",
            ImmutableArray.Create(
                MakeInterfaceMethod("Area", new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }),
                MakeInterfaceMethod("Draw")));

        // Class implementing interface
        var classDecl = MakeClassDecl("Circle",
            ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("radius", IntType),
                }),
            baseOrInterfaceName: "IShape",
            modifiers: ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()));

        // Struct implementing interface
        var structDecl = MakeStructDecl("Square",
            ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = MakeField("side", IntType),
                }),
            interfaceName: "IShape");

        var ast = MakeCompilationUnit(
            new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = iface },
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
