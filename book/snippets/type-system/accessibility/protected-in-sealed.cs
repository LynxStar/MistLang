using Standard;

namespace Snippet;

public sealed class Singleton
{
    protected int instanceCount;

    public Singleton()
    {
        this.instanceCount = default(int);
        this.instanceCount = 1;
    }
}
