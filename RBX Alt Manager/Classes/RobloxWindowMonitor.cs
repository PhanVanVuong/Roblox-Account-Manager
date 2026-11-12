using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RBX_Alt_Manager.Classes
{
    internal static class RobloxWindowMonitor
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetWindowText(IntPtr window, string text);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);
        private sealed class Tracked
        {
            public Process Process;
            public string Username;
            public IntPtr Window;
            public DateTime? MissingSince;
        }

        internal static bool ShouldStopBackground(bool hadWindow, bool previousWindowExists, bool hasCurrentWindow, DateTime? missingSince, DateTime now)
            => hadWindow && !previousWindowExists && !hasCurrentWindow && missingSince.HasValue && (now - missingSince.Value).TotalSeconds >= 15;

        internal static async Task Run(AccountManager owner)
        {
            var tracked = new Dictionary<int, Tracked>();
            try
            {
                while (!Program.Closed && !owner.IsDisposed && !owner.Disposing)
                {
                    try
                    {
                        var accounts = (KeyValuePair<string, string>[])owner.Invoke(new Func<KeyValuePair<string, string>[]>(() =>
                            AccountManager.AccountsList.Where(a => !string.IsNullOrEmpty(a.BrowserTrackerID)).Select(a => new KeyValuePair<string, string>(a.BrowserTrackerID, a.Username)).ToArray()));
                        foreach (var process in Process.GetProcessesByName("RobloxPlayerBeta"))
                        {
                            bool keep = false;
                            try
                            {
                                if (tracked.ContainsKey(process.Id)) continue;
                                string command = process.GetCommandLine();
                                var matches = accounts.Where(a => Account.MatchesWindowAccount(command, a.Key)).ToArray();
                                if (matches.Length != 1) continue;
                                tracked.Add(process.Id, new Tracked { Process = process, Username = matches[0].Value });
                                keep = true;
                            }
                            catch (Exception) { }
                            finally { if (!keep) process.Dispose(); }
                        }
                        foreach (var pair in tracked.ToArray())
                        {
                            var item = pair.Value;
                            try
                            {
                                if (item.Process.HasExited) { item.Process.Dispose(); tracked.Remove(pair.Key); continue; }
                                item.Process.Refresh();
                                var window = item.Process.MainWindowHandle;
                                if (window != IntPtr.Zero)
                                {
                                    item.Window = window;
                                    item.MissingSince = null;
                                    if (item.Process.MainWindowTitle != item.Username) SetWindowText(window, item.Username);
                                }
                                else if (item.Window != IntPtr.Zero && !IsWindow(item.Window))
                                {
                                    item.MissingSince = item.MissingSince ?? DateTime.UtcNow;
                                    if (ShouldStopBackground(true, false, false, item.MissingSince, DateTime.UtcNow))
                                    {
                                        item.Process.Kill();
                                        item.Process.Dispose();
                                        tracked.Remove(pair.Key);
                                    }
                                }
                            }
                            catch (Exception error) { Program.Logger.Error("Window monitor: " + error.GetType().Name); }
                        }
                    }
                    catch (Exception error)
                    {
                        if (Program.Closed || owner.IsDisposed || owner.Disposing) break;
                        Program.Logger.Error("Window discovery: " + error.GetType().Name);
                    }
                    await Task.Delay(1000);
                }
            }
            finally { foreach (var item in tracked.Values) item.Process.Dispose(); }
        }
    }
}
