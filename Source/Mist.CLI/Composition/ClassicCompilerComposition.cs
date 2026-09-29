using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.CLI.Composition;

/// <summary>
/// The classic pipeline's services, constructed only for a build that selects that pipeline.
/// </summary>
internal static class ClassicCompilerComposition
{
    public static CompilerDriver CreateDriver(ICompilationProgressReporter? progressReporter)
    {
        ImmutableArray<IAnalyzer> analyzers = [new TypeCheckerAnalyzer(), new FrameEscapeAnalyzer(), new ContextCheckerAnalyzer(), new ReadonlyAnalyzer(), new OwnershipAnalyzer(), new DropAnalyzer(), new EntryPointAnalyzer()];
        ImmutableArray<IBackend> backends = [new CSharpTranspilerBackend()];

        return new CompilerDriver(new MistLexer(), new MistParser(), new SemanticModelBuilder(), analyzers, backends, progressReporter);
    }
}