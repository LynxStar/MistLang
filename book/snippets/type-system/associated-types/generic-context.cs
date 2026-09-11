using System;

namespace Snippet;

interface IStringable
{
    string Stringify();
}

interface IProducer<Output>
    where Output : Snippet.IStringable
{
    Output CreateOutput(string msg);
}

class Helpers
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
