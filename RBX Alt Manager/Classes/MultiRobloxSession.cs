using System;
using System.Collections.Generic;
using System.Threading;

namespace RBX_Alt_Manager.Classes
{
    internal sealed class MultiRobloxSession : IDisposable
    {
        private readonly string[] names;
        private List<Mutex> handles;

        internal MultiRobloxSession(string mutexName = "ROBLOX_singletonMutex", string eventName = "ROBLOX_singletonEvent")
        {
            names = new[] { mutexName, eventName };
        }

        internal bool TryEnable()
        {
            if (handles != null) return true;
            var acquired = new List<Mutex>();
            try
            {
                foreach (string name in names)
                {
                    var mutex = new Mutex(false, name);
                    bool owns;
                    try { owns = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { owns = true; }
                    catch { mutex.Dispose(); throw; }
                    if (!owns) { mutex.Dispose(); return false; }
                    acquired.Add(mutex);
                }
                handles = acquired;
                return true;
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                if (handles == null) Release(acquired);
            }
        }

        private static void Release(List<Mutex> mutexes)
        {
            foreach (var mutex in mutexes)
            {
                try { mutex.ReleaseMutex(); }
                finally { mutex.Dispose(); }
            }
        }

        public void Dispose()
        {
            if (handles == null) return;
            Release(handles);
            handles = null;
        }
    }
}
