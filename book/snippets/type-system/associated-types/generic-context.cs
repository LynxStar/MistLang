using Standard;

namespace Snippet;

internal interface IStringable
{
    string Stringify();
}

internal interface IProducer<Output>
    where Output : Snippet.IStringable
{
    Output CreateOutput(string msg);
}

internal class Helpers
{
    public static string Describe<T, Output>(T producer)
        where T : Snippet.IProducer<Output>
        where Output : Snippet.IStringable
    {
        Output output = producer.CreateOutput("test");
        return output.Stringify();
    }

    public Helpers()
    {
    }

    public static Helpers __CreateDefault()
    {
        return new Helpers();
    }
}
