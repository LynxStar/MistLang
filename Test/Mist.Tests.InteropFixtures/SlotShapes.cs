namespace Mist.Tests.InteropFixtures;

public abstract class RequiredBase
{
    public abstract int Size { get; }

    protected abstract string Key(int n);
}

public class DefaultedBase
{
    public virtual string Label { get; set; } = "";

    public virtual int this[int index] => index;

    public virtual int Twice(int x)
    {
        return x * 2;
    }

    public void Fixed()
    {
    }

    protected virtual void Hook()
    {
    }
}

public class SealingDerived : DefaultedBase
{
    public sealed override int Twice(int x)
    {
        return x;
    }
}

public class OverridingDerived : RequiredBase
{
    public override int Size => 1;

    protected override string Key(int n)
    {
        return n.ToString();
    }
}

public class ImplicitImplementer : IDisposable
{
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}