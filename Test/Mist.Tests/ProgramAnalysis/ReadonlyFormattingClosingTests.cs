using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyFormattingClosingTests
{
    private const string FormatsMutable = "MIST_PA_READONLY_CONTEXT_FORMATS_MUTABLE";
    private const string CallsMutable = "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE";

    private readonly FormattingFragments f = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadonlyGenericFormattingItsParameter_IsRefusedWhereAMutableFormatterClosesIt(bool concatenates)
    {
        var read = f.Formats("Read", isReadonly: true, concatenates);
        _ = f.Call(read, f.Box, FormattingFragments.Value(f.Box), offset: 60);

        var finding = Assert.Single(f.Analyze(read));

        Assert.Equal(FormatsMutable, finding.Id.Value);
        Assert.Equal(61, finding.Location.Start.Column);
        Assert.Equal(
            "Cannot use 'Box' as type argument 'T': readonly method 'Read' formats it, and the 'ToString' of 'Box' is not readonly.",
            finding.Message);
    }

    [Fact]
    public void AConstructedGenericFormatter_IsJudgedByTheFormatterItsClosingReaches()
    {
        var read = f.Formats("Read", isReadonly: true);
        var wrapped = f.Constructed(f.Wrapper, f.Box);
        _ = f.Call(read, wrapped, FormattingFragments.Value(wrapped), offset: 60);

        var finding = Assert.Single(f.Analyze(read));

        Assert.Equal(FormatsMutable, finding.Id.Value);
        Assert.Contains("type argument 'T'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("the 'ToString' of 'Box' is not readonly", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("calm")]
    [InlineData("quiet")]
    [InlineData("int")]
    [InlineData("wrapped-calm")]
    [InlineData("wrapped-int")]
    public void AReadonlyOrInferredReadonlyFormatterClosure_IsAdmitted(string argument)
    {
        var read = f.Formats("Read", isReadonly: true);
        IMistType closing = argument switch
        {
            "calm" => f.Calm,
            "quiet" => f.Quiet,
            "int" => f.Integer,
            "wrapped-calm" => f.Constructed(f.Wrapper, f.Calm),
            _ => f.Constructed(f.Wrapper, f.Integer),
        };
        _ = f.Call(read, closing, FormattingFragments.Value(closing), offset: 60);

        Assert.Empty(f.Analyze(read));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void AReadonlyExtensionFormattingItsBlocksParameter_IsJudgedWhereTheReceiverClosesTheBlock(bool mutable, int findings)
    {
        var (declared, closed) = f.ReadonlyExtension(mutable ? f.Box : f.Calm);

        var found = f.Analyze(declared, f.CallsClosed("Main", closed, offset: 60));

        Assert.Equal(findings, found.Count);
        Assert.All(found, finding =>
        {
            Assert.Equal(FormatsMutable, finding.Id.Value);
            Assert.Equal(61, finding.Location.Start.Column);
        });
    }

    [Fact]
    public void AnUnmarkedGenericFormattingItsParameter_StaysAdmittedForAMutableFormatter()
    {
        var show = f.Formats("Show", isReadonly: false);
        _ = f.Call(show, f.Box, FormattingFragments.Value(f.Box), offset: 60);

        Assert.Empty(f.Analyze(show));
    }

    [Fact]
    public void AParameterForwardedIntoAReadonlyFormatting_IsRefusedWhereItIsClosed()
    {
        var read = f.Formats("Read", isReadonly: true);
        var wrap = f.Forwards("Wrap", isReadonly: false, read, offset: 40);
        _ = f.Call(wrap, f.Box, FormattingFragments.Value(f.Box), offset: 60);

        var finding = Assert.Single(f.Analyze(read, wrap));

        Assert.Equal(61, finding.Location.Start.Column);
        Assert.StartsWith("Cannot use 'Box' as type argument 'U': readonly method 'Read' formats it", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyGenericTypeFormattingItsParameter_IsRefusedWhereAMutableFormatterClosesIt()
    {
        var (keep, describe) = f.ReadonlyGeneric("Keep");
        f.Close(keep, f.Box, offset: 60);
        f.Close(keep, f.Calm, offset: 80);

        var finding = Assert.Single(f.Analyze(describe));

        Assert.Equal(61, finding.Location.Start.Column);
        Assert.StartsWith("Cannot use 'Box' as type argument 'K': readonly method 'Describe' formats it", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMemberCallingAnUnmarkedGenericAtAMutableClosing_IsRefusedAtTheCall()
    {
        var show = f.Formats("Show", isReadonly: false);
        var direct = f.Calls("Direct", isReadonly: true, show, f.Box, offset: 60);

        var finding = Assert.Single(f.Analyze(show, direct));

        Assert.Equal(CallsMutable, finding.Id.Value);
        Assert.Equal(61, finding.Location.Start.Column);
        Assert.Contains("Cannot call 'ToString' from readonly method 'Direct'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyGenericCallingAnUnmarkedGenericWithItsOwnParameter_PromisesThatParameter()
    {
        var show = f.Formats("Show", isReadonly: false);
        var open = f.Forwards("Open", isReadonly: true, show, offset: 40);
        _ = f.Call(open, f.Box, FormattingFragments.Value(f.Box), offset: 60);
        _ = f.Call(open, f.Calm, FormattingFragments.Value(f.Calm), offset: 80);

        var finding = Assert.Single(f.Analyze(show, open));

        Assert.Equal(FormatsMutable, finding.Id.Value);
        Assert.Equal(61, finding.Location.Start.Column);
        Assert.Contains("readonly method 'Open' formats it", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMemberCallingAReadonlyGenericAtAMutableClosing_IsOneComplaint()
    {
        var read = f.Formats("Read", isReadonly: true);
        var direct = f.Calls("Direct", isReadonly: true, read, f.Box, offset: 60);

        var finding = Assert.Single(f.Analyze(read, direct));

        Assert.Equal(FormatsMutable, finding.Id.Value);
    }

    [Fact]
    public void AnInheritedUnmarkedGenericCalledThroughADerivedReceiver_IsClosedByItsBase()
    {
        var (shown, show) = f.GenericBase("Shown");
        var derived = f.Derived("Boxes", shown, f.Box);
        f.Close(shown, f.Box, offset: 20);

        var finding = Assert.Single(f.Analyze(show, f.CallsThrough("Look", show, derived, offset: 60)));

        Assert.Equal(CallsMutable, finding.Id.Value);
        Assert.Equal(61, finding.Location.Start.Column);
    }

    [Fact]
    public void AnInheritedGenericClosedSafely_PromisesNothingAboutTheBasesOtherClosings()
    {
        var (shown, show) = f.GenericBase("Shown");
        _ = f.Derived("Boxes", shown, f.Box);
        var calms = f.Derived("Calms", shown, f.Calm);
        f.Close(shown, f.Box, offset: 20);
        f.Close(shown, f.Calm, offset: 30);

        Assert.Empty(f.Analyze(show, f.CallsThrough("Look", show, calms, offset: 60)));
    }

    [Fact]
    public void AParameterAUseLeavesUnbound_IsNoPromiseOfTheCallingMember()
    {
        var (shown, show) = f.GenericBase("Shown");
        f.Close(shown, f.Box, offset: 20);

        Assert.Empty(f.Analyze(show, f.CallsThrough("Look", show, f.Program, offset: 60)));
    }

    [Fact]
    public void AReadonlyMemberFormattingAConstructedGenericFormatter_IsRefusedAtTheHole()
    {
        var deep = f.Interpolates("Deep", f.Constructed(f.Wrapper, f.Box));

        var finding = Assert.Single(f.Analyze(deep));

        Assert.Equal(CallsMutable, finding.Id.Value);
        Assert.Contains("Cannot call 'ToString' from readonly method 'Deep'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyGenericFormattingAnOptionalOfItsParameter_IsRefusedWhereAMutableFormatterClosesIt()
    {
        var maybe = f.FormatsOptional("Maybe", isReadonly: true);
        _ = f.Call(maybe, f.Box, FormattingFragments.Value(f.Box), offset: 60);

        Assert.Equal(FormatsMutable, Assert.Single(f.Analyze(maybe)).Id.Value);
    }

    [Theory]
    [InlineData("optional", 1)]
    [InlineData("tuple", 1)]
    [InlineData("union", 0)]
    public void AReadonlyMemberFormattingAMutableFormatterInsideAnotherType_IsRefusedWhereTheTargetFormatsIt(string shape, int findings)
    {
        IMistType formatted = shape switch
        {
            "optional" => new UnionType([f.Box, f.Void]),
            "tuple" => new TupleType([f.Box, f.Integer]),
            _ => new UnionType([f.Box, f.Integer]),
        };

        var found = f.Analyze(f.Interpolates("Shown", formatted));

        Assert.Equal(findings, found.Count);
        Assert.All(found, finding => Assert.Equal(CallsMutable, finding.Id.Value));
    }

    [Fact]
    public void AReadonlyMemberFormattingAMutableFormatterDirectly_KeepsItsRefusal()
    {
        var finding = Assert.Single(f.Analyze(f.Interpolates("Shown", f.Box)));

        Assert.Equal(CallsMutable, finding.Id.Value);
    }
}