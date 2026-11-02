using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class LoanConsumptionTests
{
    private readonly LifetimeFragments f = new();

    public static TheoryData<OwnershipKind, bool, bool, string> Consumptions
    {
        get
        {
            var cases = new TheoryData<OwnershipKind, bool, bool, string>();
            foreach(var (ownership, byRef, readOnly) in new[]
            {
                (OwnershipKind.Frozen, false, false),
                (OwnershipKind.Borrowed, false, false),
                (OwnershipKind.Unrestricted, true, true),
                (OwnershipKind.Unrestricted, true, false),
            })
            {
                foreach(var action in new[] { "drop", "move", "return", "plain" })
                {
                    cases.Add(ownership, byRef, readOnly, action);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Consumptions))]
    public void ALoanParameterAndItsTransitiveAliasesCannotConsume(OwnershipKind ownership, bool byRef, bool readOnly, string action)
    {
        var parameter = new Parameter("lease", f.Token, new ParameterModifier(readOnly, byRef, ownership));
        var lease = new LocalVariable("lease", f.Token) { Parameter = parameter, Ownership = ownership, IsRef = byRef, IsReadonly = readOnly };
        var a = f.Local("a");
        var b = f.Local("b");
        var sink = new Method("Take", f.Void, [Owned("value", f.Token, action == "plain" ? OwnershipKind.Unrestricted : OwnershipKind.Owned)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = f.Sink };
        Statement consuming = action switch
        {
            "return" => Return(Ref(b)),
            "drop" => Run(Drop(b)),
            _ => Run(f.Call(sink, Ref(b))),
        };
        var body = HandBuiltModel.Block(new IfStatement(f.Is(Ref(lease), a),
            HandBuiltModel.Block(new IfStatement(f.Is(Ref(a), b), HandBuiltModel.Block(consuming), null)), null));
        var method = new Method("Inspect", action == "return" ? f.Token : f.Void, [parameter], new MemberModifiers { IsStatic = true })
        { DeclaringType = f.Sink, Body = body };

        Assert.Contains(f.Model.Analyze(new OwnershipAnalyzer(), body, member: method), diagnostic => diagnostic.Number.Value == "MIST0229");
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, false)]
    [InlineData(OwnershipKind.Borrowed, false)]
    [InlineData(OwnershipKind.Unrestricted, true)]
    public void ALocalLoanAndItsPatternAliasStayNonConsumingOutsideWith(OwnershipKind ownership, bool byRef)
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", f.Token) { Ownership = ownership, IsRef = byRef };
        var alias = f.Local("alias");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), Declare(lease, Ref(r)),
            new IfStatement(f.Is(Ref(lease), alias), HandBuiltModel.Block(Run(Drop(alias))), null));

        Assert.Contains(f.Model.Analyze(new OwnershipAnalyzer(), body), diagnostic => diagnostic.Number.Value == "MIST0229");
    }

    [Fact]
    public void ARefLoanStillFollowsItsSlotAfterANonOwningInstallation()
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", f.Token) { IsRef = true };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), With(Ref(lease), HandBuiltModel.Block(Run(Assign(r, f.New(2))))), Run(Ref(lease)))));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData("class", SubstructuralDiscipline.Linear)]
    [InlineData("struct", SubstructuralDiscipline.Linear)]
    [InlineData("class", SubstructuralDiscipline.Affine)]
    [InlineData("struct", SubstructuralDiscipline.Affine)]
    public void ALoanParameterMayReadButCannotDropItsValue(string shape, SubstructuralDiscipline discipline)
    {
        var type = f.DisciplinedValue(shape, discipline);
        var parameter = Owned("lease", type, OwnershipKind.Borrowed);
        var lease = new LocalVariable("lease", type) { Parameter = parameter, Ownership = OwnershipKind.Borrowed };
        var valid = HandBuiltModel.Block(Run(f.Count(lease)));
        var invalid = HandBuiltModel.Block(Run(Drop(lease)));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), valid));
        Assert.Equal("MIST0229", Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), invalid)).Number.Value);
    }
}