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
interface IStatefulProducer<TState>
    where TState : IStringable, IResettable
{
    string GetProducerName();
}
