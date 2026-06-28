using System;

namespace Snippet;

public interface IStringable
{
    string Stringify();
}
public interface IProducer<TOutput>
    where TOutput : IStringable
{
    TOutput CreateOutput(string msg);
}
