using System;

namespace ExtensionMethods;

public static class GreetingExtensions_with_params_L5
{
    public static Greeting Repeat(this Greeting self, int times)
    {
        var repeated = "";
        for (var i = 0; i < times; i = i + 1)
        {
            if (i > 0)
            {
                repeated = repeated + " ";
            }
            repeated = repeated + self.Message;
        }
        return new Greeting(repeated);
    }
}
