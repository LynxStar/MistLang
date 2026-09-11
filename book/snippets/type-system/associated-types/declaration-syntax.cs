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
