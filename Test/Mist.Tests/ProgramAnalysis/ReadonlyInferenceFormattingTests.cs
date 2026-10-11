using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyInferenceFormattingTests
{
    private readonly FormattingFragments f = new();

    [Fact]
    public void AnUnmarkedGenericFormattingItsParameter_IsReadonlyForEveryClosingItCannotSee()
    {
        Assert.True(new ReadonlyInference().IsReadonly(f.Formats("Show", isReadonly: false)));
    }

    [Fact]
    public void ACallerClosingItWithAMutableFormatter_IsNotReadonly()
    {
        var show = f.Formats("Show", isReadonly: false);

        Assert.False(new ReadonlyInference().IsReadonly(f.Calls("Through", isReadonly: false, show, f.Box, offset: 60)));
    }

    [Fact]
    public void ACallerClosingItThroughAConstructedFormatterThatReachesAMutableOne_IsNotReadonly()
    {
        var show = f.Formats("Show", isReadonly: false);

        Assert.False(new ReadonlyInference().IsReadonly(f.Calls("Through", isReadonly: false, show, f.Constructed(f.Wrapper, f.Box), offset: 60)));
    }

    [Theory]
    [InlineData("calm")]
    [InlineData("quiet")]
    public void ACallerClosingItWithAReadonlyOrInferredReadonlyFormatter_StaysReadonly(string argument)
    {
        var show = f.Formats("Show", isReadonly: false);

        Assert.True(new ReadonlyInference().IsReadonly(f.Calls("Through", isReadonly: false, show, argument == "calm" ? f.Calm : f.Quiet, offset: 60)));
    }

    [Fact]
    public void ACallerOfADeclaredReadonlyGeneric_TrustsTheDeclarationWhichIsJudgedAtTheClosing()
    {
        var read = f.Formats("Read", isReadonly: true);

        Assert.True(new ReadonlyInference().IsReadonly(f.Calls("Through", isReadonly: false, read, f.Box, offset: 60)));
    }

    [Fact]
    public void AMemberFormattingAConstructedFormatterThatReachesAMutableOne_IsNotReadonly()
    {
        Assert.False(new ReadonlyInference().IsReadonly(f.Interpolates("Shown", f.Constructed(f.Wrapper, f.Box), isReadonly: false)));
    }
}