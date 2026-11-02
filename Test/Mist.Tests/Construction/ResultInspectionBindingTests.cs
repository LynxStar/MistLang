using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Storm.Runtime;
using TokenKind = Mist.Generated.TokenKind;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ResultInspectionBindingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TagProofSelectsThePayloadBeforeItsBinding(bool success)
    {
        var shapes = new ResultModel();
        foreach(var type in new[] { shapes.Boolean, shapes.Integer, shapes.Text, shapes.Void })
        {
            _ = shapes.Built.Model.RegisterType((MistType)type);
        }

        var optional = shapes.Built.Interner.GetOrCreateUnionType([shapes.Integer, shapes.Void]);
        var context = new BuilderContext(shapes.Built.Model, new Mist.Common.Diagnostics.DiagnosticBag(), NullAmbientTypeProvider.Instance);
        var binding = new BindingContext(new BodyScope(new Scope(null)), null);
        var syntax = Wrap(new TuplePositionalPatternPrimaryPatternNode
        {
            First = Wrap(new ConstantPatternPrimaryPatternNode { Literal = success ? new TrueConstantConstantLiteralNode() : new FalseConstantConstantLiteralNode() }),
            Rest = Wrap(new VarPatternPrimaryPatternNode { Name = Name("payload") }),
        });
        var pattern = Assert.IsType<TuplePattern>(PatternBinder.Bind(syntax, shapes.Inspection(optional, shapes.Text), binding, context));
        Assert.Same(success ? optional : shapes.Text, pattern.Elements[1].Binding!.Type);
        Assert.Equal(!success, pattern.Elements[1].Binding!.IsKnownResultError);
        Assert.NotNull(pattern.Constituent);
        Assert.Empty(context.Diagnostics.ToImmutableArray());
    }

    private static PatternNode Wrap(PrimaryPatternNode primary)
    {
        return new PatternNode { OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } } };
    }

    private static Token Name(string name)
    {
        return new Token((int)TokenKind.IDENTIFIER, default, default, default, TokenChannel.Default, name);
    }
}