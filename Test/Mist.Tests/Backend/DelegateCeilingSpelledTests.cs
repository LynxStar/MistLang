using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Backend;

// MIST0028 where a delegate is spelled during emission (variadics Requirement 16.2): once per function and
// declaration, at the use that fixed a pack's width or else the declaration being emitted, and only inside
// the scope that collects the backend's diagnostics.
[Trait("Tier", "1")]
public sealed class DelegateCeilingSpelledTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void AFunctionSpelledTwiceInOneDeclaration_IsRefusedOnce_AndOnceMoreInAnother()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var function = Function(17);

        using(DelegateCeiling.Enter(built.Model, diagnostics))
        {
            using(DelegateCeiling.Emitting(AtLine(3), "Box.Map", "Probe.Box.Map"))
            {
                DelegateCeiling.Spelled(function, 17);
                DelegateCeiling.Spelled(function, 17);
            }

            using(DelegateCeiling.Emitting(AtLine(7), "Box.Fold", "Probe.Box.Fold"))
            {
                DelegateCeiling.Spelled(function, 17);
            }
        }

        var refusals = diagnostics.ToImmutableArray();

        Assert.Equal([3, 7], refusals.Select(refusal => refusal.Position().Start.Line));
        Assert.Contains(" in 'Box.Map' takes 17 inputs", refusals[0].Message);
        Assert.Contains(" in 'Box.Fold' takes 17 inputs", refusals[1].Message);
        Assert.NotEqual(Subject(refusals[0]).Identity, Subject(refusals[1]).Identity);
    }

    [Fact]
    public void ARefusalWithNoNodeToPointAt_IsStillReported_WithAnAbsentLocation()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        using(DelegateCeiling.Enter(built.Model, diagnostics))
        {
            DelegateCeiling.Spelled(Function(16), 16);
            DelegateCeiling.Spelled(Function(17), 17);

            using(DelegateCeiling.Emitting(null, "Box", "Probe.Box"))
            {
                DelegateCeiling.Spelled(Function(18), 18);
            }
        }

        var refusals = diagnostics.ToImmutableArray();

        Assert.Equal(2, refusals.Length);
        Assert.All(refusals, refusal => Assert.Equal(new Absent(AbsenceReason.SyntheticWithoutRoot), refusal.Location));
        Assert.DoesNotContain(" in '", refusals[0].Message);
        Assert.Contains("' in 'Box' takes 18 inputs", refusals[1].Message);
        Assert.Equal([(17, 16), (18, 16)], refusals.Select(refusal => ((int)Quantity(refusal, 1), (int)Quantity(refusal, 2))));
    }

    [Fact]
    public void UnderASpecializationNoRecordedUseClosed_TheRefusalFallsBackToTheDeclarationBeingEmitted()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var pack = new TypeParameter("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

        using(DelegateCeiling.Enter(built.Model, diagnostics))
        using(DelegateCeiling.Emitting(AtLine(5), "Bundle.Map", "Probe.Bundle.Map"))
        using(Monomorph.For([pack], [17])!.Enter())
        {
            DelegateCeiling.Spelled(Function(17), 17);
        }

        var refusal = Assert.Single(diagnostics.ToImmutableArray());

        Assert.Equal(5, refusal.Position().Start.Line);
        Assert.Contains(" in 'Bundle.Map', where 'TArgs' holds 17 types, takes 17 inputs", refusal.Message);
    }

    [Fact]
    public void UnderTwoPacks_TheRefusalNamesBothWidths_AtTheUseThatClosedThemTogether()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var model = built.Model;
        var family = new ClassType("Pair<>", "Probe", new Scope(null), null, null);
        TypeParameter[] packs =
        [
            new("TArgs", 0, family.Scope, family) { PackKind = TypePackKind.Unbracketed },
            new("UArgs", 1, family.Scope, family) { PackKind = TypePackKind.Bracketed },
        ];
        family.TypeParametersList.AddRange(packs);
        model.ConstructorConstraintClosures.RecordType(family, [Pack(9), Pack(9)], AtLine(11));
        model.ConstructorConstraintClosures.RecordType(family, [Pack(9), Pack(8)], AtLine(13));

        using(DelegateCeiling.Enter(model, diagnostics))
        using(DelegateCeiling.Emitting(AtLine(2), "Pair.Zip", "Probe.Pair.Zip"))
        using(Monomorph.For(packs, [9, 8])!.Enter())
        {
            DelegateCeiling.Spelled(Function(17), 17);
        }

        var refusal = Assert.Single(diagnostics.ToImmutableArray());

        Assert.Equal(13, refusal.Position().Start.Line);
        Assert.Contains(" in 'Pair.Zip', where 'TArgs' holds 9 types and 'UArgs' holds 8 types, takes 17 inputs", refusal.Message);
    }

    [Fact]
    public void ALeftDeclarationRestoresTheOneAroundIt()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        using(DelegateCeiling.Enter(built.Model, diagnostics))
        using(DelegateCeiling.Emitting(AtLine(2), "Box", "Probe.Box"))
        {
            using(DelegateCeiling.Emitting(AtLine(4), "Box.Map", "Probe.Box.Map"))
            {
            }

            DelegateCeiling.Spelled(Function(17), 17);
        }

        var refusal = Assert.Single(diagnostics.ToImmutableArray());

        Assert.Equal(2, refusal.Position().Start.Line);
        Assert.Contains("' in 'Box' takes 17 inputs", refusal.Message);
    }

    [Fact]
    public void AnInnerScopeCollectsItsOwnRefusals_AndLeavingItRestoresTheOuterOne()
    {
        var (outer, inner) = (new DiagnosticBag(PipelineStage.Backend), new DiagnosticBag(PipelineStage.Backend));
        var function = Function(17);

        using(DelegateCeiling.Enter(built.Model, outer))
        using(DelegateCeiling.Emitting(AtLine(3), "Box.Map", "Probe.Box.Map"))
        {
            using(DelegateCeiling.Enter(built.Model, inner))
            using(DelegateCeiling.Emitting(AtLine(3), "Box.Map", "Probe.Box.Map"))
            {
                DelegateCeiling.Spelled(function, 17);
            }

            DelegateCeiling.Spelled(function, 17);
        }

        Assert.Equal(3, Assert.Single(inner.ToImmutableArray()).Position().Start.Line);
        Assert.Equal(3, Assert.Single(outer.ToImmutableArray()).Position().Start.Line);
    }

    [Fact]
    public void OutsideAnyScope_NothingIsTrackedOrReported()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        using(DelegateCeiling.Enter(built.Model, diagnostics))
        {
        }

        Assert.Null(DelegateCeiling.Emitting(AtLine(3), "Box.Map", "Probe.Box.Map"));
        DelegateCeiling.Spelled(Function(17), 17);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private FunctionType Function(int inputs)
    {
        return new FunctionType([.. Enumerable.Repeat<IMistType>(built.Type("Int32"), inputs)], built.Type("String"));
    }

    private LiftedType Pack(int width)
    {
        return new LiftedType(built.Type("Type"), new TypePackValue([.. Enumerable.Repeat<IMistType>(built.Type("Int32"), width)]));
    }

    private static SymbolValue Subject(Diagnostic refusal)
    {
        return Assert.IsType<SymbolValue>(refusal.Arguments[0]);
    }

    private static Int128 Quantity(Diagnostic refusal, int slot)
    {
        return Assert.IsType<QuantityValue>(refusal.Arguments[slot]).Value;
    }

    private static SyntaxNode AtLine(int line)
    {
        var text = new SourceText("Probe.mist", new string('\n', line - 1) + "x");
        return new TrueLiteralExpressionPrimaryNode
        {
            Span = new Storm.Runtime.SourceSpan(line - 1, 1),
            Origin = new ParsedOrigin(text),
        };
    }
}