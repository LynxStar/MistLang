using System;

namespace Snippet;

internal interface IDescribable
{
    string Describe();
}

internal interface IFullyDescribable : Snippet.IDescribable
{
    string DetailedDescribe();
}
