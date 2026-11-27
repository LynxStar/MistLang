using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;
using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "2")]
public sealed class SnippetOutputKindInferenceTests
{
    [Fact]
    public void CleanDeclarationOnlyBatchInfersLibrary()
    {
        Assert.Equal(ProjectOutputType.Library, InferWithSecondSource(new(UnitWithMethod("Helper"))));
    }

    [Fact]
    public void PossibleMainInLaterSourceKeepsExecutableValidation()
    {
        Assert.Equal(ProjectOutputType.Executable, InferWithSecondSource(new(UnitWithMethod("Main"))));
    }

    [Fact]
    public void ErrorInSharedDiagnosticBagKeepsExecutableValidation()
    {
        Assert.Equal(ProjectOutputType.Executable,
            InferWithSecondSource(new(UnitWithMethod("Helper"), ReportsLexerError: true)));
    }

    [Fact]
    public void ErrorOnlyInParseResultKeepsExecutableValidation()
    {
        Assert.Equal(ProjectOutputType.Executable,
            InferWithSecondSource(new(UnitWithMethod("Helper"), ReturnsParserError: true)));
    }

    [Fact]
    public void MissingRootWithoutErrorsKeepsExecutableValidation()
    {
        Assert.Equal(ProjectOutputType.Executable, InferWithSecondSource(new(Root: null)));
    }

    private static ProjectOutputType InferWithSecondSource(FrontendResult secondResult)
    {
        var directory = Directory.CreateTempSubdirectory("mist-snippet-kind-");
        var first = Path.Combine(directory.FullName, "snippet.mist");
        var second = Path.Combine(directory.FullName, "support.mist");
        try
        {
            File.WriteAllText(first, "");
            File.WriteAllText(second, "");
            var frontend = new StubFrontend(new Dictionary<string, FrontendResult>
            {
                [first] = new(UnitWithMethod("Describe")),
                [second] = secondResult,
            });
            return SnippetOutputKind.Infer([first, second], frontend, frontend);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            directory.Delete();
        }
    }

    private static CompilationUnitNode UnitWithMethod(string name)
    {
        return new CompilationUnitNode
        {
            Items = [new TypeItemTopLevelItemNode
            {
                TypeDeclaration = new BodyTypeTypeDeclarationNode
                {
                    TypeBody = new TypeBodyDeclarationNode
                    {
                        Members = [new MethodMemberMemberDeclarationNode
                        {
                            Method = new MethodDeclarationNode { Name = MistTypeGenerator.MakeToken(name) },
                        }],
                    },
                },
            }],
        };
    }

    private sealed record FrontendResult(CompilationUnitNode? Root, bool ReportsLexerError = false, bool ReturnsParserError = false);

    private sealed class StubFrontend(IReadOnlyDictionary<string, FrontendResult> results) : ILexer, IParser
    {
        public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
        {
            if(results[source.Path].ReportsLexerError)
            {
                diagnostics.ReportTyped(LexerDiagnostics.LexError, new FileOnly(source.Path), new TextValue("Stub lexer error"));
            }

            return new TokenStream(source, []);
        }

        public ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)
        {
            var result = results[tokens.Source.Path];
            return new ParseResult(tokens.Source, tokens, result.Root, result.ReturnsParserError
                ? [DiagnosticFactory.Create(ParserDiagnostics.ParseError, new FileOnly(tokens.Source.Path),
                    PipelineStage.Parsing, [new TextValue("Stub parser error")]).Occurrence]
                : []);
        }
    }
}