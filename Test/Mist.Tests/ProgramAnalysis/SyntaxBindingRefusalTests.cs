using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: direct binders consume hand-built syntax fragments with explicit stub types.
[Trait("Tier", "2")]
public sealed class SyntaxBindingRefusalTests
{
    [Theory]
    [InlineData("local-function-generic", "MIST_PM_LOCAL_FUNCTION_GENERIC")]
    [InlineData("local-function-with-sealed-modifier", "MIST_PM_LOCAL_FUNCTION_MODIFIER")]
    [InlineData("duplicate-local-function", "MIST_PM_DUPLICATE_LOCAL_FUNCTION")]
    [InlineData("local-after-local-function-of-the-same-name", "MIST_PM_DUPLICATE_LOCAL")]
    [InlineData("abstract-constructed", "MIST_PM_ABSTRACT_CONSTRUCTED")]
    [InlineData("anonymous-member-with-no-name", "MIST_PM_ANONYMOUS_MEMBER_NOT_NAMEABLE")]
    [InlineData("from-end-index-into-an-uncountable-receiver", "MIST_PM_RECEIVER_NOT_COUNTABLE")]
    [InlineData("list-pattern-over-an-uncountable-receiver", "MIST_PM_RECEIVER_NOT_COUNTABLE")]
    public void BinderRefusesTheOriginalSyntaxShape(string refusal, string diagnosticId)
    {
        var f = new BindingFragments();
        if(refusal is "local-function-generic" or "local-function-with-sealed-modifier" or "duplicate-local-function" or "local-after-local-function-of-the-same-name")
        {
            var function = BindingFragments.At<LocalFunctionDeclarationNode>();
            function.Name = BindingFragments.Token("Twice");
            function.ReturnType = BindingFragments.TypeName("Int32");
            function.Body = new BlockNode { Statements = [] };
            function.Modifiers = refusal == "local-function-with-sealed-modifier" ? [new SealedModifierModifierNode()] : [];
            function.TypeParams = refusal == "local-function-generic"
                ? new TypeParameterListNode { TypeParameter = new RegularTypeParameterTypeParameterNode { Name = BindingFragments.Token("T") } }
                : null;
            function.Parameters = new ParameterListNode { Parameter = new ParameterNode { Name = BindingFragments.Token("x"), Type = BindingFragments.TypeName("Int32"), Attributes = [] } };
            var statements = new List<StatementNode> { new LocalFunctionDeclarationStatementStatementNode { LocalFunction = function } };
            if(refusal == "duplicate-local-function")
            {
                var later = BindingFragments.At<LocalFunctionDeclarationNode>();
                later.Name = function.Name;
                later.ReturnType = function.ReturnType;
                later.Body = function.Body;
                later.Parameters = function.Parameters;
                statements.Add(new LocalFunctionDeclarationStatementStatementNode { LocalFunction = later });
            }

            LocalFunctionBinder.Declare(statements, f.Binding, f.Builder);
            if(refusal == "local-after-local-function-of-the-same-name")
            {
                _ = StatementBinder.BindStatement(new VariableDeclarationStatementStatementNode
                {
                    VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
                    {
                        Name = function.Name,
                        Initializer = BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") }),
                    },
                }, f.Binding, f.Builder);
            }
            else
            {
                var invoke = new InvocationSuffixPostfixSuffixNode
                {
                    Arguments = BindingFragments.Arguments(BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("2") })),
                };
                _ = f.Bind(BindingFragments.Identifier("Twice"), invoke);
            }
        }
        else if(refusal == "abstract-constructed")
        {
            f.Register(new AbstractType("Shape", "Probe", f.Types, null, null));
            var creation = BindingFragments.At<ObjectCreationExpressionPrimaryNode>();
            creation.Type = BindingFragments.Name("Shape");
            _ = Assert.IsType<ErrorExpression>(ExpressionBinder.BindPrimary(creation, f.Binding, f.Builder));
        }
        else if(refusal == "anonymous-member-with-no-name")
        {
            var projection = BindingFragments.At<PostfixNode>();
            projection.Target = BindingFragments.Identifier("Make");
            projection.Suffix = [new InvocationSuffixPostfixSuffixNode()];
            var anonymous = new AnonymousTypeExpressionPrimaryNode
            {
                Properties = new AnonymousPropertyListNode { AnonymousProperty = new ProjectedAnonymousPropertyAnonymousPropertyNode { Projection = projection } },
            };
            _ = Assert.IsType<ErrorExpression>(AnonymousObjectBinder.Bind(anonymous, f.Binding, f.Builder));
        }
        else
        {
            var shelf = HandBuiltModel.Class("Shelf");
            shelf.IndexersList.Add(new Indexer(f.Built.Type("Int32"), [new Parameter("slot", f.Built.Type("Int32"))], DeclarationFragments.Public)
            {
                DeclaringType = shelf,
                Getter = new PropertyAccessor { IsAuto = true },
            });
            _ = f.Local("shelf", shelf);
            if(refusal == "from-end-index-into-an-uncountable-receiver")
            {
                var index = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
                index.Index = BindingFragments.Expression(new FromEndExpressionUnaryNode
                {
                    Operand = new PostfixExpressionUnaryNode { Primary = new PostfixNode { Target = new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("1") } } },
                });
                _ = Assert.IsType<IndexExpression>(f.Bind(BindingFragments.Identifier("shelf"), index));
            }
            else
            {
                var primary = BindingFragments.At<ListPatternPrimaryPatternNode>();
                primary.Elements = new ListPatternElementListNode
                {
                    ListPatternElement = new PatternElementListPatternElementNode { Pattern = Pattern(new VarPatternPrimaryPatternNode { Name = BindingFragments.Token("only") }) },
                };
                var bound = Assert.IsType<ListPattern>(PatternBinder.Bind(Pattern(primary), shelf, f.Binding, f.Builder));
                _ = Assert.Single(bound.Elements);
            }
        }

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        if(refusal == "local-after-local-function-of-the-same-name")
        {
            Assert.Equal("<unknown>", diagnostic.CompilationUnitPath);
        }
        else
        {
            Assert.Equal(31, diagnostic.Location.Start.Column);
        }
    }

    private static PatternNode Pattern(PrimaryPatternNode primary)
    {
        return new()
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
    }
}