using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Inspection;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class InspectionReportTests
{
    [Fact]
    public void Write_ListsPropertiesBeforeMethods_EachInOrdinalOrder_OverloadsBySignature()
    {
        var report = new InspectionReport(
            "Sample.Widget",
            "class",
            null,
            [
                Shown(VisibleMemberKind.Method, "size", "(int) -> void"),
                Shown(VisibleMemberKind.Method, "Zeta", "() -> void"),
                Shown(VisibleMemberKind.Property, "Name", "string"),
                Shown(VisibleMemberKind.Method, "Alpha", "(string) -> void"),
                Shown(VisibleMemberKind.Method, "Alpha", "() -> void"),
                Shown(VisibleMemberKind.Property, "Count", "int"),
            ],
            []);

        Assert.Equal(
            [
                "Sample.Widget (class)",
                "",
                "  VISIBLE:",
                "    property Count: int",
                "    property Name: string",
                "    method Alpha() -> void",
                "    method Alpha(string) -> void",
                "    method Zeta() -> void",
                "    method size(int) -> void",
            ],
            Lines(report));
    }

    [Fact]
    public void Write_MarksProtectedAndInheritedMembers_AndListsHiddenOnesWithTheirReasons()
    {
        var report = new InspectionReport(
            "Sample.Widget",
            "class",
            null,
            [new VisibleMember(VisibleMemberKind.Method, "MemberwiseClone", "() -> Object", true, "System.Object")],
            [
                new HiddenMember("Handle", "HIDDEN: non-public", null),
                new HiddenMember("Finalize", "HIDDEN: finalizer", "System.Object"),
                new HiddenMember("Changed", "HIDDEN: event", null),
            ]);

        Assert.Equal(
            [
                "Sample.Widget (class)",
                "",
                "  VISIBLE:",
                "    method MemberwiseClone() -> Object [protected] [from System.Object]",
                "",
                "  HIDDEN:",
                "    Changed: HIDDEN: event",
                "    Finalize: HIDDEN: finalizer [from System.Object]",
                "    Handle: HIDDEN: non-public",
            ],
            Lines(report));
    }

    [Fact]
    public void Write_TypeWithNoMembers_SaysSo()
    {
        var report = new InspectionReport("Sample.IMarker", "interface", null, [], []);

        Assert.Equal(["Sample.IMarker (interface)", "  no members"], Lines(report));
    }

    [Fact]
    public void Write_HiddenOnly_OmitsTheVisibleSection()
    {
        var report = new InspectionReport(
            "System.DayOfWeek",
            "enum",
            null,
            [],
            [new HiddenMember("HasFlag", ProjectionInspector.NotInheritedReason, "System.Enum")]);

        Assert.Equal(
            [
                "System.DayOfWeek (enum)",
                "",
                "  HIDDEN:",
                "    HasFlag: HIDDEN: not inherited in Mist [from System.Enum]",
            ],
            Lines(report));
    }

    [Theory]
    [InlineData(nameof(WithholdingReason.NonPublic), "HIDDEN: non-public")]
    [InlineData(nameof(WithholdingReason.AccessorOrOperator), "HIDDEN: accessor or operator")]
    [InlineData(nameof(WithholdingReason.Finalizer), "HIDDEN: finalizer")]
    [InlineData(nameof(WithholdingReason.UnresolvableConstraint), "HIDDEN: unresolvable generic constraint")]
    public void Describe_StatesTheProjectorsReason(string reason, string expected)
    {
        Assert.Equal(expected, ProjectionInspector.Describe(new Withholding(Enum.Parse<WithholdingReason>(reason))));
    }

    [Fact]
    public void Describe_UnresolvableType_NamesTheOffendingTypeAsTheClrSpellsIt()
    {
        Assert.Equal("HIDDEN: signature uses System.Char*", Unresolvable(typeof(char).MakePointerType()));
        Assert.Equal("HIDDEN: signature uses System.Byte*", Unresolvable(typeof(byte).MakePointerType().MakeByRefType()));
        Assert.Equal(
            "HIDDEN: signature uses System.Collections.Generic.Dictionary<System.String, System.Int32[]>",
            Unresolvable(typeof(Dictionary<string, int[]>)));
    }

    [Fact]
    public void Aliased_ListsTheStandardDeclarationsExposedMembers_UnderANoteNamingIt()
    {
        var int32 = new StructType("Int32", "Standard", null, null, null);
        var standard = new ClassType("Queue<>", "Standard", null, null, null!);
        standard.PropertiesList.Add(new Property("Count", int32, new MemberModifiers { Accessibility = Accessibility.Public }));
        standard.MethodsList.Add(new Method("Grow", int32, [new Parameter("by", int32)], new MemberModifiers { Accessibility = Accessibility.Private }));
        standard.MethodsList.Add(new Method("Peek", int32, [new Parameter("at", int32)], new MemberModifiers { Accessibility = Accessibility.Public }));

        Assert.Equal(
            [
                "System.Collections.Generic.Queue`1 (class)",
                "  alias of Standard.Queue; its members are the Standard library's declarations",
                "",
                "  VISIBLE:",
                "    property Count: int",
                "    method Peek(int) -> int",
            ],
            Lines(ProjectionInspector.Aliased("System.Collections.Generic.Queue`1", standard)));
    }

    [Theory]
    [InlineData(false, false, "int")]
    [InlineData(true, false, "ref int")]
    [InlineData(true, true, "readonly ref int")]
    public void Result_SpellsAReferenceTheWayMistDeclaresOne(bool isRef, bool returnsReadonlyRef, string expected)
    {
        var int32 = new StructType("Int32", "Standard", null, null, null);

        Assert.Equal(expected, ProjectionInspector.Result(new MemberModifiers { IsRef = isRef, ReturnsReadonlyRef = returnsReadonlyRef }, int32));
    }

    [Fact]
    public void Inspect_NameWithoutANamespace_ExitsNonZeroBeforeLoadingAnything()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(1, TypeInspector.Inspect("net10.0", [], "NoNamespace", output, error));
        Assert.Empty(output.ToString());
        Assert.Contains("'NoNamespace'", error.ToString());
    }

    private static VisibleMember Shown(VisibleMemberKind kind, string name, string signature)
    {
        return new VisibleMember(kind, name, signature, false, null);
    }

    private static string Unresolvable(Type offending)
    {
        return ProjectionInspector.Describe(new Withholding(WithholdingReason.UnresolvableType, offending));
    }

    private static string[] Lines(InspectionReport report)
    {
        var output = new StringWriter();
        report.Write(output);
        return output.ToString().Split(Environment.NewLine)[..^1];
    }
}