using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// A dependent parameter sharing a name with a member of its own type (dependent-types 6.5): the value
/// property the parameter projects would be a second member of that name.
/// </summary>
public sealed class DependentNameConflictTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("field", true)]
    [InlineData("property", true)]
    [InlineData("method", true)]
    [InlineData("nested-type", true)]
    [InlineData("other-name", false)]
    [InlineData("ordinary-parameter", false)]
    public void AMemberNamedLikeADependentParameterConflictsWithIt(string member, bool conflicts)
    {
        var fragment = new DeclarationFragments();
        var box = fragment.Class("Box");
        var parameter = DeclarationFragments.Parameter(box, box.Scope, "N");
        parameter.IsDependent = member != "ordinary-parameter";
        parameter.Domain = fragment.Built.Type("Int32");
        var named = member == "other-name" ? "Count" : "N";

        switch(member)
        {
            case "property":
                box.PropertiesList.Add(new Property(named, fragment.Built.Type("Int32"), DeclarationFragments.Public));
                break;
            case "method":
                _ = DeclarationFragments.Method(box, named, fragment.Built.Type("Int32"));
                break;
            case "nested-type":
                _ = fragment.Class(named, box);
                break;
            default:
                box.FieldsList.Add(new Field(named, fragment.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public));
                break;
        }

        Assert.Equal(conflicts, DeclarationShapeAnalyzer.DependentNameConflicts(box).Contains(parameter));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void APackSharingAMembersNameProjectsNothingToConflictWith()
    {
        var fragment = new DeclarationFragments();
        var box = fragment.Class("Box");
        var pack = DeclarationFragments.Parameter(box, box.Scope, "Items");
        pack.IsDependent = true;
        pack.PackKind = TypePackKind.Unbracketed;
        box.FieldsList.Add(new Field("Items", fragment.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public));

        Assert.Empty(DeclarationShapeAnalyzer.DependentNameConflicts(box));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TheConflictIsReportedOnceAtTheTypesDeclaration()
    {
        var fragment = new DeclarationFragments();
        var box = fragment.Class("Box");
        var parameter = DeclarationFragments.Parameter(box, box.Scope, "N");
        parameter.IsDependent = true;
        parameter.Domain = fragment.Built.Type("Int32");
        box.FieldsList.Add(new Field("N", fragment.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public));

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));

        Assert.Equal("MIST_DT_0005", finding.Id.Value);
        Assert.Equal(11, finding.Location.Start.Column);
        Assert.Contains("'N' conflicts with member 'N' declared in type 'Box'", finding.Message, StringComparison.Ordinal);
    }
}