using System;

interface ILabeled
{
    string Label { get; }
    int Id { get; }
    string Describe();
}
