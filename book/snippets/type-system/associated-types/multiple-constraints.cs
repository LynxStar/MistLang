using System;

namespace Snippet;

internal interface IStringable
{
    string Stringify();
}

internal interface IResettable
{
    string GetState();
}

internal interface IStatefulProducer<State>
    where State : Snippet.IStringable, Snippet.IResettable
{
    string GetProducerName();
}
