// Tier 1: a CLR type's shell carries its sealed state, with the static-class exception, and an accessibility that is never widened to Internal.

using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ClrShellAccessibilityAndSealedTests
{
    internal static int Hidden => 0;

    [Theory]
    [InlineData(typeof(Version), true)]
    [InlineData(typeof(Stream), false)]
    [InlineData(typeof(Math), false)]
    [InlineData(typeof(DateTime), true)]
    public void AShellIsSealedAsTheClrTypeIsExceptAStaticClass(Type clrType, bool isSealed)
    {
        var shell = Assert.IsAssignableFrom<ConcreteType>(TypeProjector.CreateShell(clrType, clrType.Namespace!));

        Assert.Equal(isSealed, shell.IsSealed);
    }

    [Theory]
    [InlineData(typeof(Console), Accessibility.Public)]
    [InlineData(typeof(NullAmbientTypeProvider), Accessibility.Private)]
    public void ATopLevelShellIsPublicOrPrivateAndNeverInternal(Type clrType, Accessibility accessibility)
    {
        var shell = Assert.IsAssignableFrom<MemberType>(TypeProjector.CreateShell(clrType, clrType.Namespace!));

        Assert.Equal(accessibility, shell.Modifiers.Accessibility);
    }

    [Fact]
    public void AMemberIsPublicProtectedOrPrivateAndNeverInternal()
    {
        var writeLine = typeof(Console).GetMethod("WriteLine", Type.EmptyTypes)!;
        var protectedConstructor = typeof(Stream).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!;
        var hidden = typeof(ClrShellAccessibilityAndSealedTests).GetProperty(nameof(Hidden), BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Equal(Accessibility.Public, ClrAccessibilityMap.FromMethod(writeLine));
        Assert.Equal(Accessibility.Protected, ClrAccessibilityMap.FromMethod(protectedConstructor));
        Assert.Equal(Accessibility.Private, ClrAccessibilityMap.FromProperty(hidden));
        Assert.Equal(Accessibility.Private, ClrAccessibilityMap.FromMethod(hidden.GetMethod!));
    }
}