using Mist.Common.Source;

namespace Mist.Tests.Common.SourceText;

public class NodeOriginTests
{
    [Fact]
    public void SyntheticOrigin_EmptyReason_ThrowsArgumentException()
    {
        var parsedOrigin = new ParsedOrigin(new Mist.Common.Source.SourceText("test.mist", "hello"));

        _ = Assert.Throws<ArgumentException>(() => new SyntheticOrigin(parsedOrigin, ""));
    }

    [Fact]
    public void TestOrigin_Instance_IsSingleton()
    {
        var first = TestOrigin.Instance;
        var second = TestOrigin.Instance;

        Assert.Same(first, second);
    }
}