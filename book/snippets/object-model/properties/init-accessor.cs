using Standard;

namespace Snippet;

public class Record
{
    public Record(string name, int year)
    {
        this.Name = "";
        this.Year = default(int);
        this.Name = name;
        this.Year = year;
    }

    public string Name { get; init; }

    public int Year
    {
        get;
        init
        {
            if (value > 0)
            {
                field = value;
            }
        }
    }
}
