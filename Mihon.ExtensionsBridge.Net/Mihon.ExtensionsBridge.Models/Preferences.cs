using System;
using System.Collections.Generic;
using System.Text;

namespace Mihon.ExtensionsBridge.Models
{
    public class FlareSolverrPreferences
    {
        public bool Enabled { get; set; } = false;
        public string Url { get; set; } = "http://localhost:8191";
        public int Timeout { get; set; } = 60;
        public string SessionName { get; set; } = "extension.bridge";
        public int SessionTtl { get; set; } = 15;
        public bool AsResponseFallback { get; set; } = false;
    }
    public class SocksProxyPreferences
    {
        public bool Enabled { get; set; } = false;
        public int Version { get; set; } = 5;
        public string Host { get; set; } = "";
        public int Port { get; set; } = 0;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
    public class Preferences
    {
        public FlareSolverrPreferences FlareSolverr { get; set; } = new FlareSolverrPreferences();

        public SocksProxyPreferences SocksProxy { get; set; } = new SocksProxyPreferences();

        public Dictionary<string, Dictionary<string, string>> Interceptors = [];
    }
}
