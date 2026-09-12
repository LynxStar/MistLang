using Standard;

namespace Snippet;

public static class GreetingExtensions_withparams_0
{
    public static Snippet.Greeting Repeat(this Snippet.Greeting self, int times)
    {
        string repeated = "";
        for (int i = 0; i < times; i = i + 1)
        {
            if (i > 0)
            {
                repeated = repeated + " ";
            }
            repeated = repeated + self.Message;
        }
        return new Snippet.Greeting(repeated);
    }
}
