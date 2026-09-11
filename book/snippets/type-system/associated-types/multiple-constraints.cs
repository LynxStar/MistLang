using System;

namespace Snippet;

interface IStringable
{
    string Stringify();
}

interface IResettable
{
    string GetState();
}

interface IStatefulProducer<State>
    where State : Snippet.IStringable, Snippet.IResettable
{
    string GetProducerName();
}
