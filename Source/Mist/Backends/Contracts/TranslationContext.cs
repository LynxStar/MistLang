using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Backends.Contracts;

public sealed record TranslationContext(
    ImmutableArray<ClaimedInput> Inputs,
    ImmutableArray<CompilationUnit> ParsedUnits,
    IAmbientTypeSource AmbientTypes,
    ILexer Lexer,
    IParser Parser,
    ProjectFacts Project,
    DiagnosticBag Diagnostics);