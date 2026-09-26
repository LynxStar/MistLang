// Tier 3: real Standard slices and element accesses consume captured Index markers and declared or lifted member bounds.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RangeIndexMemberDecisionTests
{
    private const string Preamble = "bool flag = true; int[] numbers = [10, 20, 30, 40, 50];";
    private const string Unknown = "public class Pick { public static Index Edge() { return ^1; } public static void Change(ref Index edge) { edge = ^5; } public static int Offset(int value) { return value; } }";

    [Theory]
    [InlineData("Index end = ^1; Range window = 0..end; var slice = numbers[window]; var last = slice[3];")]
    [InlineData("int offset = 1; Index end = ^offset; offset = 5; Range window = 0..end; var slice = numbers[window]; var last = slice[3];")]
    [InlineData("Index end = ^1; Index copy = end; end = ^4; Range window = 0..copy; var slice = numbers[window]; var last = slice[3];")]
    [InlineData("Index end = ^1; Range window = 0..end; end = ^4; var slice = numbers[window]; var last = slice[3];")]
    [InlineData("Index end = ^1; end = ^2; Range window = 0..end; var slice = numbers[window]; var last = slice[2];")]
    [InlineData("Index end = ^1; if(flag) { end = ^1; } Range window = 0..end; var slice = numbers[window]; var last = slice[3];")]
    [InlineData("Index end = ^1; while(flag) { end = ^1; flag = false; } Range window = 0..end; var slice = numbers[window]; var last = slice[3];")]
    [InlineData("Index end = ^1; Range window = 0..end; if(flag) { window = 0..^1; } var slice = numbers[window]; var last = slice[3];")]
    [InlineData("Index start = ^3; Index end = ^1; var slice = numbers[start..end]; var last = slice[1];")]
    [InlineData("Index end = ^0; var slice = numbers[end..end];")]
    [InlineData("Index end = ^1; var last = numbers[end];")]
    [InlineData("int offset = Pick.Offset(1); if(offset >= 1 && offset <= 2) { Index end = ^offset; offset = 5; var slice = numbers[0..end]; var last = slice[2]; } ")]
    [InlineData("Index end = ^1; var value = \"abc\"[end];")]
    public void StoredIndexes_PreserveTheirCapturedOffsetsAndMarkers(string statements)
    {
        Assert.Empty(Findings(Build(statements, Unknown)));
    }

    [Theory]
    [InlineData("Index end = ^0; var last = numbers[end];", "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    [InlineData("Index end = ^6; var last = numbers[end];", "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    [InlineData("Index end = ^6; var slice = numbers[0..end]; var last = slice[0];", "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    [InlineData("Index end = ^1; if(flag) { end = ^2; } var slice = numbers[0..end]; var last = slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Index end = ^1; Range window = 0..end; if(flag) { window = 0..4; } var slice = numbers[window]; var last = slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Index end = Pick.Edge(); Range window = 0..end; end = ^1; var slice = numbers[window]; var last = slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Index end = ^1; var change = () => { end = ^5; }; change(); var slice = numbers[0..end]; var last = slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Index end = ^1; ref Index alias = end; Range window = 0..alias; alias = ^5; var slice = numbers[window]; var last = slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Index end = ^1; Range window = 0..end; Pick.Change(ref end); var slice = numbers[window]; var last = slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    public void UnknownDifferentAndInvalidIndexes_RemainRefused(string statements, string id)
    {
        Assert.Equal(id, Assert.Single(Findings(Build(statements, Unknown))).Id.Value);
    }

    [Theory]
    [InlineData("Index end = 4;")]
    [InlineData("Index end = ^1; end = 4;")]
    public void AnIntegerAssignment_DoesNotInventAnImplicitIndexConversion(string statements)
    {
        Assert.Equal("MIST_PA_NO_IMPLICIT_CONVERSION", Assert.Single(Findings(Build(statements))).Id.Value);
    }

    [Theory]
    [InlineData("public int End { get { return 3; } }", "Range window = 0..holder.End; var slice = numbers[window]; var last = slice[2];")]
    [InlineData("public Offset end = 3; public void Change() { end = 4; }", "Range window = 0..holder.end; holder.Change(); var slice = numbers[window]; var last = slice[2];")]
    [InlineData("public Offset end = 3;", "Range original = 0..holder.end; Range window = original; holder.end = 4; var slice = numbers[window]; var last = slice[2];")]
    [InlineData("public Offset End { get { return 3; } }", "Range window = 0..holder.End; if(flag) { window = 0..holder.End; } var slice = numbers[window]; var last = slice[2];")]
    [InlineData("public static int End { get { return 3; } }", "Range window = 0..Holder.End; var slice = numbers[window]; var last = slice[2];")]
    [InlineData("public int End { get { return 3; } }", "Index end = ^holder.End; Range window = end..^0; var slice = numbers[window]; var last = slice[2];")]
    [InlineData("public int End { get { if(PickFlag) { return 3; } return 4; } } bool PickFlag = true;", "Range window = 0..holder.End; var slice = numbers[window]; var last = slice[2];")]
    public void MemberOffsets_UseTheirDeclaredOrLiftedSignature(string members, string statements)
    {
        var model = Build("var holder = new Holder();" + statements, "type Offset = int >= 3 and <= 4; public class Holder { " + members + " }");

        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("public int end = 3;", "Range window = 0..holder.end;")]
    [InlineData("int end = 3; public int End { get { return end; } }", "Range window = 0..holder.End;")]
    [InlineData("public Index End { get { return ^1; } }", "Range window = 0..holder.End;")]
    [InlineData("public Index end = ^1;", "Range window = 0..holder.end;")]
    [InlineData("public Holder Child { get { return this; } } public int End { get { return 3; } }", "Range window = 0..holder.Child.End;")]
    [InlineData("public int End { get { return 3; } }", "ref Holder alias = holder; Range window = 0..alias.End;")]
    public void UnsupportedMembers_DoNotAcquireFactsFromTheirSourceOrReceiver(string members, string statements)
    {
        var model = Build("var holder = new Holder();" + statements + "var slice = numbers[window]; var last = slice[0];", "public class Holder { " + members + " }");

        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(model)).Id.Value);
    }

    [Theory]
    [InlineData("edge = ^1; Range window = 0..this.edge; edge = ^4; var slice = numbers[window]; return slice[3];", null)]
    [InlineData("this.edge = ^1; Range window = 0..edge; if(flag) { window = 0..^1; } var slice = numbers[window]; return slice[3];", null)]
    [InlineData("edge = ^1; Range window = 0..edge; Change(); var slice = numbers[window]; return slice[3];", null)]
    [InlineData("edge = ^1; ref Index alias = edge; alias = ^5; Range window = 0..edge; var slice = numbers[window]; return slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("this.edge = ^1; ref Index alias = this.edge; alias = ^5; Range window = 0..this.edge; var slice = numbers[window]; return slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("edge = ^1; var self = this; ref Index alias = self.edge; alias = ^5; Range window = 0..edge; var slice = numbers[window]; return slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("edge = ^1; Change(); Range window = 0..edge; var slice = numbers[window]; return slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("edge = ^1; if(flag) { edge = ^2; } Range window = 0..edge; var slice = numbers[window]; return slice[0];", "MIST_PA_INDEX_UNPROVEN")]
    public void VisibleOwnedFieldAssignments_CaptureIndexesUntilAnInvalidatingCallOrDifferentJoin(string statements, string? id)
    {
        var model = Build("var holder = new Holder(); var last = holder.Read(flag);",
            "public class Holder { Index edge; public void Change() { edge = ^5; } public int Read(bool flag) { int[] numbers = [10, 20, 30, 40, 50]; " + statements + " } }");
        var findings = Findings(model);

        if(id is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(id, Assert.Single(findings).Id.Value);
        }
    }

    [Theory]
    [InlineData("Range window = 1..numbers.Length; var slice = numbers[window]; var last = slice[3];", null)]
    [InlineData("Range window = 1..numbers.Length; numbers = [10]; var slice = numbers[window]; var last = slice[0];", "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    [InlineData("Index end = ^numbers.Length; var slice = numbers[end..^0]; var last = slice[4];", null)]
    public void BuiltinLength_CapturesTheMeasuredValueOnce(string statements, string? id)
    {
        var findings = Findings(Build(statements));

        if(id is not null)
        {
            Assert.Equal(id, Assert.Single(findings).Id.Value);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData("int[] a = Inputs.Values(); if(a.Length >= 3 && a.Length <= 5) { Range window = 0..a.Length; if(flag) { window = 0..a.Length; } var slice = a[window]; var last = slice[2]; }", null)]
    [InlineData("int[] a = Inputs.Values(); int[] b = Inputs.Values(); if(a.Length >= 3 && a.Length <= 5 && b.Length >= 3 && b.Length <= 5) { Range window = 0..a.Length; if(flag) { window = 0..b.Length; } var slice = a[window]; var last = slice[0]; }", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("int[] a = Inputs.Values(); if(a.Length >= 3 && a.Length <= 5) { Range window = 0..a.Length; Range copy = window; a = [10]; var slice = a[copy]; var last = slice[0]; }", "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    public void NonExactLengthCaptures_KeepReceiverIdentityAndTheirCapturedValues(string statements, string? id)
    {
        var findings = Findings(Build(statements, "public class Inputs { public static int[] Values() { return [10, 20, 30]; } }"));

        if(id is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(id, Assert.Single(findings).Id.Value);
        }
    }

    private static ProgramModelClass Build(string statements, string types = "")
    {
        var model = StandardModelProbe.Build(Preamble + statements, out var diagnostics, types);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        foreach(var index in BodyWalker.DescendantsAndSelf(Body(model)).OfType<IndexExpression>())
        {
            Assert.IsNotType<ErrorType>(index.Type);

            if(Lengths.IsRangeSlice(index))
            {
                Assert.Null(index.ParameterType);
                Assert.Null(index.Indexer);
            }
        }

        return model;
    }

    private static Block Body(ProgramModelClass model)
    {
        return Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!;
    }

    private static ImmutableArray<Diagnostic> Findings(ProgramModelClass model)
    {
        var diagnostics = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, diagnostics);
        return diagnostics.ToImmutableArray();
    }
}