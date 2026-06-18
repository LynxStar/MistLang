using Snippet;
using System;

Settings s = Settings.__CreateDefault();

namespace Snippet
{
    public class Settings
    {
        public int Timeout = 30;

        public bool Enabled = true;

        public string Mode = "auto";

        public Settings()
        {
            this.Timeout = 30;
            this.Enabled = true;
            this.Mode = "auto";
        }

        public static Settings __CreateDefault()
        {
            var inst = new Settings();
            return inst;
        }
    }
}
