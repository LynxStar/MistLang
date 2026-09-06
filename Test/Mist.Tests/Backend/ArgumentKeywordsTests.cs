using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// The by-ref keyword an argument takes, one case per row of the CLR truth table. Mist has one
/// by-ref spelling and C# has three, so this mapping is the whole of what the call site cannot say
/// for itself.
/// <para>
/// A unit test rather than a sample, because five of these six rows cannot be written in Mist at
/// all -- they are shapes a CLR signature carries and Mist source has no syntax for. The one that
/// can be, ClrRefInterop, is banked and covers rows 2, 3 and 6 end to end.
/// </para>
/// </summary>
public sealed class ArgumentKeywordsTests
{
    private static Parameter Param(bool isRef, bool isReadonly, bool markedOut)
    {
        var parameter = new Parameter(
            "p",
            new ClassType("Int32", "Standard", new Scope(null), null, null),
            new ParameterModifier(isReadonly, isRef, OwnershipKind.Unrestricted));

        if(markedOut)
        {
            parameter.AttributesList.Add(new AttributeData(
                new ClassType("OutAttribute", "System.Runtime.InteropServices", new Scope(null), null, null),
                [],
                []));
        }

        return parameter;
    }

    [Fact]
    public void ByValue_TakesNoKeyword()
    {
        Assert.Equal("", ArgumentKeywords.For(Param(isRef: false, isReadonly: false, markedOut: false)));
    }

    [Fact]
    public void PlainByRef_TakesRef()
    {
        Assert.Equal("ref ", ArgumentKeywords.For(Param(isRef: true, isReadonly: false, markedOut: false)));
    }

    [Fact]
    public void OutParameter_TakesOut()
    {
        Assert.Equal("out ", ArgumentKeywords.For(Param(isRef: true, isReadonly: false, markedOut: true)));
    }

    /// <summary>
    /// <c>[In, Out] ref</c> is mutable <c>ref</c> in C# and sets the CLR Out flag anyway. COM interop
    /// declares 43 such parameters in the WindowsDesktop reference pack, and <c>out</c> is CS1620
    /// against every one of them. The not-readonly guard is what keeps this row at <c>ref</c>.
    /// </summary>
    [Fact]
    public void ReadonlyByRefMarkedOut_StaysRef()
    {
        Assert.Equal("ref ", ArgumentKeywords.For(Param(isRef: true, isReadonly: true, markedOut: true)));
    }

    /// <summary>
    /// <c>[Out]</c> on a BY-VALUE parameter is a marshalling annotation and says nothing about the
    /// calling convention; nine such parameters exist in that same pack, where <c>out</c> is CS1615.
    /// The by-ref guard is what keeps this row empty.
    /// </summary>
    [Fact]
    public void ByValueMarkedOut_TakesNoKeyword()
    {
        Assert.Equal("", ArgumentKeywords.For(Param(isRef: false, isReadonly: false, markedOut: true)));
    }

    /// <summary>
    /// The readonly by-ref directions deliberately stay at <c>ref</c>. A genuine CLR <c>in</c> and an
    /// <c>[In] ref</c> reach this function as the same shape, because projection filters both
    /// InAttribute and IsReadOnlyAttribute, and they need opposite keywords. Pinned so that changing
    /// it is a decision rather than an accident.
    /// </summary>
    [Fact]
    public void ReadonlyByRef_StaysRef()
    {
        Assert.Equal("ref ", ArgumentKeywords.For(Param(isRef: true, isReadonly: true, markedOut: false)));
    }
}