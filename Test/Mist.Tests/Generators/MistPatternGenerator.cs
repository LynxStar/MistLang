// Generators for Mist pattern matching AST nodes.
// Produces random but structurally valid programs containing `is` expressions
// with various pattern kinds for property-based testing.
// Feature: pattern-matching
// **Validates: Requirements 2.8, 3.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// Represents a scrutinee variable declaration and a pattern to match against it.
/// </summary>
public record struct ScrutineeAndPattern(
    StatementNode Declaration,
    string ScrutineeName,
    PatternNode Pattern);

/// <summary>
/// FsCheck generators for Mist programs containing <c>is</c> expressions with
/// various pattern kinds (type, constant, var, discard, property, relational,
/// logical, list, parenthesized). Each generated program is a complete
/// <see cref="CompilationUnitNode"/> with a class, a <c>static void Main</c>
/// method, scrutinee variable declarations, and <c>if (scrutinee is pattern)</c>
/// statements so the full compiler pipeline can process them.
/// </summary>
public static class MistPatternGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] BindingNames = ["a", "b", "c", "d", "result"];
    private static readonly string[] PropertyFieldNames = ["Length", "Count", "Value"];

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers — wrap an AdditiveNode into a full ExpressionNode
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps an <see cref="AdditiveNode"/> into a full <see cref="ExpressionNode"/>
    /// through the precedence chain, using an <see cref="IsExpressionRelationalNode"/>
    /// at the relational level.
    /// </summary>
    private static ExpressionNode WrapIsExpression(AdditiveNode scrutinee, PatternNode pattern)
    {
        var rel = new IsExpressionRelationalNode { Left = scrutinee, Pattern = pattern };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates an <see cref="AdditiveNode"/> wrapping a simple identifier expression.
    /// </summary>
    private static AdditiveNode MakeIdentifierAdditive(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        return new AdditiveNode { Left = mult };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern node builders
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Wraps a <see cref="PrimaryPatternNode"/> into a full <see cref="PatternNode"/>.</summary>
    private static PatternNode WrapPrimaryPattern(PrimaryPatternNode primary)
    {
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    /// <summary>Wraps a <see cref="UnaryPatternNode"/> into a full <see cref="PatternNode"/>.</summary>
    private static PatternNode WrapUnaryPattern(UnaryPatternNode unary)
    {
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Primary pattern generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a type pattern: <c>int i</c>, <c>string s</c>.
    /// Scrutinee should be an <c>int</c> or <c>string</c> variable.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenTypePattern()
    {
        var intTypePattern = from bindName in Gen.Elements(BindingNames)
                             select (PrimaryPatternNode)new TypePatternPrimaryPatternNode
                             {
                                 Type = new PrimitiveTypeRefTypeReferenceNode
                                 {
                                     PrimitiveType = new IntTypePrimitiveTypeNode(),
                                 },
                                 Name = Tok(bindName),
                             };

        var stringTypePattern = from bindName in Gen.Elements(BindingNames)
                                select (PrimaryPatternNode)new TypePatternPrimaryPatternNode
                                {
                                    Type = new PrimitiveTypeRefTypeReferenceNode
                                    {
                                        PrimitiveType = new StringTypePrimitiveTypeNode(),
                                    },
                                    Name = Tok(bindName),
                                };

        return Gen.OneOf(intTypePattern, stringTypePattern);
    }

    /// <summary>
    /// Generates a constant pattern: <c>42</c>, <c>"hello"</c>, <c>true</c>.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenConstantPattern()
    {
        var intConstant = Gen.Choose(0, 100).Select(n =>
            (PrimaryPatternNode)new ConstantPatternPrimaryPatternNode
            {
                Literal = new IntegerConstantConstantLiteralNode
                {
                    Value = Tok(n.ToString()),
                },
            });

        var stringConstant = Gen.Elements("\"hello\"", "\"world\"", "\"test\"").Select(s =>
            (PrimaryPatternNode)new ConstantPatternPrimaryPatternNode
            {
                Literal = new StringConstantConstantLiteralNode
                {
                    Value = Tok(s),
                },
            });

        var boolConstant = Gen.Elements(true, false).Select(b =>
            (PrimaryPatternNode)new ConstantPatternPrimaryPatternNode
            {
                Literal = b
                    ? (ConstantLiteralNode)new TrueConstantConstantLiteralNode()
                    : new FalseConstantConstantLiteralNode(),
            });

        return Gen.OneOf(intConstant, stringConstant, boolConstant);
    }

    /// <summary>
    /// Generates a var pattern: <c>var y</c>.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenVarPattern()
    {
        return Gen.Elements(BindingNames).Select(name =>
            (PrimaryPatternNode)new VarPatternPrimaryPatternNode
            {
                Name = Tok(name),
            });
    }

    /// <summary>
    /// Generates a discard pattern: <c>_</c>.
    /// Represented as a type pattern with identifier <c>_</c> and a type,
    /// or as a var pattern with name <c>_</c>. In practice, the parser
    /// recognizes <c>_</c> as a standalone identifier. We use a var pattern
    /// with name <c>_</c> which pretty-prints as <c>var _</c>.
    /// For a true discard, we use a constant-like approach — but actually
    /// the grammar handles <c>_</c> as a type pattern where the type is
    /// an identifier. Let's use a type pattern with a named type <c>_</c>
    /// which won't work. Instead, we generate <c>var _</c> which is the
    /// idiomatic discard in Mist.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenDiscardPattern()
    {
        return Gen.Constant(
            (PrimaryPatternNode)new VarPatternPrimaryPatternNode
            {
                Name = Tok("_"),
            });
    }

    /// <summary>
    /// Generates a relational pattern: <c>&gt; 0</c>, <c>&lt;= 100</c>.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenRelationalPattern()
    {
        var ltPattern = Gen.Choose(1, 100).Select(n =>
            (PrimaryPatternNode)new LessThanRelationalPatternPrimaryPatternNode
            {
                Constant = MakeConstantExpression(n.ToString()),
            });

        var gtPattern = Gen.Choose(0, 99).Select(n =>
            (PrimaryPatternNode)new GreaterThanRelationalPatternPrimaryPatternNode
            {
                Constant = MakeConstantExpression(n.ToString()),
            });

        var ltePattern = Gen.Choose(1, 100).Select(n =>
            (PrimaryPatternNode)new LessThanOrEqualRelationalPatternPrimaryPatternNode
            {
                Constant = MakeConstantExpression(n.ToString()),
            });

        var gtePattern = Gen.Choose(0, 99).Select(n =>
            (PrimaryPatternNode)new GreaterThanOrEqualRelationalPatternPrimaryPatternNode
            {
                Constant = MakeConstantExpression(n.ToString()),
            });

        return Gen.OneOf(ltPattern, gtPattern, ltePattern, gtePattern);
    }

    /// <summary>
    /// Generates a property pattern: <c>{ Length: &gt; 0 }</c>.
    /// Uses no type prefix (anonymous property pattern).
    /// </summary>
    public static Gen<PrimaryPatternNode> GenPropertyPattern()
    {
        return from propName in Gen.Elements(PropertyFieldNames)
               from constVal in Gen.Choose(0, 50)
               select (PrimaryPatternNode)new PropertyPatternPrimaryPatternNode
               {
                   TypeName = null,
                   Subpatterns = new PropertySubpatternListNode
                   {
                       PropertySubpattern = new PropertySubpatternNode
                       {
                           Name = Tok(propName),
                           Pattern = WrapPrimaryPattern(
                               new GreaterThanRelationalPatternPrimaryPatternNode
                               {
                                   Constant = MakeConstantExpression(constVal.ToString()),
                               }),
                       },
                   },
                   Name = null,
               };
    }

    /// <summary>
    /// Generates a list pattern: <c>[1, 2, ..]</c>.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenListPattern()
    {
        var singleElement = Gen.Choose(0, 10).Select(n =>
            (PrimaryPatternNode)new ListPatternPrimaryPatternNode
            {
                Elements = new ListPatternElementListNode
                {
                    ListPatternElement = new PatternElementListPatternElementNode
                    {
                        Pattern = WrapPrimaryPattern(
                            new ConstantPatternPrimaryPatternNode
                            {
                                Literal = new IntegerConstantConstantLiteralNode
                                {
                                    Value = Tok(n.ToString()),
                                },
                            }),
                    },
                    ListPatternElements = ImmutableArray.Create<ListPatternElementNode>(
                        new SliceElementListPatternElementNode()),
                },
            });

        var twoElements = (from a in Gen.Choose(0, 10)
                           from b in Gen.Choose(0, 10)
                           select (PrimaryPatternNode)new ListPatternPrimaryPatternNode
                           {
                               Elements = new ListPatternElementListNode
                               {
                                   ListPatternElement = new PatternElementListPatternElementNode
                                   {
                                       Pattern = WrapPrimaryPattern(
                                           new ConstantPatternPrimaryPatternNode
                                           {
                                               Literal = new IntegerConstantConstantLiteralNode
                                               {
                                                   Value = Tok(a.ToString()),
                                               },
                                           }),
                                   },
                                   ListPatternElements = ImmutableArray.Create<ListPatternElementNode>(
                                       new PatternElementListPatternElementNode
                                       {
                                           Pattern = WrapPrimaryPattern(
                                               new ConstantPatternPrimaryPatternNode
                                               {
                                                   Literal = new IntegerConstantConstantLiteralNode
                                                   {
                                                       Value = Tok(b.ToString()),
                                                   },
                                               }),
                                       },
                                       new SliceElementListPatternElementNode()),
                               },
                           });

        return Gen.OneOf(singleElement, twoElements);
    }

    /// <summary>
    /// Generates a parenthesized pattern: <c>(&gt; 0 and &lt; 100)</c>.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenParenthesizedPattern()
    {
        return from lo in Gen.Choose(0, 49)
               from hi in Gen.Choose(50, 100)
               select (PrimaryPatternNode)new ParenthesizedPatternPrimaryPatternNode
               {
                   Inner = MakeAndPattern(
                       new GreaterThanRelationalPatternPrimaryPatternNode
                       {
                           Constant = MakeConstantExpression(lo.ToString()),
                       },
                       new LessThanRelationalPatternPrimaryPatternNode
                       {
                           Constant = MakeConstantExpression(hi.ToString()),
                       }),
               };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Compound pattern generators (logical combinators)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a logical <c>and</c> pattern: <c>&gt; 0 and &lt; 100</c>.
    /// </summary>
    public static Gen<PatternNode> GenAndPatternNode()
    {
        return from lo in Gen.Choose(0, 49)
               from hi in Gen.Choose(50, 100)
               select MakeAndPattern(
                   new GreaterThanRelationalPatternPrimaryPatternNode
                   {
                       Constant = MakeConstantExpression(lo.ToString()),
                   },
                   new LessThanRelationalPatternPrimaryPatternNode
                   {
                       Constant = MakeConstantExpression(hi.ToString()),
                   });
    }

    /// <summary>
    /// Generates a logical <c>or</c> pattern: <c>int or string</c>.
    /// Uses type patterns for both alternatives.
    /// </summary>
    public static Gen<PatternNode> GenOrPatternNode()
    {
        return from leftBind in Gen.Elements(BindingNames)
               from rightBind in Gen.Elements(BindingNames)
               where leftBind != rightBind
               select MakeOrPattern(
                   new TypePatternPrimaryPatternNode
                   {
                       Type = new PrimitiveTypeRefTypeReferenceNode
                       {
                           PrimitiveType = new IntTypePrimitiveTypeNode(),
                       },
                       Name = Tok(leftBind),
                   },
                   new TypePatternPrimaryPatternNode
                   {
                       Type = new PrimitiveTypeRefTypeReferenceNode
                       {
                           PrimitiveType = new StringTypePrimitiveTypeNode(),
                       },
                       Name = Tok(rightBind),
                   });
    }

    /// <summary>
    /// Generates a <c>not</c> pattern: <c>not 0</c>.
    /// </summary>
    public static Gen<PatternNode> GenNotPatternNode()
    {
        return Gen.Choose(0, 10).Select(n =>
        {
            var primary = new ConstantPatternPrimaryPatternNode
            {
                Literal = new IntegerConstantConstantLiteralNode
                {
                    Value = Tok(n.ToString()),
                },
            };
            var notUnary = new NotPatternUnaryPatternNode { Operand = primary };
            return WrapUnaryPattern(notUnary);
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern → full PatternNode generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random <see cref="PatternNode"/> from any of the supported
    /// primary pattern kinds (no logical combinators).
    /// </summary>
    public static Gen<PatternNode> GenSimplePattern()
    {
        return Gen.OneOf(
            GenTypePattern().Select(WrapPrimaryPattern),
            GenConstantPattern().Select(WrapPrimaryPattern),
            GenVarPattern().Select(WrapPrimaryPattern),
            GenDiscardPattern().Select(WrapPrimaryPattern),
            GenRelationalPattern().Select(WrapPrimaryPattern),
            GenPropertyPattern().Select(WrapPrimaryPattern),
            GenListPattern().Select(WrapPrimaryPattern),
            GenParenthesizedPattern().Select(WrapPrimaryPattern));
    }

    /// <summary>
    /// Generates a random <see cref="PatternNode"/> including logical combinators.
    /// </summary>
    public static Gen<PatternNode> GenPattern()
    {
        return Gen.OneOf(
            GenSimplePattern(),
            GenAndPatternNode(),
            GenOrPatternNode(),
            GenNotPatternNode());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scrutinee + pattern pairing
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scrutinee variable declaration paired with a compatible pattern.
    /// The scrutinee type is chosen to be compatible with the generated pattern kind.
    /// </summary>
    public static Gen<ScrutineeAndPattern> GenScrutineeAndPattern()
    {
        var intScrutinee = from name in Gen.Elements(ScrutineeNames)
                           from value in Gen.Choose(0, 100)
                           from pattern in Gen.OneOf(
                               GenTypePattern().Select(WrapPrimaryPattern),
                               GenConstantPattern().Select(WrapPrimaryPattern),
                               GenVarPattern().Select(WrapPrimaryPattern),
                               GenDiscardPattern().Select(WrapPrimaryPattern),
                               GenRelationalPattern().Select(WrapPrimaryPattern),
                               GenParenthesizedPattern().Select(WrapPrimaryPattern),
                               GenAndPatternNode(),
                               GenNotPatternNode())
                           select new ScrutineeAndPattern(
                               MistAstGenerator.MakeIntVarDecl(name, value.ToString()),
                               name,
                               pattern);

        var stringScrutinee = from name in Gen.Elements(ScrutineeNames)
                              from strVal in Gen.Elements("\"hello\"", "\"world\"", "\"test\"")
                              from pattern in Gen.OneOf(
                                  GenTypePattern().Select(WrapPrimaryPattern),
                                  GenConstantPattern().Select(WrapPrimaryPattern),
                                  GenVarPattern().Select(WrapPrimaryPattern),
                                  GenDiscardPattern().Select(WrapPrimaryPattern),
                                  GenPropertyPattern().Select(WrapPrimaryPattern))
                              select new ScrutineeAndPattern(
                                  MistAstGenerator.MakeStringVarDecl(name, strVal),
                                  name,
                                  pattern);

        return Gen.OneOf(intScrutinee, stringScrutinee);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Complete program generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a complete <see cref="CompilationUnitNode"/> with a class containing
    /// a <c>static void Main</c> method that declares a scrutinee variable and uses
    /// an <c>if (scrutinee is pattern)</c> statement.
    /// </summary>
    public static Gen<CompilationUnitNode> GenPatternMatchingProgram()
    {
        return from sp in GenScrutineeAndPattern()
               select MakeProgramWithIsExpression(sp);
    }

    /// <summary>
    /// Generates a program with multiple <c>is</c> expressions using different pattern kinds.
    /// </summary>
    public static Gen<CompilationUnitNode> GenMultiPatternProgram()
    {
        return from sp1 in GenScrutineeAndPattern()
               from sp2 in GenScrutineeAndPattern()
               select MakeProgramWithMultipleIsExpressions(sp1, sp2);
    }

    /// <summary>
    /// Generates a random pattern matching program (single or multi-pattern).
    /// </summary>
    public static Gen<CompilationUnitNode> GenAnyPatternProgram()
    {
        return Gen.OneOf(
            GenPatternMatchingProgram(),
            GenMultiPatternProgram());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a program with a single <c>if (scrutinee is pattern) { }</c> statement.
    /// </summary>
    private static CompilationUnitNode MakeProgramWithIsExpression(ScrutineeAndPattern sp)
    {
        var scrutineeAdditive = MakeIdentifierAdditive(sp.ScrutineeName);
        var isExpr = WrapIsExpression(scrutineeAdditive, sp.Pattern);

        var ifStatement = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = isExpr,
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray<StatementNode>.Empty,
                },
            },
        };

        var statements = ImmutableArray.Create(
            sp.Declaration,
            (StatementNode)ifStatement);

        return MistProgramGenerator.MakeProgram(statements);
    }

    /// <summary>
    /// Creates a program with two <c>if (scrutinee is pattern) { }</c> statements
    /// using different scrutinee names to avoid conflicts.
    /// </summary>
    private static CompilationUnitNode MakeProgramWithMultipleIsExpressions(
        ScrutineeAndPattern sp1,
        ScrutineeAndPattern sp2)
    {
        // Ensure distinct scrutinee names by appending a suffix to the second
        var name1 = sp1.ScrutineeName;
        var name2 = sp2.ScrutineeName == name1 ? sp2.ScrutineeName + "2" : sp2.ScrutineeName;

        var isExpr1 = WrapIsExpression(MakeIdentifierAdditive(name1), sp1.Pattern);
        var ifStmt1 = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = isExpr1,
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray<StatementNode>.Empty,
                },
            },
        };

        var isExpr2 = WrapIsExpression(MakeIdentifierAdditive(name2), sp2.Pattern);
        var ifStmt2 = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = isExpr2,
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray<StatementNode>.Empty,
                },
            },
        };

        // Rebuild the second declaration with the adjusted name if needed
        var decl2 = name2 != sp2.ScrutineeName
            ? RebuildDeclarationWithName(sp2.Declaration, name2)
            : sp2.Declaration;

        var statements = ImmutableArray.Create(
            sp1.Declaration,
            decl2,
            (StatementNode)ifStmt1,
            (StatementNode)ifStmt2);

        return MistProgramGenerator.MakeProgram(statements);
    }

    /// <summary>
    /// Rebuilds a variable declaration statement with a new name.
    /// </summary>
    private static StatementNode RebuildDeclarationWithName(StatementNode decl, string newName)
    {
        if (decl is VariableDeclarationStatementStatementNode varDeclStmt)
        {
            if (varDeclStmt.VariableDeclaration is TypedVariableDeclarationVariableDeclarationStatementNode typed)
            {
                return new VariableDeclarationStatementStatementNode
                {
                    VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                    {
                        Type = typed.Type,
                        Name = Tok(newName),
                        Initializer = typed.Initializer,
                    },
                };
            }
        }
        return decl;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Creates a constant integer expression for use in relational patterns.</summary>
    private static ExpressionNode MakeConstantExpression(string value)
    {
        return MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(value) });
    }

    /// <summary>Creates an <c>and</c> pattern from two primary patterns.</summary>
    private static PatternNode MakeAndPattern(PrimaryPatternNode left, PrimaryPatternNode right)
    {
        var leftUnary = new DirectPatternUnaryPatternNode { Primary = left };
        var rightUnary = new DirectPatternUnaryPatternNode { Primary = right };
        var and = new AndPatternNode { Left = leftUnary, Right = rightUnary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    /// <summary>Creates an <c>or</c> pattern from two primary patterns.</summary>
    private static PatternNode MakeOrPattern(PrimaryPatternNode left, PrimaryPatternNode right)
    {
        var leftUnary = new DirectPatternUnaryPatternNode { Primary = left };
        var rightUnary = new DirectPatternUnaryPatternNode { Primary = right };
        var leftAnd = new AndPatternNode { Left = leftUnary };
        var rightAnd = new AndPatternNode { Left = rightUnary };
        var or = new OrPatternNode { Left = leftAnd, Right = rightAnd };
        return new PatternNode { OrPattern = or };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registrations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>FsCheck Arbitrary for pattern matching programs.</summary>
    public static Arbitrary<CompilationUnitNode> ArbitraryPatternProgram() =>
        GenAnyPatternProgram().ToArbitrary();
}
