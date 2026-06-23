using System;

interface IDescribable
{
    string Describe();
}
interface IFullyDescribable : IDescribable
{
    string DetailedDescribe();
}
