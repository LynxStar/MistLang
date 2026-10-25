using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// What an extension block's body can see, and that it is looked at in the first place.
/// <para>
/// Both were previously false and silently so. The receiver existed only as a string on the block,
/// answerable by no scope, so <c>self</c> did not resolve; and no body was ever bound, because the
/// binder walks TYPES and a block is not one. Together those meant a member could name an identifier
/// that does not exist anywhere and the file still compiled clean.
/// </para>
/// </summary>
public sealed class ExtensionReceiverScopeTests : ProgramModelProbe
{
    public ExtensionReceiverScopeTests()
        : base("MistExtReceiver_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Greeting
        {
            public int Size;
        }
        """;

    protected override string Preamble { get; } = "";

    private ImmutableArray<Diagnostic> Errors(string block)
    {
        return [.. BuildRun("var unused = 1;", block).Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    [Fact]
    public void InstanceMember_SeesTheReceiver()
    {
        Assert.Empty(Errors("""
            extension(Greeting self)
            {
                public int Read()
                {
                    return self.Size;
                }
            }
            """));
    }

    /// <summary>
    /// The static rule FALLS OUT rather than being judged. MethodScope narrows an outward request to
    /// static-only for a static member, and the receiver is instance state, so the scope simply does
    /// not answer. No pass walks a finished body looking for this, which is what keeps it out of the
    /// analysis phase.
    /// </summary>
    [Fact]
    public void StaticMember_DoesNotSeeTheReceiver()
    {
        var errors = Errors("""
            extension(Greeting self)
            {
                public static int Read()
                {
                    return self.Size;
                }
            }
            """);

        var error = Assert.Single(errors);
        Assert.Equal("MIST0296", error.Number.Value);
    }

    /// <summary>
    /// The bodies are bound at all, which is the claim that a silent acceptance no longer happens.
    /// </summary>
    [Fact]
    public void UnknownIdentifierInABody_IsReported()
    {
        var errors = Errors("""
            extension(Greeting self)
            {
                public int Read()
                {
                    return zzzNoSuchIdentifier;
                }
            }
            """);

        var error = Assert.Single(errors);
        Assert.Equal("MIST0296", error.Number.Value);
    }

    [Fact]
    public void PropertyAccessorBody_IsBoundToo()
    {
        var errors = Errors("""
            extension(Greeting self)
            {
                public int Doubled => zzzNoSuchIdentifier;
            }
            """);

        var error = Assert.Single(errors);
        Assert.Equal("MIST0296", error.Number.Value);
    }
}