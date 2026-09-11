using System;

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
