using Standard;

namespace Snippet;

public class Thermostat
{
    public Thermostat(int initial)
    {
        this.Temperature = default(int);
        this.Temperature = initial;
    }

    public int Temperature
    {
        get
        {
            return field;
        }
        set
        {
            if (value < -273)
            {
                field = -273;
            }
            else
            {
                field = value;
            }
        }
    }
}
