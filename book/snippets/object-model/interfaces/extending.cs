using System;

namespace Snippet;

interface IDescribable
{
    string Describe();
}
interface IFullyDescribable : IDescribable
{
    string DetailedDescribe();
}
