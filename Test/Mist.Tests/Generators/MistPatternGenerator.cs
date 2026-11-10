using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

public record struct ScrutineeAndPattern(
    StatementNode Declaration,
    string ScrutineeName,
    PatternNode Pattern);

public static class MistPatternGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] BindingNames = ["a", "b", "c", "d", "result"];
    private static readonly string[] PropertyFieldNames = ["Length", "Count", "Value"];

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers — wrap an AdditiveNode into a full ExpressionNode
    // ═══════════════════════════════════════════════════════════════════════

    private static ExpressionNode WrapIsExpression(AdditiveNode scrutinee, PatternNode pattern)
    {
        var rel = new IsExpressionRelationalNode { Left = scrutinee, Pattern = pattern };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static AdditiveNode MakeIdentifierAdditive(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        return new AdditiveNode { Left = mult };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern node builders
    // ═══════════════════════════════════════════════════════════════════════

    private static PatternNode WrapPrimaryPattern(PrimaryPatternNode primary)
    {
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

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
    /// Scrutinee should be an <c>int</c> or <c>string</c> variable.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenTypePattern()
    {
        var intTypePattern = from bindName in Gen.Elements(BindingNames)
                             select (PrimaryPatternNode)new TypePatternPrimaryPatternNode
                             {
                                 Type = new TypeReferenceNode
                                 {
                                     First = new PrimitiveSingleTypeRefSingleTypeRefNode
                                     {
                                         PrimitiveType = new IntTypePrimitiveTypeNode(),
                                     }
                                 },
                                 Name = Tok(bindName),
                             };

        var stringTypePattern = from bindName in Gen.Elements(BindingNames)
                                select (PrimaryPatternNode)new TypePatternPrimaryPatternNode
                                {
                                    Type = new TypeReferenceNode
                                    {
                                        First = new PrimitiveSingleTypeRefSingleTypeRefNode
                                        {
                                            PrimitiveType = new StringTypePrimitiveTypeNode(),
                                        }
                                    },
                                    Name = Tok(bindName),
                                };

        return Gen.OneOf(intTypePattern, stringTypePattern);
    }

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
                    ? new TrueConstantConstantLiteralNode()
                    : new FalseConstantConstantLiteralNode(),
            });

        return Gen.OneOf(intConstant, stringConstant, boolConstant);
    }

    public static Gen<PrimaryPatternNode> GenVarPattern()
    {
        return Gen.Elements(BindingNames).Select(name =>
            (PrimaryPatternNode)new VarPatternPrimaryPatternNode
            {
                Name = Tok(name),
            });
    }

    /// <summary>
    /// Both forms are valid catch-all patterns in Mist.
    /// </summary>
    public static Gen<PrimaryPatternNode> GenDiscardPattern()
    {
        return Gen.OneOf(
            Gen.Constant((PrimaryPatternNode)new DiscardPatternPrimaryPatternNode
            {
                DISCARD = Tok("_"),
            }),
            Gen.Constant((PrimaryPatternNode)new VarPatternPrimaryPatternNode
            {
                Name = Tok("_"),
            }));
    }

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

        var twoElements = from a in Gen.Choose(0, 10)
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
                          };

        return Gen.OneOf(singleElement, twoElements);
    }

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

    public static Gen<PatternNode> GenOrPatternNode()
    {
        return from leftBind in Gen.Elements(BindingNames)
               from rightBind in Gen.Elements(BindingNames)
               where leftBind != rightBind
               select MakeOrPattern(
                   new TypePatternPrimaryPatternNode
                   {
                       Type = new TypeReferenceNode
                       {
                           First = new PrimitiveSingleTypeRefSingleTypeRefNode
                           {
                               PrimitiveType = new IntTypePrimitiveTypeNode(),
                           }
                       },
                       Name = Tok(leftBind),
                   },
                   new TypePatternPrimaryPatternNode
                   {
                       Type = new TypeReferenceNode
                       {
                           First = new PrimitiveSingleTypeRefSingleTypeRefNode
                           {
                               PrimitiveType = new StringTypePrimitiveTypeNode(),
                           }
                       },
                       Name = Tok(rightBind),
                   });
    }

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

    public static Gen<CompilationUnitNode> GenPatternMatchingProgram()
    {
        return from sp in GenScrutineeAndPattern()
               select MakeProgramWithIsExpression(sp);
    }

    public static Gen<CompilationUnitNode> GenMultiPatternProgram()
    {
        return from sp1 in GenScrutineeAndPattern()
               from sp2 in GenScrutineeAndPattern()
               select MakeProgramWithMultipleIsExpressions(sp1, sp2);
    }

    public static Gen<CompilationUnitNode> GenAnyPatternProgram()
    {
        return Gen.OneOf(
            GenPatternMatchingProgram(),
            GenMultiPatternProgram());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AST construction helpers
    // ═══════════════════════════════════════════════════════════════════════

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
            ifStatement);

        return MistProgramGenerator.MakeProgram(statements);
    }

    private static CompilationUnitNode MakeProgramWithMultipleIsExpressions(
        ScrutineeAndPattern sp1,
        ScrutineeAndPattern sp2)
    {
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

        var decl2 = name2 != sp2.ScrutineeName
            ? RebuildDeclarationWithName(sp2.Declaration, name2)
            : sp2.Declaration;

        var statements = ImmutableArray.Create(
            sp1.Declaration,
            decl2,
            ifStmt1,
            ifStmt2);

        return MistProgramGenerator.MakeProgram(statements);
    }

    private static StatementNode RebuildDeclarationWithName(StatementNode decl, string newName)
    {
        if(decl is VariableDeclarationStatementStatementNode varDeclStmt)
        {
            if(varDeclStmt.VariableDeclaration is TypedVariableDeclarationVariableDeclarationStatementNode typed)
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

    /// <remarks>
    /// The relational-pattern <c>Constant</c> property is bounded to <see cref="VoidCoalescingNode"/>
    /// (the structural analogue of Roslyn's coalescing precedence floor), so this returns the
    /// <c>VoidCoalescing</c> node from the built expression chain.
    /// </remarks>
    private static VoidCoalescingNode MakeConstantExpression(string value)
    {
        return MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(value) })
            .AsAssignment().Target;
    }

    private static PatternNode MakeAndPattern(PrimaryPatternNode left, PrimaryPatternNode right)
    {
        var leftUnary = new DirectPatternUnaryPatternNode { Primary = left };
        var rightUnary = new DirectPatternUnaryPatternNode { Primary = right };
        var and = new AndPatternNode { Left = leftUnary, UnaryPatterns = [rightUnary] };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakeOrPattern(PrimaryPatternNode left, PrimaryPatternNode right)
    {
        var leftUnary = new DirectPatternUnaryPatternNode { Primary = left };
        var rightUnary = new DirectPatternUnaryPatternNode { Primary = right };
        var leftAnd = new AndPatternNode { Left = leftUnary };
        var rightAnd = new AndPatternNode { Left = rightUnary };
        var or = new OrPatternNode { Left = leftAnd, AndPatterns = [rightAnd] };
        return new PatternNode { OrPattern = or };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registrations
    // ═══════════════════════════════════════════════════════════════════════

    public static Arbitrary<CompilationUnitNode> ArbitraryPatternProgram()
    {
        return GenAnyPatternProgram().ToArbitrary();
    }
}