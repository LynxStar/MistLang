using Standard;

using Snippet;

Snippet.Settings s = Snippet.Settings.__CreateDefault();

namespace Snippet
{
    public class Settings
    {
        public int Timeout = 30;
        public bool Enabled = true;
        public string Mode = "auto";

        public Settings()
        {
        }

        public static Settings __CreateDefault()
        {
            return new Settings();
        }
    }
}
