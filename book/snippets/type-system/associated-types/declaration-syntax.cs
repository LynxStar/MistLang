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
