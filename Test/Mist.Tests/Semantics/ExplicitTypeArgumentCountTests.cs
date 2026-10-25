using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ExplicitTypeArgumentCountTests
{
    private const string CallPath = "Probe/Call.mist";

    [Fact]
    public void StatingACountNoCandidateOfThatArityDeclaresIsRefusedWithTheCountsOnOffer()
    {
        var f = new HandBuiltModel();
        var intType = f.Type("Int32");
        var candidates = Convert(f);
        IReadOnlyList<Expression> arguments = [HandBuiltModel.Literal(intType, 1L)];
        var diagnostics = new DiagnosticBag();

        var choice = OverloadResolution.Select(candidates, arguments, null, [intType, intType, intType], f.Interner);
        Assert.Null(choice.Selected);
        OverloadFailure.Report(choice, candidates, arguments, "method 'Convert'", HandBuiltModel.Node(16, CallPath), diagnostics);

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_TYPE_ARGUMENT_COUNT_MISMATCH", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "The call to method 'Convert' states the wrong number of type arguments. Declared type parameter counts: 0, 1, 2.",
            diagnostic.Message);
        Assert.Equal(CallPath, diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 17), new LinePosition(1, 18)), diagnostic.Position());
    }

    [Fact]
    public void StatingTheCountOneCandidateDeclaresSelectsThatCandidate()
    {
        var f = new HandBuiltModel();
        var candidates = Convert(f);
        var stated = new IMistType[] { f.Type("String"), f.Type("Int64") };

        var choice = OverloadResolution.Select(candidates, [HandBuiltModel.Literal(f.Type("Int32"), 1L)], null, stated, f.Interner);

        var selected = Assert.IsType<Method>(choice.Selected);
        Assert.Same(candidates[3], selected);
        Assert.Empty(choice.TypeArgumentMismatch);
        Assert.Equal(stated, selected.TypeParametersList.Select(parameter => choice.Substitution.Apply(parameter, f.Interner)));
    }

    [Fact]
    public void AnArgumentCountNoCandidateTakesIsReportedInsteadOfTheTypeArgumentCount()
    {
        var f = new HandBuiltModel();
        var intType = f.Type("Int32");
        var candidates = Convert(f);
        IReadOnlyList<Expression> arguments = [HandBuiltModel.Literal(intType, 1L), HandBuiltModel.Literal(intType, 2L), HandBuiltModel.Literal(intType, 3L)];
        var diagnostics = new DiagnosticBag();

        var choice = OverloadResolution.Select(candidates, arguments, null, [intType, intType, intType, intType], f.Interner);
        OverloadFailure.Report(choice, candidates, arguments, "method 'Convert'", HandBuiltModel.Node(16, CallPath), diagnostics);

        Assert.True(choice.NoArityMatch);
        Assert.Empty(choice.TypeArgumentMismatch);
        Assert.Equal("MIST_PM_NO_APPLICABLE_OVERLOAD", Assert.Single(diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void AnArgumentThatAlreadyFailedSilencesTheTypeArgumentCount()
    {
        var f = new HandBuiltModel();
        var intType = f.Type("Int32");
        var candidates = Convert(f);
        IReadOnlyList<Expression> arguments = [new ErrorExpression(HandBuiltModel.Node(24, CallPath))];
        var diagnostics = new DiagnosticBag();

        var choice = OverloadResolution.Select(candidates, arguments, null, [intType, intType, intType], f.Interner);
        OverloadFailure.Report(choice, candidates, arguments, "method 'Convert'", HandBuiltModel.Node(16, CallPath), diagnostics);

        Assert.NotEmpty(choice.TypeArgumentMismatch);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static Method[] Convert(HandBuiltModel f)
    {
        var intType = f.Type("Int32");
        return
        [
            Generic("Convert", intType, [], intType),
            Generic("Convert", intType, ["T"], intType),
            Generic("Convert", intType, ["T"], f.Type("String")),
            Generic("Convert", intType, ["T", "U"], intType),
            Generic("Convert", intType, ["T", "U", "V"], intType, intType),
        ];
    }

    private static Method Generic(string name, IMistType returns, string[] typeParameters, params IMistType[] parameters)
    {
        var method = new Method(name, returns, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), new MemberModifiers())
        {
            Scope = new Scope(null),
        };

        foreach(var typeParameter in typeParameters)
        {
            _ = BindingFragments.Parameter(method, typeParameter);
        }

        return method;
    }
}