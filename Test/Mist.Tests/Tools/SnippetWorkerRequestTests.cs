using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "3")]
[Collection(SnippetConsoleCollection.Name)]
public sealed class SnippetWorkerRequestTests : IDisposable
{
    private readonly DirectoryInfo scratch = Directory.CreateTempSubdirectory("mist-snippet-worker-request-");

    [Fact]
    public async Task MalformedRequestReportsErrorAndLeavesExistingResponseUntouched()
    {
        var request = Path.Combine(scratch.FullName, "request.json");
        var response = Path.Combine(scratch.FullName, "response.json");
        var previousResponse = "[ ]\n"u8.ToArray();
        await File.WriteAllTextAsync(request, "[");
        await File.WriteAllBytesAsync(response, previousResponse);
        File.SetLastWriteTimeUtc(response, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var previousWriteTime = File.GetLastWriteTimeUtc(response);

        var (output, error) = (Console.Out, Console.Error);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exitCode;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exitCode = await SnippetApplication.Run(["--worker", request, response]);
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
        }

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout.ToString());
        var diagnostic = stderr.ToString();
        Assert.StartsWith("error: ", diagnostic);
        Assert.DoesNotContain("Unknown option:", diagnostic, StringComparison.Ordinal);
        Assert.Equal(previousResponse, await File.ReadAllBytesAsync(response));
        Assert.Equal(previousWriteTime, File.GetLastWriteTimeUtc(response));
    }

    public void Dispose()
    {
        scratch.Delete(recursive: true);
    }
}