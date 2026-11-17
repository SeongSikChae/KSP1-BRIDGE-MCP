using System;
using System.Collections.Generic;

namespace KspBridge.Mod.Core
{
    /// <summary>
    /// Every external hook of a generation (GameEvents, vessel callbacks) is registered here and released together on Stop,
    /// so an old generation can never keep reacting after a reload.
    /// </summary>
    public sealed class Subscriptions
    {
        private readonly List<Action> releases = new List<Action>();
        private readonly Action<string> logError;

        public Subscriptions(Action<string> logError) => this.logError = logError;

        public int Count => releases.Count;

        /// <summary>Records how to undo a hook that was just installed.</summary>
        public void Add(Action release) => releases.Add(release);

        public void Add<T>(EventData<T> evt, EventData<T>.OnEvent handler)
        {
            evt.Add(handler);
            releases.Add(() => evt.Remove(handler));
        }

        public void Add(EventVoid evt, EventVoid.OnEvent handler)
        {
            evt.Add(handler);
            releases.Add(() => evt.Remove(handler));
        }

        public void ReleaseAll()
        {
            for (int i = releases.Count - 1; i >= 0; i--)
            {
                try
                {
                    releases[i]();
                }
                catch (Exception ex)
                {
                    logError?.Invoke("subscription release failed: " + ex);
                }
            }
            releases.Clear();
        }
    }
}
