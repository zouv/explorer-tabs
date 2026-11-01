using System;
namespace ExplorerTabs.Core
{
    public static class WorkspaceCapturePolicy
    {
        public const int ItemTimeoutMilliseconds = 2500;
        // Serial single-flight Shell reads: budget includes one full deadline per tab plus overhead.
        // The supported library maximum is 100 tabs. Exit is separately capped to avoid trapping quit.
        public static int TimeoutMilliseconds(int count, bool automatic, bool exiting = false)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            int budget = 5000 + Math.Min(count, 100) * (ItemTimeoutMilliseconds + 100);
            return exiting ? Math.Min(30000, Math.Max(7000, budget)) : Math.Max(automatic ? 7000 : 30000, budget);
        }
    }
}
