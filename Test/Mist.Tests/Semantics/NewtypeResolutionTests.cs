// Member access and construction on a newtype over a class or a struct, asserted on the MODEL: the
// clone's own members answer with the source's member types, and its constructors are its own. A newtype
// over an intrinsic cannot be built against this probe's Standard shells; IntrinsicCloneTests holds those.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class NewtypeResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public type Position = new Point;
        public type Actor = new Entity;

        public struct Point
        {
            public int X;
            public int Y;

            public Point(int x, int y)
            {
                this.X = x;
                this.Y = y;
            }

            public string Describe()
            {
                return "pt";
            }
        }

        public class Entity
        {
            private string name;

            public Entity(string name)
            {
                this.name = name;
            }

            public string GetName()
            {
                return this.name;
            }
        }
        """;

    public NewtypeResolutionTests()
        : base("MistNewtype_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        Position pos = new Position(3, 7);
        Actor actor = new Actor("Hero");
        """;

    [Fact]
    public void AClonedField_HasTheSourceMemberType()
    {
        var local = SingleLocal("var x = pos.X;");

        Assert.Equal("Standard.Int32", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void AClonedMethod_HasTheSourceReturnType()
    {
        var local = SingleLocal("var d = pos.Describe();");

        Assert.Equal("Standard.String", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void AClonedClassMethod_HasTheSourceReturnType()
    {
        var local = SingleLocal("var n = actor.GetName();");

        Assert.Equal("Standard.String", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void APrivateMemberOfAClone_IsInaccessibleOutsideIt()
    {
        var model = BuildModel("var n = actor.name;", extraTypes: "", extraPreamble: "", out _);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        _ = Assert.Single(findings.ToImmutableArray(), finding => finding.Number.Value == "MIST0159");
    }

    [Fact]
    public void Construction_SelectsTheClonesOwnConstructor()
    {
        var creation = InitializerOf<NewExpression>("var p = new Position(1, 2);");

        Assert.NotNull(creation.Constructor);
        Assert.Equal(2, creation.Constructor.ParametersList.Count);
        Assert.Equal("Probe.Position", creation.Type.FullyQualifiedName);
    }
}