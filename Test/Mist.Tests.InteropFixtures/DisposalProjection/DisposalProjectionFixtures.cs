namespace Mist.Tests.InteropFixtures.DisposalProjection;

public interface IResource : IDisposable;

public interface IGenericResource<T> : IResource;

public class Resource<T> : IGenericResource<T>
{
    public void Dispose()
    {
    }
}

public class Middle<T> : Resource<T>;

public sealed class Leaf : Middle<int>;

public sealed class ExplicitResource : Resource<int>
{
    public int Drop()
    {
        return 0;
    }
}

public sealed class Plain;