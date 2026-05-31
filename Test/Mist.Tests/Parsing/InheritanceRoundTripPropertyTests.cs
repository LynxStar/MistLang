// Feature: class-inheritance, Property 1: Parse/print round-trip for inheritance clauses

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for parse/print round-trip with inheritance clauses.
///
/// **Validates: Requirements 1.1, 1.2, 1.4, 1.5, 1.6**
///
/// For any valid Mist AST containing class declarations with or without an
/// inheritance clause, pretty-printing the AST to source text, parsing it back,
/// and pretty-printing again SHALL produce identical source text.
/// </summary>
public class InheritanceRoundTripPropertyTests
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

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a generated test case: a set of class declarations, some with
    /// inheritance clauses, wrapped in a CompilationUnitNode.
    /// </summary>
#pragma warning disable CA1034
    public record InheritanceRoundTripTestCase(
        string Description,
        CompilationUnitNode Ast)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── AST construction helpers ────────────────────────────────────────

    private static ExpressionNode WrapPrimary(PrimaryNode primary)
    {
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
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIntLiteral(string value) =>
        WrapPrimary(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(value) });

    private static ExpressionNode MakeIdentifierExpr(string name) =>
        WrapPrimary(new IdentifierExpressionPrimaryNode { Name = Tok(name) });

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

    private static ConstructorDeclarationNode MakeConstructor(
        string className,
        ParameterListNode? parameters = null,
        ConstructorChainNode? chain = null,
        ImmutableArray<StatementNode>? body = null,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        return new ConstructorDeclarationNode
        {
            Name = Tok(className),
            Parameters = parameters,
            Chain = chain,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body ?? ImmutableArray<StatementNode>.Empty } },
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };
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

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseClassName,
        ImmutableArray<MemberDeclarationNode> members,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(name),
            Members = members,
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };

        if (baseClassName is not null)
        {
            var _baseList = new BaseListNode
            {
                First = new BaseTypeRefNode { Name = Tok(baseClassName) },
                Rest = new BaseTypeRefNode { Name = Tok(baseClassName) },
                BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
            };
            classDecl.BaseClause = _baseList;
            classDecl.BaseList = _baseList;
        }

        return classDecl;
    }

    private static CompilationUnitNode MakeCompilationUnit(params ClassDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
            (TypeDeclarationNode)new ClassTypeTypeDeclarationNode { ClassDeclaration = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = (types).AsTopLevelItems() };
    }

    // ─── Parse helper ────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        // Check for parse errors
        if (bag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
            return null;

        return result.Root as CompilationUnitNode;
    }

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Animal", "Dog", "Cat", "Vehicle", "Car", "Shape", "Circle"];
    private static readonly string[] FieldNames = ["x", "y", "name", "value", "count", "size"];
    private static readonly string[] MethodNames = ["DoWork", "Process", "GetValue", "Run", "Execute"];

    private static Gen<TypeReferenceNode> GenPrimitiveType()
    {
        return Gen.Elements<TypeReferenceNode>(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } });
    }

    private static Gen<ImmutableArray<ModifierNode>> GenModifiers()
    {
        return Gen.Elements(
            ImmutableArray<ModifierNode>.Empty,
            ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()));
    }

    private static Gen<MemberDeclarationNode> GenFieldMember()
    {
        return from name in Gen.Elements(FieldNames)
               from type in GenPrimitiveType()
               from mods in GenModifiers()
               select (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
               {
                   Field = MakeField(name, type, mods),
               };
    }

    private static Gen<MemberDeclarationNode> GenMethodMember(string className)
    {
        return from name in Gen.Elements(MethodNames)
               from mods in GenModifiers()
               select (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
               {
                   Method = MakeMethod(
                       name,
                       new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                       ImmutableArray<StatementNode>.Empty,
                       mods),
               };
    }

    private static Gen<MemberDeclarationNode> GenConstructorMember(
        string className,
        bool hasBaseClass)
    {
        var genChain = hasBaseClass
            ? Gen.Elements<ConstructorChainNode?>(
                null,
                new BaseChainConstructorChainNode { Arguments = null },
                new BaseChainConstructorChainNode
                {
                    Arguments = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = MakeIntLiteral("0") } },
                })
            : Gen.Elements<ConstructorChainNode?>(
                null,
                new ThisChainConstructorChainNode { Arguments = null });

        return from mods in GenModifiers()
               from hasParam in Gen.Elements(true, false)
               from chain in genChain
               let parameters = hasParam
                   ? new ParameterListNode
                   {
                       Parameter = new ParameterNode
                       {
                           Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                           {
                               PrimitiveType = new IntTypePrimitiveTypeNode(),
                           } },
                           Name = Tok("val"),
                       },
                   }
                   : null
               select (MemberDeclarationNode)new ConstructorMemberMemberDeclarationNode
               {
                   Constructor = MakeConstructor(className, parameters, chain, modifiers: mods),
               };
    }

    /// <summary>
    /// Generates a random set of class declarations, some with inheritance clauses.
    /// </summary>
    private static Gen<InheritanceRoundTripTestCase> GenTestCase()
    {
        // Strategy: generate 1-3 classes where derived classes may reference earlier classes as base.
        // We use a fixed-size approach to avoid Gen.ArrayOf sizing issues.
        var gen1Class = from idx in Gen.Choose(0, ClassNames.Length - 1)
                        from memberCount in Gen.Choose(0, 2)
                        from seed in Gen.Choose(0, 10000)
                        select BuildTestCase(
                            new[] { idx },
                            new[] { memberCount },
                            new[] { false },
                            seed);

        var gen2Classes = from idx0 in Gen.Choose(0, ClassNames.Length - 1)
                          from idx1 in Gen.Choose(0, ClassNames.Length - 1)
                          from mc0 in Gen.Choose(0, 2)
                          from mc1 in Gen.Choose(0, 2)
                          from inh1 in Gen.Elements(true, false)
                          from seed in Gen.Choose(0, 10000)
                          select BuildTestCase(
                              new[] { idx0, idx1 },
                              new[] { mc0, mc1 },
                              new[] { false, inh1 },
                              seed);

        var gen3Classes = from idx0 in Gen.Choose(0, ClassNames.Length - 1)
                          from idx1 in Gen.Choose(0, ClassNames.Length - 1)
                          from idx2 in Gen.Choose(0, ClassNames.Length - 1)
                          from mc0 in Gen.Choose(0, 2)
                          from mc1 in Gen.Choose(0, 2)
                          from mc2 in Gen.Choose(0, 2)
                          from inh1 in Gen.Elements(true, false)
                          from inh2 in Gen.Elements(true, false)
                          from seed in Gen.Choose(0, 10000)
                          select BuildTestCase(
                              new[] { idx0, idx1, idx2 },
                              new[] { mc0, mc1, mc2 },
                              new[] { false, inh1, inh2 },
                              seed);

        return Gen.OneOf(gen1Class, gen2Classes, gen3Classes);
    }

    private static InheritanceRoundTripTestCase BuildTestCase(
        int[] classIndices,
        int[] memberCounts,
        bool[] hasInheritance,
        int seed)
    {
        var rng = new Random(seed);
        var classDecls = new List<ClassDeclarationNode>();
        var declaredNames = new List<string>();

        for (var i = 0; i < classIndices.Length; i++)
        {
            // Ensure unique class names
            var baseName = ClassNames[classIndices[i]];
            var name = baseName;
            var suffix = 0;
            while (declaredNames.Contains(name))
            {
                suffix++;
                name = baseName + suffix;
            }
            declaredNames.Add(name);

            // Determine base class: only if there's a previously declared class and hasInheritance is true
            string? baseClassName = null;
            if (hasInheritance[i] && i > 0)
            {
                baseClassName = declaredNames[rng.Next(i)];
            }

            // Build members
            var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
            var memberCount = memberCounts[i];

            for (var m = 0; m < memberCount; m++)
            {
                // Alternate between fields and methods to keep it simple
                if (m % 2 == 0)
                {
                    var fieldName = FieldNames[rng.Next(FieldNames.Length)];
                    members.Add(new FieldMemberMemberDeclarationNode
                    {
                        Field = MakeField(
                            fieldName,
                            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } }),
                    });
                }
                else
                {
                    var methodName = MethodNames[rng.Next(MethodNames.Length)];
                    members.Add(new MethodMemberMemberDeclarationNode
                    {
                        Method = MakeMethod(
                            methodName,
                            new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                            ImmutableArray<StatementNode>.Empty,
                            ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode())),
                    });
                }
            }

            // Optionally add a constructor with base chain for derived classes
            if (baseClassName is not null && rng.Next(2) == 0)
            {
                members.Add(new ConstructorMemberMemberDeclarationNode
                {
                    Constructor = MakeConstructor(
                        name,
                        chain: new BaseChainConstructorChainNode { Arguments = null },
                        modifiers: ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode())),
                });
            }

            var modifiers = rng.Next(2) == 0
                ? ImmutableArray<ModifierNode>.Empty
                : ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode());

            classDecls.Add(MakeClassDecl(name, baseClassName, members.ToImmutable(), modifiers));
        }

        var description = string.Join(", ",
            classDecls.Select(c =>
            {
                var basePart = c.BaseClause is not null ? $" : {c.BaseList.First.Name.Lexeme}" : "";
                return $"{c.Name.Lexeme}{basePart}";
            }));

        return new InheritanceRoundTripTestCase(description, MakeCompilationUnit(classDecls.ToArray()));
    }

    /// <summary>FsCheck Arbitrary registration for InheritanceRoundTripTestCase.</summary>
    public static Arbitrary<InheritanceRoundTripTestCase> ArbitraryInheritanceRoundTripTestCase() =>
        GenTestCase().ToArbitrary();

    // ═════════════════════════════════════════════════════════════════════
    // Property 1: Parse/print round-trip for inheritance clauses
    //
    // **Validates: Requirements 1.1, 1.2, 1.4, 1.5, 1.6**
    //
    // For any valid Mist AST containing class declarations with or without
    // an inheritance clause, pretty-printing → parsing → pretty-printing
    // SHALL produce identical source text.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(InheritanceRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePreservesInheritanceClauses(InheritanceRoundTripTestCase testCase)
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
    ;
    }

}
