using System;

namespace Demo;

public class Record
{
    public string Name { get; init; }

    private int _year;
    public int Year
    {
        get => _year;
        init
        {
            if (value > 0)
            {
                _year = value;
            }
        }
    }

    public Record(string name, int year)
    {
        this.Name = name;
        this.Year = year;
    }
}
