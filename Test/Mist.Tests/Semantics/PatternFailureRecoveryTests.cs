using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class PatternFailureRecoveryTests
{
    private const string Path = "Probe/Pattern.mist";

    [Theory]
    [InlineData("var", true)]
    [InlineData("property", true)]
    [InlineData("tuple", true)]
    [InlineData("list", true)]
    [InlineData("nested", true)]
    [InlineData("and", true)]
    [InlineData("var", false)]
    [InlineData("property", false)]
    [InlineData("tuple", false)]
    [InlineData("list", false)]
    [InlineData("nested", false)]
    [InlineData("and", false)]
    public void LegalNamesSurviveAndOnlyReportedScrutineesSuppressConsequentialOverloadFailures(string shape, bool failed)
    {
        var f = new BindingFragments();
        _ = f.Local("source", ErrorType.Instance, failed);

        var result = BindIs(f, "source", Shape(shape));
        var names = shape is "tuple" or "nested" ? new[] { "first", "kept" } : new[] { "kept" };

        Assert.Equal(failed, result is ErrorExpression);
        if(!failed)
        {
            var unfinished = Assert.IsType<IsPatternExpression>(result);
            Assert.False(unfinished.Pattern.IsFailed);
            Assert.Same(ErrorType.Instance, unfinished.Scrutinee.Type);
        }

        foreach(var name in names)
        {
            var reference = Assert.IsType<LocalReferenceExpression>(f.Bind(BindingFragments.Identifier(name)));
            Assert.Same(ErrorType.Instance, reference.Type);
            Assert.Equal(failed, reference.Local.IsFailed);
            _ = Consume(f, name);
        }

        Assert.Equal(failed ? 0 : names.Length, f.Diagnostics.ToImmutableArray().Length);
        Assert.All(f.Diagnostics.ToImmutableArray(), diagnostic =>
        {
            Assert.Equal("MIST0390", diagnostic.Number.Value);
            AssertLocation(diagnostic, 60);
        });
    }

    [Theory]
    [InlineData("var")]
    [InlineData("property")]
    public void AReportedMissingIdentifierSurvivesPatternBindingWithoutAnotherComplaint(string shape)
    {
        var f = new BindingFragments();

        _ = Assert.IsType<ErrorExpression>(BindIs(f, "missing", Shape(shape)));
        Assert.True(f.Scope.Locals["kept"].IsFailed);
        _ = Consume(f, "kept");

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0296", diagnostic.Number.Value);
        AssertLocation(diagnostic, 30);
    }

    [Theory]
    [InlineData("property")]
    [InlineData("tuple")]
    [InlineData("list")]
    public void ValidScrutineesKeepTheirInferredElementTypes(string shape)
    {
        var f = new BindingFragments();
        var integer = f.Built.Type("Int32");
        f.Register(f.Built.Type("Array<,>"));
        var receiverType = shape switch
        {
            "property" => Box(f),
            "tuple" => f.Built.Interner.GetOrCreateTupleType([integer, integer]),
            _ => f.Builder.Resolver.ConstructArray(integer, null),
        };
        _ = f.Local("source", receiverType);

        _ = Assert.IsType<IsPatternExpression>(BindIs(f, "source", Shape(shape)));

        Assert.Same(integer, f.Scope.Locals["kept"].Type);
        Assert.False(f.Scope.Locals["kept"].IsFailed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("or")]
    [InlineData("not")]
    public void RecoveryDoesNotDeclareNamesWhereThePatternCannotEstablishThem(string logical)
    {
        var f = new BindingFragments();
        _ = f.Local("source", ErrorType.Instance, failed: true);
        var primary = Property(Pattern(Var("kept")));
        var pattern = logical == "not"
            ? new PatternNode { OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new NotPatternUnaryPatternNode { Operand = primary } } } }
            : new PatternNode
            {
                OrPattern = new OrPatternNode
                {
                    Left = And(primary),
                    AndPatterns = [And(At<DiscardPatternPrimaryPatternNode>())],
                },
            };

        _ = BindIs(f, "source", pattern);

        Assert.False(f.Scope.Locals.ContainsKey("kept"));
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0348", diagnostic.Number.Value);
        AssertLocation(diagnostic, 40);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Take")]
    public void AValidReceiverStillReportsItsOwnMissingOrNonStorageMember(string member)
    {
        var f = new BindingFragments();
        var box = Box(f);
        _ = BindingFragments.Member(box, "Take", f.Built.Type("Int32"));
        _ = f.Local("source", box);
        var primary = Property(Pattern(Var("kept")), member);

        _ = Assert.IsType<ErrorExpression>(BindIs(f, "source", Pattern(primary)));
        Assert.True(f.Scope.Locals["kept"].IsFailed);
        _ = Consume(f, "kept");

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(member == "Missing" ? "MIST0301" : "MIST0349", diagnostic.Number.Value);
        AssertLocation(diagnostic, 45);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIndependentlyReportedPatternTypeIsRememberedEvenWhenTheScrutineeIsUnfinished(bool property)
    {
        var f = new BindingFragments();
        _ = f.Local("source", ErrorType.Instance);
        PrimaryPatternNode primary;
        if(property)
        {
            var nested = Property(Pattern(Var("kept")));
            nested.TypeName = At<QualifiedIdentifierNode>();
            nested.TypeName.IDENTIFIER = BindingFragments.Token("Nope");
            primary = nested;
        }
        else
        {
            var stated = At<TypePatternPrimaryPatternNode>();
            stated.Type = BindingFragments.TypeName("Nope");
            stated.Type.Span = stated.Span;
            stated.Type.Origin = stated.Origin;
            stated.Type.First.Span = stated.Span;
            stated.Type.First.Origin = stated.Origin;
            stated.Name = BindingFragments.Token("kept");
            primary = stated;
        }

        _ = Assert.IsType<ErrorExpression>(BindIs(f, "source", Pattern(primary)));
        Assert.True(f.Scope.Locals["kept"].IsFailed);
        _ = Consume(f, "kept");

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0298", diagnostic.Number.Value);
        AssertLocation(diagnostic, 40);
    }

    [Fact]
    public void AReportedConjunctTypeMarksBindingsInTheFollowingConjunct()
    {
        var f = new BindingFragments();
        _ = f.Local("source", ErrorType.Instance);
        var stated = At<BareTypePatternPrimaryPatternNode>();
        stated.Type = BindingFragments.TypeName("Nope");
        var pattern = new PatternNode
        {
            OrPattern = new OrPatternNode
            {
                Left = new AndPatternNode
                {
                    Left = new DirectPatternUnaryPatternNode { Primary = stated },
                    UnaryPatterns = [new DirectPatternUnaryPatternNode { Primary = Var("kept") }],
                },
            },
        };

        _ = Assert.IsType<ErrorExpression>(BindIs(f, "source", pattern));
        Assert.True(f.Scope.Locals["kept"].IsFailed);
        _ = Consume(f, "kept");

        Assert.Equal("MIST0298", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void AFailedTypedPropertyAliasCarriesItsRecordedFailureIntoNestedNames()
    {
        var f = new BindingFragments();
        var alias = new TypeAlias("Broken", "Probe", new Scope(null), null, At<TypeAliasDeclarationNode>())
        {
            AliasedType = ErrorType.Instance,
            IsFailed = true,
        };
        f.Register(alias);
        _ = f.Local("source", ErrorType.Instance);
        var primary = Property(Pattern(Var("kept")));
        primary.TypeName = BindingFragments.Name("Broken");

        _ = Assert.IsType<ErrorExpression>(BindIs(f, "source", Pattern(primary)));
        Assert.True(f.Scope.Locals["kept"].IsFailed);
        _ = Consume(f, "kept");

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnInaccessibleMemberRetainsTheMemberForItsOwningAnalysisRule()
    {
        var f = new BindingFragments();
        var box = Box(f);
        var field = new Field("Secret", f.Built.Type("Int32"), OwnershipKind.Unrestricted,
            DeclarationFragments.Public with { Accessibility = Accessibility.Private })
        { DeclaringType = box };
        box.FieldsList.Add(field);
        _ = f.Local("source", box);

        var test = Assert.IsType<IsPatternExpression>(BindIs(f, "source", Pattern(Property(Pattern(Var("kept")), "Secret"))));
        var subpattern = Assert.Single(Assert.IsType<PropertyPattern>(test.Pattern).Subpatterns);

        Assert.Same(field, Assert.IsType<FieldResult>(subpattern.Member).Field);
        Assert.False(f.Scope.Locals["kept"].IsFailed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AFailedSwitchScrutineeCarriesItsProvenanceIntoTheArmsBinding()
    {
        var f = new BindingFragments();
        _ = f.Local("source", ErrorType.Instance, failed: true);
        var selection = At<SwitchExpressionSuffixPostfixSuffixNode>();
        selection.Arms = new SwitchArmListNode
        {
            SwitchArm = new SwitchArmNode
            {
                Pattern = Shape("property"),
                Result = BindingFragments.Expression(BindingFragments.Identifier("kept")),
            },
        };

        _ = Assert.IsType<ErrorExpression>(f.Bind(BindingFragments.Identifier("source"), selection));

        Assert.False(f.Scope.Locals.ContainsKey("kept"));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static PatternNode Shape(string shape)
    {
        var kept = Pattern(Var("kept"));
        return shape switch
        {
            "var" => kept,
            "property" => Pattern(Property(kept)),
            "tuple" => Pattern(Tuple(Pattern(Var("first")), kept)),
            "list" => Pattern(List(kept)),
            "nested" => Pattern(Property(Pattern(Tuple(Pattern(Var("first")), Pattern(List(kept)))))),
            "and" => new PatternNode
            {
                OrPattern = new OrPatternNode
                {
                    Left = new AndPatternNode
                    {
                        Left = new DirectPatternUnaryPatternNode { Primary = At<DiscardPatternPrimaryPatternNode>() },
                        UnaryPatterns = [new DirectPatternUnaryPatternNode { Primary = Property(kept) }],
                    },
                },
            },
            _ => throw new ArgumentException(shape, nameof(shape)),
        };
    }

    private static PrimaryPatternNode Var(string name)
    {
        var primary = At<VarPatternPrimaryPatternNode>();
        primary.Name = BindingFragments.Token(name);
        return primary;
    }

    private static PropertyPatternPrimaryPatternNode Property(PatternNode child, string member = "Value")
    {
        var primary = At<PropertyPatternPrimaryPatternNode>();
        var subpattern = At<PropertySubpatternNode>(45);
        subpattern.Name = BindingFragments.Token(member);
        subpattern.Pattern = child;
        primary.Subpatterns = new PropertySubpatternListNode { PropertySubpattern = subpattern };
        return primary;
    }

    private static PrimaryPatternNode Tuple(PatternNode first, PatternNode second)
    {
        var primary = At<TuplePositionalPatternPrimaryPatternNode>();
        primary.First = first;
        primary.Rest = second;
        return primary;
    }

    private static PrimaryPatternNode List(PatternNode child)
    {
        var primary = At<ListPatternPrimaryPatternNode>();
        primary.Elements = new ListPatternElementListNode
        {
            ListPatternElement = new PatternElementListPatternElementNode { Pattern = child },
            ListPatternElements = [new SliceElementListPatternElementNode { Subpattern = Pattern(At<DiscardPatternPrimaryPatternNode>()) }],
        };
        return primary;
    }

    private static PatternNode Pattern(PrimaryPatternNode primary)
    {
        return new PatternNode { OrPattern = new OrPatternNode { Left = And(primary) } };
    }

    private static AndPatternNode And(PrimaryPatternNode primary)
    {
        return new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } };
    }

    private static Expression BindIs(BindingFragments f, string source, PatternNode pattern)
    {
        var identifier = At<IdentifierExpressionPrimaryNode>(30);
        identifier.Name = BindingFragments.Token(source);
        var sourceExpression = (AssignmentFormExpressionNode)BindingFragments.Expression(identifier);
        var relational = (RelationalComparisonRelationalNode)sourceExpression.Assignment.Target.Left.Left.Left.Left;
        sourceExpression.Assignment.Target.Left.Left.Left.Left = new IsExpressionRelationalNode { Left = relational.Left, Pattern = pattern };
        return ExpressionBinder.BindExpression(sourceExpression, f.Binding, f.Builder);
    }

    private static Expression Consume(BindingFragments f, string argument)
    {
        if(!f.Scope.Locals.ContainsKey("printer"))
        {
            var printer = HandBuiltModel.Class("Printer");
            _ = BindingFragments.Member(printer, "Take", f.Built.Type("Void"), f.Built.Type("Int32"));
            _ = BindingFragments.Member(printer, "Take", f.Built.Type("Void"), f.Built.Type("String"));
            _ = f.Local("printer", printer);
        }

        var call = At<MethodCallSuffixPostfixSuffixNode>(60);
        call.Name = BindingFragments.Token("Take");
        call.Arguments = BindingFragments.Arguments(BindingFragments.Expression(BindingFragments.Identifier(argument)));
        return f.Bind(BindingFragments.Identifier("printer"), call);
    }

    private static ClassType Box(BindingFragments f)
    {
        var box = HandBuiltModel.Class("Box");
        box.FieldsList.Add(new Field("Value", f.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = box });
        f.Register(box);
        return box;
    }

    private static T At<T>(int offset = 40) where T : SyntaxNode, new()
    {
        var location = HandBuiltModel.Node(offset, Path);
        return new T { Span = location.Span, Origin = location.Origin };
    }

    private static void AssertLocation(Diagnostic diagnostic, int offset)
    {
        Assert.Equal(Path, diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, offset + 1), new LinePosition(1, offset + 2)), diagnostic.Position());
    }
}