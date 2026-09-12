using Standard;

namespace Snippet;

public interface IStringable
{
    string Stringify();
}

public interface IProducer<Output>
    where Output : Snippet.IStringable
{
    Output CreateOutput(string msg);
}

internal class MessageProducer : Snippet.IProducer<Snippet.MessageProducer.Output>
{
    public Snippet.MessageProducer.Output CreateOutput(string msg)
    {
        return new Snippet.MessageProducer.Output(msg);
    }

    public MessageProducer()
    {
    }

    public static MessageProducer __CreateDefault()
    {
        return new MessageProducer();
    }

    public class Output : Snippet.IStringable
    {
        public string Value;

        public Output(string value)
        {
            Value = value;
        }

        public string Stringify()
        {
            return $"Output({Value})";
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.MessageProducer producer = new Snippet.MessageProducer();
        Snippet.MessageProducer.Output output = producer.CreateOutput("hello");
        System.Console.WriteLine(output.Stringify());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
