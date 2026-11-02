using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Backends.CSharpTranspiler;

public sealed class CSharpBackend : IBackend
{
    public BackendId Id => BackendId.CSharp;
    public ImmutableArray<ISourceTranslator> SourceTranslators { get; } = [new RazorComponentTranslator()];
}