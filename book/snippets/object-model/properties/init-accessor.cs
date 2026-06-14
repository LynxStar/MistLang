using System;

namespace Snippet;

public class Record
{
    public string Name { get; init; }

    private int _Year_k__BackingField;
    public int Year { get; init { if (value > 0) { _Year_k__BackingField = value; } } }

    public Record(string name, int year)
    {
        this.Name = name;
        this.Year = year;
    }
}
