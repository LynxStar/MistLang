using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ContextualExpressionStorageFailureTests
{
    [Theory]
    [InlineData("initializer", true)]
    [InlineData("initializer", false)]
    [InlineData("pattern", true)]
    [InlineData("pattern", false)]
    public void OnlyAPositivelyRefusedStorageTargetSuppressesItsConsequentialAccessorCheck(string shape, bool failed)
    {
        var built = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Widget");
        var integer = built.Type("Int32");
        var property = new Property("Count", integer, DeclarationFragments.Public with { IsStatic = true }) { DeclaringType = owner };
        var found = new PropertyResult(property, owner, []);
        var root = shape == "initializer"
            ? (object)new ObjectInitializerAssignment(HandBuiltModel.Node(), "Count", HandBuiltModel.Literal(integer, 1L), null, member: found)
            { IsTargetFailed = failed }
            : new PropertySubpattern(HandBuiltModel.Node(), "Count", new CatchAllPattern(HandBuiltModel.Node(), integer, binding: null), ErrorType.Instance, found)
            { IsMemberFailed = failed };

        var findings = built.Analyze(new AccessorAnalyzer(), root);

        if(failed)
        {
            Assert.Empty(findings);
        }
        else
        {
            _ = Assert.Single(findings);
        }
    }
}