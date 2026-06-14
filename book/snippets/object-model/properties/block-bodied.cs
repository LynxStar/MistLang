using System;

namespace Snippet;

public class Thermostat
{
    public Thermostat(int initial)
    {
        this.Temperature = initial;
    }

    private int _Temperature_k__BackingField;
    public int Temperature { get { return _Temperature_k__BackingField; } set { if (value < -273) { _Temperature_k__BackingField = -273; } } }
}
