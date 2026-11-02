using System.Collections.Immutable;

namespace Mist.Backends.Contracts;

public interface ISourceTranslator
{
    ImmutableArray<string> Extensions { get; }
    ImmutableArray<TranslatedUnit> Translate(TranslationContext context);
}