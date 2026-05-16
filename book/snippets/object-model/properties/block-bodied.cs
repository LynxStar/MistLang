using System;

namespace Demo;

public class Thermostat
{
    private int _temperature;

    public Thermostat(int initial)
    {
        this._temperature = initial;
    }

    public int Temperature
    {
        get
        {
            return _temperature;
        }
        set
        {
            if (value < -273)
            {
                _temperature = -273;
            }
            else
            {
                _temperature = value;
            }
        }
    }
}
