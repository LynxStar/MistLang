using System;

namespace Snippet;

interface IStringable
{
    string Stringify();
}
interface IProducer<TOutput>
    where TOutput : IStringable
{
    TOutput CreateOutput(string msg);
}
class MessageProducer : IProducer<MessageProducer.Output>
{
    public class Output : IStringable
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

    public Output CreateOutput(string msg)
    {
        return new Output(msg);
    }

    public static MessageProducer __CreateDefault()
    {
        var inst = new MessageProducer();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var producer = new MessageProducer();
        var output = producer.CreateOutput("hello");
        Console.WriteLine(output.Stringify());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
