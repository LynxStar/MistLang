using Standard;
using System;

namespace AssociatedTypesDemo;

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

        protected Output()
        {
            this.Value = "";
        }

        public static Output __CreateDefault()
        {
            var inst = new Output();
            return inst;
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
        var producer = new MessageProducer();
        var output = producer.CreateOutput("hello");
        System.Console.WriteLine(output.Stringify());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
