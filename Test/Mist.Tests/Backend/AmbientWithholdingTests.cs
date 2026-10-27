using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Inspection;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientWithholdingTests
{
    [Fact]
    public void ProjectorsRetainEveryWithheldSignatureWithoutAdmittingAnError()
    {
        var built = new HandBuiltModel();
        var diagnostics = new DiagnosticBag();
        var owner = HandBuiltModel.Class("MissingSurface");
        var resolver = new AmbientTypeResolver([], new Dictionary<string, IMistType>
        {
            ["System.Int32"] = built.Type("Int32"),
            ["System.Void"] = built.Type("Void"),
        }, built.Interner, diagnostics);

        MethodProjector.Project(owner, typeof(MissingSurface), resolver);
        PropertyProjector.Project(owner, typeof(MissingSurface), resolver);
        IndexerProjector.Project(owner, typeof(MissingSurface), resolver);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(["Visible"], owner.MethodsList.Select(method => method.Name));
        Assert.Empty(owner.PropertiesList);
        Assert.Empty(owner.IndexersList);
        var overloads = owner.WithheldMembers.Where(frame => frame.Member.Spelling == "Missing").ToList();
        Assert.Equal(2, overloads.Count);
        Assert.Equal(2, overloads.Select(frame => frame.Member.Identity).Distinct().Count());
        foreach(var name in new[] { "Missing", "Property", "Item" })
        {
            foreach(var reason in owner.WithheldMembers.Where(frame => frame.Member.Spelling == name))
            {
                Assert.Equal(nameof(WithholdingReason.UnresolvableType), reason.Reason.Token);
                Assert.Equal(typeof(DateTime).AssemblyQualifiedName, reason.Offending?.Identity);
                Assert.Equal("System.DateTime", reason.Offending?.Spelling);
                Assert.Equal("HIDDEN: signature uses System.DateTime", ProjectionInspector.Describe(reason));
            }
        }

        Assert.Contains(owner.WithheldMembers, frame => frame.Member.Spelling == "Property");
        Assert.Contains(owner.WithheldMembers, frame => frame.Member.Spelling == "Item");
    }

    [Fact]
    public void InspectionUsesTheRetainedReasonWithoutReprojectingTheMember()
    {
        var reflected = typeof(MissingSurface).GetMethod("Missing", [typeof(int)])!;
        var declaring = new ClassType("MissingSurface", typeof(MissingSurface).Namespace!, null, null, null)
        {
            Assembly = ProgramModel.Model.MistAssembly.Ambient,
        };
        var snapshot = new Withholding(WithholdingReason.UnresolvableType, typeof(DateTime)).Snapshot(reflected);
        declaring.WithheldMembers.Add(snapshot);
        var report = ProjectionInspector.Retained(reflected, declaring, "Base");

        Assert.NotNull(report);
        Assert.Same(snapshot, report.Withheld);
        Assert.Equal("HIDDEN: signature uses System.DateTime", report.Reason);
        Assert.Equal("Base", report.DeclaringBase);
    }

    private sealed class MissingSurface
    {
        public static DateTime Missing(int value)
        {
            _ = value;
            return default;
        }

        public static DateTime Missing(DateTime value)
        {
            return value;
        }

        public static DateTime Property => default;

        public DateTime this[int index] => default;

        public static int Visible(int value)
        {
            return value;
        }
    }
}