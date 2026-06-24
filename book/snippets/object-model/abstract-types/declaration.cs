using System;

namespace Snippet;

interface IShape
{
    string Name { get; }
    string Describe();
    int Area();
}
