using Mist.Common.Source;
using Mist.Driver.Contracts;

namespace Mist.Backends.Contracts;

// Text.Path identifies the claimed input until the driver writes the translated text.
public sealed record TranslatedUnit(SourceText Text, MistProject Project);