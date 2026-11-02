using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;

namespace Mist.Driver;

public sealed class BackendRegistry
{
    private readonly Dictionary<BackendId, IBackend> backends;

    public BackendRegistry() : this([new CSharpBackend()])
    {
    }

    public BackendRegistry(IEnumerable<IBackend> backends)
    {
        this.backends = backends.ToDictionary(backend => backend.Id);
    }

    public IBackend? Resolve(BackendId id)
    {
        return backends.GetValueOrDefault(id);
    }
}