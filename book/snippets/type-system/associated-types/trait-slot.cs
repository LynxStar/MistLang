using System;

namespace Snippet;

interface IStringable
{
    string Stringify();
}
interface Consumable<TInput>
    where TInput : IStringable
{
    string GetLabel();
}
