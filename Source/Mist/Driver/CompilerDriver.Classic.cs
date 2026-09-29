using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;

namespace Mist.Driver;

/// <summary>
/// The only way into <see cref="ClassicPipeline"/>. Nothing in the rest of the driver names a classic
/// type, so deleting this file and the pipeline it composes leaves the experimental route compiling.
/// </summary>
public sealed partial class CompilerDriver
{
    public CompilerDriver(
        ILexer lexer,
        IParser parser,
        ISemanticModelBuilder semanticModelBuilder,
        ImmutableArray<IAnalyzer> analyzers,
        ImmutableArray<IBackend> backends,
        ICompilationProgressReporter? progressReporter = null)
        : this(lexer, parser, semanticModelBuilder, analyzers, backends, StdlibParseCache.Shared, progressReporter)
    {
    }

    internal CompilerDriver(
        ILexer lexer,
        IParser parser,
        ISemanticModelBuilder semanticModelBuilder,
        ImmutableArray<IAnalyzer> analyzers,
        ImmutableArray<IBackend> backends,
        StdlibParseCache stdlibParseCache,
        ICompilationProgressReporter? progressReporter = null)
        : this(
            lexer,
            parser,
            stdlibParseCache,
            progressReporter,
            backends.Select(backend => backend.Id),
            new ClassicPipeline(semanticModelBuilder, analyzers, backends, progressReporter ?? NullProgressReporter.Instance))
    {
    }
}