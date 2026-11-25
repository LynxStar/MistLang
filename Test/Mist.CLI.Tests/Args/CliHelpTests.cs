using Mist.CLI.Args;

namespace Mist.CLI.Tests.Args;

/// <summary>
/// The expected commands, flags and values are the tables in <c>book/src/tooling/cli-usage.md</c>;
/// the test command's description is criterion 6 of
/// <c>specs/attributes-and-test-command/requirements.md</c>, Requirement 5.
/// </summary>
[Trait("Tier", "1")]
public sealed class CliHelpTests
{
    private static readonly string[] BookCommands = ["build", "run", "test", "restore", "inspect", "steering", "version"];

    [Fact]
    public void ListsExactlyTheBookCommands()
    {
        Assert.Equal(BookCommands.Order(), Commands().Keys.Order());
    }

    [Fact]
    public void EveryListedCommandIsOneTheParserSelects()
    {
        foreach(var name in Commands().Keys)
        {
            var result = CliArgumentParser.Parse([name]);

            Assert.Empty(result.Errors);
            Assert.Equal(Enum.Parse<CliCommand>(name, ignoreCase: true), result.Arguments!.Command);
        }
    }

    [Fact]
    public void TheTestCommandHasItsSpecifiedDescription()
    {
        Assert.Equal("Compile and run tests", Commands()["test"]);
    }

    [Theory]
    [InlineData("--backend")]
    [InlineData("--output")]
    [InlineData("--bin")]
    [InlineData("--mode", "compile", "lex")]
    [InlineData("--pipeline", "standard")]
    [InlineData("--platform", "windows", "linux", "macos")]
    [InlineData("--rid")]
    [InlineData("--verbose", "-v")]
    [InlineData("--quiet", "-q")]
    [InlineData("--diagnostic-format", "text", "json")]
    [InlineData("--diagnostic-file")]
    [InlineData("--warnings-as-errors")]
    [InlineData("--disable")]
    [InlineData("--enable")]
    [InlineData("--min-severity", "info", "warning", "error")]
    public void ListsEveryBookFlagWithItsAliasAndValues(string flag, params string[] named)
    {
        var line = Assert.Single(HelpLines(), l => FirstToken(l) == flag);

        var words = line.Split([' ', ',', '|', '(', ')', ':'], StringSplitOptions.RemoveEmptyEntries);
        foreach(var word in named)
        {
            Assert.Contains(word, words);
        }
    }

    private static string[] HelpLines()
    {
        using var writer = new StringWriter();
        CliHelp.Write(writer);
        return writer.ToString().Split(Environment.NewLine);
    }

    private static string FirstToken(string line)
    {
        return line.TrimStart().Split(' ', 2)[0].TrimEnd(',');
    }

    private static Dictionary<string, string> Commands()
    {
        var commands = new Dictionary<string, string>();
        foreach(var line in HelpLines().SkipWhile(l => l != "Commands:").Skip(1).TakeWhile(l => l.Length > 0))
        {
            var name = FirstToken(line);
            var description = line.TrimStart()[name.Length..].TrimStart();
            if(description.StartsWith('<'))
            {
                description = description[(description.IndexOf('>') + 1)..].TrimStart();
            }

            commands.Add(name, description);
        }

        return commands;
    }
}