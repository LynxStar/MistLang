using System;

namespace Snippet;

interface IDescribable
{
    string Describe();
}

interface IFullyDescribable : Snippet.IDescribable
{
    string DetailedDescribe();
}
