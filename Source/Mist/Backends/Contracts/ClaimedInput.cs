using Mist.Common.Source;
using Mist.Driver.Contracts;

namespace Mist.Backends.Contracts;

public sealed record ClaimedInput(SourceText Text, MistProject Project);