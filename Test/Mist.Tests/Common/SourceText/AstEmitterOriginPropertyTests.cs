using Storm.SourceGenerator.Emit;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Verifies that AstEmitter.EmitRuntimeTypes() emits the Origin property
/// on the SyntaxNode base class.
/// </summary>
public class AstEmitterOriginPropertyTests
{
    private readonly string _output = AstEmitter.EmitRuntimeTypes();

    [Fact]
    public void EmitRuntimeTypes_SyntaxNode_ContainsOriginProperty()
    {
        Assert.Contains("public Mist.Common.Text.NodeOrigin? Origin { get; set; }", _output);
    }

    [Fact]
    public void EmitRuntimeTypes_OriginProperty_AppearsAfterSpanProperty()
    {
        var spanIndex = _output.IndexOf("public SourceSpan Span { get; set; }");
        var originIndex = _output.IndexOf("public Mist.Common.Text.NodeOrigin? Origin { get; set; }");

        Assert.True(spanIndex >= 0, "Span property not found in emitted output");
        Assert.True(originIndex >= 0, "Origin property not found in emitted output");
        Assert.True(originIndex > spanIndex, "Origin property should appear after Span property");
    }

    [Fact]
    public void EmitRuntimeTypes_OriginProperty_IsInsideSyntaxNodeClass()
    {
        var syntaxNodeIndex = _output.IndexOf("public abstract class SyntaxNode");
        var originIndex = _output.IndexOf("public Mist.Common.Text.NodeOrigin? Origin { get; set; }");

        Assert.True(syntaxNodeIndex >= 0, "SyntaxNode class not found in emitted output");
        Assert.True(originIndex >= 0, "Origin property not found in emitted output");
        Assert.True(originIndex > syntaxNodeIndex, "Origin property should be inside SyntaxNode class");
    }
}