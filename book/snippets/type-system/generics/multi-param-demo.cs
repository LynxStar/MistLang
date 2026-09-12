using Standard;

namespace Snippet;

public class MultiParamDemo
{
    public static void Run()
    {
        Snippet.KeyValueStore<string, int> store = new Snippet.KeyValueStore<string, int>("age", 30);
        System.Console.WriteLine(store.Describe());
        Snippet.KeyValueStore<int, bool> mixed = new Snippet.KeyValueStore<int, bool>(1, true);
        System.Console.WriteLine(mixed.Describe());
    }

    public MultiParamDemo()
    {
    }

    public static MultiParamDemo __CreateDefault()
    {
        return new MultiParamDemo();
    }
}
