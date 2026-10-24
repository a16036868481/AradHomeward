// Copyright (c) 2026 eirros. All rights reserved.
// Custom contributions; see the project LICENSE.txt and retained third-party notices.
using System;
using System.IO;

namespace Dnf60Lifecycle
{
    // Pure session state: starting services alone never arms shutdown. A real
    // client from this installation must be seen before its exit can trigger it.
    public sealed class ClientExitMonitor
    {
        private bool armed;
        private DateTime? missingSince;
        public string State { get; private set; }

        public ClientExitMonitor() { State = "waiting-for-game"; }

        public bool Observe(bool clientRunning, bool keepManagement, DateTime now)
        {
            if (keepManagement)
            {
                armed = false; missingSince = null; State = "management-mode";
                return false;
            }
            if (clientRunning)
            {
                armed = true; missingSince = null; State = "armed";
                return false;
            }
            if (!armed) { State = "waiting-for-game"; return false; }
            if (!missingSince.HasValue) missingSince = now;
            State = "waiting-for-client-exit";
            return now - missingSince.Value >= TimeSpan.FromSeconds(2);
        }

        public static bool IsInstalledClient(string executablePath, string clientDirectory)
        {
            if (String.IsNullOrWhiteSpace(executablePath) || String.IsNullOrWhiteSpace(clientDirectory)) return false;
            try
            {
                return String.Equals(Path.GetFullPath(executablePath),
                    Path.GetFullPath(Path.Combine(clientDirectory, "DNF.exe")), StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
    }
}
