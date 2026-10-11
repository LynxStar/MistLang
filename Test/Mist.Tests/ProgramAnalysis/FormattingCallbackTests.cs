using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FormattingCallbackTests
{
    private readonly FormattingFragments f = new();

    [Fact]
    public void AGenericFormattingItsParameter_FormatsThatParameter()
    {
        var show = f.Formats("Show", isReadonly: false);

        Assert.Same(show.TypeParametersList[0], Assert.Single(new FormattingCallbacks().Of(show, show.DeclaringType)));
    }

    [Fact]
    public void AGenericForwardingItsParameterIntoAnUnmarkedFormatting_FormatsItsOwnParameter()
    {
        var wrap = f.Forwards("Wrap", isReadonly: false, f.Formats("Show", isReadonly: false), offset: 40);

        Assert.Same(wrap.TypeParametersList[0], Assert.Single(new FormattingCallbacks().Of(wrap, wrap.DeclaringType)));
    }

    [Fact]
    public void AGenericForwardingIntoADeclaredReadonlyFormatting_LeavesItToThatPromise()
    {
        var wrap = f.Forwards("Wrap", isReadonly: false, f.Formats("Read", isReadonly: true), offset: 40);

        Assert.Empty(new FormattingCallbacks().Of(wrap, wrap.DeclaringType));
    }

    [Fact]
    public void TwoGenericsCallingEachOther_SettleOnWhatEitherFormats()
    {
        var first = f.Formats("First", isReadonly: false);
        var second = f.Formats("Second", isReadonly: false);
        var held = first.TypeParametersList[0];
        var passed = second.TypeParametersList[0];
        first.Body!.Statements = [new ReturnStatement(f.Call(second, held, FormattingFragments.Value(held), 40), f.Text)];
        second.Body!.Statements = [
            new ExpressionStatement(f.Call(first, passed, FormattingFragments.Value(passed), 50)),
            .. second.Body.Statements,
        ];
        var callbacks = new FormattingCallbacks();

        Assert.Same(first.TypeParametersList[0], Assert.Single(callbacks.Of(first, first.DeclaringType)));
        Assert.Same(second.TypeParametersList[0], Assert.Single(callbacks.Of(second, second.DeclaringType)));
    }

    [Fact]
    public void AMemberWithNoTypeParameterInScope_FormatsNoParameter()
    {
        Assert.Empty(new FormattingCallbacks().Of(f.Interpolates("Shown", f.Box), f.Program));
    }

    [Fact]
    public void AConstructedFormatterIsFollowedThroughItsClosingToTheFormatterItReaches()
    {
        var steps = new FormattingCallbacks().Reach(f.Constructed(f.Wrapper, f.Box)).ToList();

        Assert.Equal(2, steps.Count);
        Assert.Same(f.Wrapper.MethodsList.Single(method => method.Name == "ToString"), steps[0].Formatter);
        Assert.Same(f.Box.MethodsList.Single(method => method.Name == "ToString"), steps[1].Formatter);
    }

    [Fact]
    public void AConstructedFormatterOverAParameterReachesThatParameter()
    {
        var parameter = new TypeParameter("T", 0, f.Program.Scope, null);

        var steps = new FormattingCallbacks().Reach(f.Constructed(f.Wrapper, parameter)).ToList();

        Assert.Same(parameter, steps[^1].Open);
    }
}