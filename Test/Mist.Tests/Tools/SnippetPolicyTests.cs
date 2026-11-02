using Mist.Generated;
using Mist.Tests.Generators;
using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "1")]
public sealed class SnippetPolicyTests
{
    [Fact]
    public void OptionsKeepAllSelectorsAndExplicitWorkerLimit()
    {
        var options = SnippetOptions.Parse(["first.mist", "--timings", "chapter", "--jobs", "2", "--list"]);

        Assert.Equal(["first.mist", "chapter"], options.Paths);
        Assert.Equal(2, options.Jobs);
        Assert.True(options.Timings);
        Assert.True(options.List);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public void InvalidWorkerCountIsRefused(string count)
    {
        _ = Assert.Throws<ArgumentException>(() => SnippetOptions.Parse(["--jobs", count]));
    }

    [Fact]
    public void MissingWorkerCountAndUnknownFlagsAreRefused()
    {
        _ = Assert.Throws<ArgumentException>(() => SnippetOptions.Parse(["--jobs"]));
        _ = Assert.Throws<ArgumentException>(() => SnippetOptions.Parse(["--job", "2"]));
    }

    [Fact]
    public void DefaultSelectionAndWorkerCountAreBounded()
    {
        var options = SnippetOptions.Parse([]);

        Assert.Equal(["book/snippets"], options.Paths);
        Assert.InRange(options.Jobs, 1, 8);
    }

    [Fact]
    public void MainAndTopLevelStatementsKeepExecutableValidation()
    {
        Assert.True(SnippetOutputKind.MayStart(MistProgramGenerator.MakeProgram([])));
        Assert.True(SnippetOutputKind.MayStart(new CompilationUnitNode
        {
            Items = [new StatementItemTopLevelItemNode()],
        }));
    }

    [Fact]
    public void DeclarationOnlyTreeDoesNotRequireAnExecutableAttempt()
    {
        var method = new MethodDeclarationNode { Name = MistTypeGenerator.MakeToken("RunDemo") };
        var root = new CompilationUnitNode
        {
            Items = [new TypeItemTopLevelItemNode
            {
                TypeDeclaration = new BodyTypeTypeDeclarationNode
                {
                    TypeBody = new TypeBodyDeclarationNode
                    {
                        Members = [new MethodMemberMemberDeclarationNode { Method = method }],
                    },
                },
            }],
        };

        Assert.False(SnippetOutputKind.MayStart(root));
    }

    [Fact]
    public void MainWithAnUnknownSignatureStillRequiresTheCompilerDecision()
    {
        Assert.True(SnippetOutputKind.MayStart(new MethodDeclarationNode
        {
            Name = MistTypeGenerator.MakeToken("Main"),
        }));
    }

    [Theory]
    [InlineData("class A {}", "class A {}\n")]
    [InlineData("class A {}\r\n", "class A {}\n")]
    [InlineData("class A {}\n", "class A {}\n")]
    public void EmissionUsesLfAndATrailingNewline(string source, string expected)
    {
        Assert.Equal(expected, SnippetOutput.Normalize(source));
    }
}