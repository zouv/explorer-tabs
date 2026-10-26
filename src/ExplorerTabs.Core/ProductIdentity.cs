using System;
using System.IO;

namespace ExplorerTabs.Core
{
    public static class ProductIdentity
    {
        public const string Name = "ExplorerTabs";

        // Stable compatibility identity, not product branding. Do not change independently:
        // existing releases share settings/workspace/recovery and must remain mutually exclusive.
        private const string LegacyIdentity = "ExplorerTabHost";

        public static string GetStateDirectory(string localApplicationData)
        {
            if (string.IsNullOrWhiteSpace(localApplicationData)) throw new ArgumentException("A local application data directory is required.", nameof(localApplicationData));
            return Path.Combine(localApplicationData, LegacyIdentity);
        }

        public static string GetMutexName(string userIdentity)
        {
            if (string.IsNullOrWhiteSpace(userIdentity)) throw new ArgumentException("A user identity is required.", nameof(userIdentity));
            return "Local\\" + LegacyIdentity + "." + userIdentity;
        }
    }
}
