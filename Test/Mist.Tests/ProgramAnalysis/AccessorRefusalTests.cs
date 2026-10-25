// Tier 1 (Unit): accessor existence, access domains and construction privileges decide each refusal directly.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class AccessorRefusalTests
{
    [Theory]
    [InlineData("clr-protected-setter-written-outside-a-derivation", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("get-only-auto-property-of-a-base-written-in-a-derived-constructor", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("get-only-auto-property-of-another-instance-written-in-a-constructor", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("get-only-auto-property-written-in-a-lambda-in-its-constructor", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("get-only-auto-property-written-in-a-local-function-in-its-constructor", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("get-only-auto-property-written-outside-its-constructor", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("index-coalesced-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("index-coalesced-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("index-read-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("index-reassigned-from-itself-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("index-reassigned-from-itself-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("index-with-a-private-setter-written-outside-its-type", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("index-written-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("init-only-property-of-another-instance-written-in-a-constructor", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-of-another-instance-written-in-an-init-accessor", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-written-after-construction", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-written-in-a-get-accessor-beside-an-init-one", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-written-in-a-lambda-in-an-init-accessor", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-written-in-a-lambda-in-its-constructor", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-written-in-a-local-function-in-its-constructor", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-written-in-a-method-of-its-type", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("init-only-property-written-in-a-set-accessor", "MIST_PA_INIT_ONLY_ASSIGNMENT")]
    [InlineData("initializer-entry-over-a-get-only-auto-property", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("initializer-entry-with-a-private-setter", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("initializer-entry-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("private-init-accessor-called-by-an-initializer-outside-its-type", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("private-init-accessor-written-after-construction-outside-its-type", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("private-setter-coalesced-outside-its-type", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("private-setter-of-a-protected-property-written-in-a-derived-type", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("private-setter-written-by-name-in-a-derived-type", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("private-setter-written-outside-its-type", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("property-coalesced-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("property-coalesced-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("property-pattern-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("property-read-by-name-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("property-read-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("property-reassigned-from-itself-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("property-written-by-name-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("property-written-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("selected-indexer-with-a-private-setter", "MIST_PA_INACCESSIBLE_ACCESSOR")]
    [InlineData("selected-indexer-without-a-getter", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("selected-indexer-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("spread-projection-written-without-a-setter", "MIST_PA_NO_SET_ACCESSOR")]
    [InlineData("static-get-only-auto-property-written-in-an-instance-constructor", "MIST_PA_NO_SET_ACCESSOR")]
    public void RefusedAccessorKeepsItsDiagnostic(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new AccessorFragments(caseName).Decide());

        Assert.True(diagnostic.Number.Value == expected, $"{caseName}: expected {expected}, got {diagnostic.Number.Value}");
    }
    [Fact]
    public void AdmittedAccessAndCoveredMemberFailuresRemainSilent()
    {
        var owner = HandBuiltModel.Class("Counter");
        var other = HandBuiltModel.Class("Probe");
        var publicAccessor = new PropertyAccessor();
        var privateAccessor = new PropertyAccessor { Accessibility = Accessibility.Private };
        var init = new PropertyAccessor { IsInit = true };

        Assert.Null(AccessorDecision.Read(publicAccessor, other, MistAssembly.Synthetic, Accessibility.Public, owner));
        Assert.Null(AccessorDecision.Read(privateAccessor, owner, MistAssembly.Synthetic, Accessibility.Public, owner));
        Assert.Null(AccessorDecision.Read(privateAccessor, other, MistAssembly.Synthetic, Accessibility.Private, owner));
        Assert.Null(AccessorDecision.Write(publicAccessor, false, false, other, MistAssembly.Synthetic, Accessibility.Public, owner));
        Assert.Null(AccessorDecision.Write(init, false, true, other, MistAssembly.Synthetic, Accessibility.Public, owner));
        Assert.Null(AccessorDecision.Write(null, true, false, owner, MistAssembly.Synthetic, Accessibility.Public, owner));
        Assert.Null(AccessorDecision.Write(privateAccessor, false, false, other, MistAssembly.Synthetic, Accessibility.Private, owner));
    }
}