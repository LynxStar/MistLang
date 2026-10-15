using Standard;

namespace Snippet;

public class Thermostat
{
    public Thermostat(int initial)
    {
        this.__Temperature_field = default(int);
        this.Temperature = initial;
    }

    private int __Temperature_field;
    public int Temperature
    {
        get
        {
            return __Temperature_field;
        }
        set
        {
            if (value < -273)
            {
                __Temperature_field = -273;
            }
            else
            {
                __Temperature_field = value;
            }
        }
    }
}
