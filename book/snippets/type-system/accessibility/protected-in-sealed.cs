using Standard;

namespace Snippet;

public sealed class Singleton
{
    protected int instanceCount;

    public Singleton()
    {
        this.instanceCount = 1;
    }

    private Singleton(Mist.Emission.CSharp.__DefaultInit marker)
    {
        this.instanceCount = default(int);
    }

    public static Singleton __CreateDefault()
    {
        return new Singleton(default(Mist.Emission.CSharp.__DefaultInit));
    }
}
