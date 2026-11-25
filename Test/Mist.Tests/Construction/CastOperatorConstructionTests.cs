using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

/// <summary>
/// The shape a cast operator declaration must have (cast-operators spec Requirements 6.3 and 6.4, design
/// Property 4: exactly one parameter, one refusal whatever the wrong count) and the 'static' its
/// descriptor requires. Each declaration still becomes a cast operator (Requirement 5.1) unless it has
/// no parameter to convert from.
/// </summary>
[Trait("Tier", "2")]
public sealed class CastOperatorConstructionTests
{
    private const int CastOffset = 40;

    private readonly BindingFragments f = new();

    [Fact]
    public void ACastOperatorWithoutStaticIsRefusedAndStillRecorded()
    {
        var holder = Build(Cast(isStatic: false, "value"));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0333", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(CastOffset + 1, diagnostic.Position().Start.Column);
        var recorded = Assert.Single(holder.CastOperatorsList);
        Assert.False(recorded.Modifiers.IsStatic);
        Assert.Equal("value", recorded.SourceParameter.Name);
    }

    [Fact]
    public void ACastOperatorWithNoParameterIsRefusedAndNotRecorded()
    {
        var holder = Build(Cast(isStatic: true));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0331", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(CastOffset + 1, diagnostic.Position().Start.Column);
        Assert.Empty(holder.CastOperatorsList);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ACastOperatorWithExtraParametersIsRefusedOnceAndConvertsFromTheFirst(int count)
    {
        var names = Enumerable.Range(0, count).Select(i => $"p{i}").ToArray();

        var holder = Build(Cast(isStatic: true, names));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0332", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(CastOffset + 1, diagnostic.Position().Start.Column);
        Assert.Equal("p0", Assert.Single(holder.CastOperatorsList).SourceParameter.Name);
    }

    [Fact]
    public void ANonStaticCastOperatorWithNoParameterIsRefusedForBoth()
    {
        var holder = Build(Cast(isStatic: false));

        var refusals = f.Diagnostics.ToImmutableArray();
        Assert.Equal(["MIST0331", "MIST0333"], refusals.Select(diagnostic => diagnostic.Number.Value).Order());
        Assert.All(refusals, diagnostic => Assert.Equal(CastOffset + 1, diagnostic.Position().Start.Column));
        Assert.Empty(holder.CastOperatorsList);
    }

    [Fact]
    public void AStaticCastOperatorWithOneParameterIsAdmitted()
    {
        var holder = Build(Cast(isStatic: true, "value"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var recorded = Assert.Single(holder.CastOperatorsList);
        Assert.True(recorded.Modifiers.IsStatic);
        Assert.True(recorded.IsImplicit);
        Assert.Equal("value", recorded.SourceParameter.Name);
        Assert.Equal("Int32", recorded.TargetType.Name);
    }

    private ClassType Build(CastOperatorDeclarationNode cast)
    {
        var body = BindingFragments.At<TypeBodyDeclarationNode>(10);
        body.Name = BindingFragments.Token("Holder");
        body.Keyword = new ClassKeywordTypeKeywordNode();
        body.Members = [new CastOperatorMemberMemberDeclarationNode { CastOperator = cast }];

        var holder = new ClassType("Holder", "Probe", f.Types, null, new BodyTypeTypeDeclarationNode { TypeBody = body });
        new ConcreteTypeBuilder(f.Builder, f.Builder.Resolver).Build(holder);
        return holder;
    }

    private static CastOperatorDeclarationNode Cast(bool isStatic, params string[] parameters)
    {
        var cast = BindingFragments.At<CastOperatorDeclarationNode>(CastOffset);
        cast.Modifiers = isStatic ? [new StaticModifierModifierNode()] : [];
        cast.CastKind = new ImplicitCastCastOperatorKindNode();
        cast.TargetType = BindingFragments.TypeName("Int32");
        cast.Parameters = Parameters(parameters);
        cast.Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode() };
        return cast;
    }

    private static ParameterListNode? Parameters(string[] names)
    {
        if(names.Length == 0)
        {
            return null;
        }

        var parameters = names
            .Select(name => new ParameterNode { Type = BindingFragments.TypeName("Int32"), Name = BindingFragments.Token(name) })
            .ToList();

        return new ParameterListNode { Parameter = parameters[0], Parameters = [.. parameters.Skip(1)] };
    }
}