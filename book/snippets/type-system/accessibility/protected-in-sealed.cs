using Standard;

namespace Snippet;

public sealed class Singleton
{
    protected int instanceCount;

    public Singleton()
    {
        this.instanceCount = 1;
    }
}
