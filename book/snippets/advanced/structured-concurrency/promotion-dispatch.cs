using Standard;

namespace Snippet;

public interface IReader
{
    int Read();
}

public class Reader : Snippet.IReader
{
    public async System.Threading.Tasks.Task<int> Read()
    {
        return await Snippet.Program.Fetch();
    }

    public Reader()
    {
    }

    public static Reader __CreateDefault()
    {
        return new Reader();
    }

    int Snippet.IReader.Read() => this.Read().GetAwaiter().GetResult();
}

public class Program
{
    public static async System.Threading.Tasks.Task<int> Fetch()
    {
        return 7;
    }

    public static async System.Threading.Tasks.Task Main()
    {
        Snippet.Reader reader = new Snippet.Reader();
        Snippet.IReader view = reader;
        Snippet.Reader absent = null;
        System.Console.WriteLine((await reader.Read()).ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(view.Read().ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine((await Snippet.Int32Extensions_promotiondispatch_0.ReadExtension(2)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        System.Console.WriteLine(((absent is { } __e1 ? await __e1.Read() : default(int?)) ?? -1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}

public static class Int32Extensions_promotiondispatch_0
{
    public static async System.Threading.Tasks.Task<int> ReadExtension(this int self)
    {
        return await Snippet.Program.Fetch();
    }
}
