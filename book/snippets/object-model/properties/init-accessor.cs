using Standard;

namespace Snippet;

public class Record
{
    public Record(string name, int year)
    {
        this.Name = "";
        this.__Year_field = default(int);
        this.Name = name;
        this.Year = year;
    }

    public string Name { get; init; }

    private int __Year_field;
    public int Year
    {
        get => __Year_field;
        init
        {
            if (value > 0)
            {
                __Year_field = value;
            }
        }
    }
}
