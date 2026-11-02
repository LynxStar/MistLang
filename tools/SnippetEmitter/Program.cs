using SnippetEmitter;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await SnippetApplication.Run(args);
    }
}