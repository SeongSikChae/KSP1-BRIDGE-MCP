using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace KspBridge.Editor
{
    /// <summary>
    /// Removes stock app launcher buttons and callbacks left behind by UIApps (0069 S6). Each UIApp adds a button and three
    /// launcher callbacks per onGUIApplicationLauncherReady, but remembers only the last button (appLauncherButton) and its
    /// OnDestroy removes one button and one set of callbacks. Overlapping scene loads delivered that event twice, so the extra
    /// buttons stayed in the launcher (DontDestroyOnLoad) after their app was destroyed, stacking the toolbar and leaving
    /// reposition callbacks on destroyed apps (GenericAppFrame.Reposition NRE). Decided from game state only: a UIApp button
    /// (onTrue = the app's Show) is kept when its app is alive and remembers that very button. Main thread only.
    /// </summary>
    public static class AppLauncherRepair
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo AppList = typeof(ApplicationLauncher).GetField("appList", Private);
        private static readonly FieldInfo AppListHidden = typeof(ApplicationLauncher).GetField("appListHidden", Private);
        private static readonly FieldInfo OnHide = typeof(ApplicationLauncher).GetField("onHide", Private);
        private static readonly FieldInfo OnShow = typeof(ApplicationLauncher).GetField("onShow", Private);
        private static readonly FieldInfo OnReposition = typeof(ApplicationLauncher).GetField("onReposition", Private);

        private static bool _stopped;   // after an exception: no retry each tick (reload_mod starts a new logic)
        private static ApplicationLauncher _seenLauncher;
        private static int _seenCount = -1;
        private static object _seenHide, _seenShow, _seenReposition;

        /// <summary>
        /// Every physics tick (BridgeLogic.FixedUpdate); never throws, like the other ticks there. Only reads two list counts and
        /// three delegate references unless they changed since the last scan (delegates are immutable: every Add/Remove…Callback
        /// makes a new one, and a destroyed app's OnDestroy removes its callbacks).
        /// </summary>
        public static void Tick()
        {
            if (_stopped)
            {
                return;
            }
            try
            {
                if (!Flight.FlightReset.SceneReady
                    || AppList == null || AppListHidden == null || OnHide == null || OnShow == null || OnReposition == null)
                {
                    return;
                }
                ApplicationLauncher launcher = ApplicationLauncher.Instance;
                if (launcher == null
                    || !(AppList.GetValue(launcher) is List<ApplicationLauncherButton> shown)
                    || !(AppListHidden.GetValue(launcher) is List<ApplicationLauncherButton> hidden))
                {
                    return;
                }
                if (ReferenceEquals(launcher, _seenLauncher) && shown.Count + hidden.Count == _seenCount
                    && ReferenceEquals(OnHide.GetValue(launcher), _seenHide)
                    && ReferenceEquals(OnShow.GetValue(launcher), _seenShow)
                    && ReferenceEquals(OnReposition.GetValue(launcher), _seenReposition))
                {
                    return;
                }
                Repair(launcher, shown, hidden);
                _seenLauncher = launcher;
                _seenCount = shown.Count + hidden.Count;
                _seenHide = OnHide.GetValue(launcher);
                _seenShow = OnShow.GetValue(launcher);
                _seenReposition = OnReposition.GetValue(launcher);
            }
            catch (Exception e)
            {
                _stopped = true;
                Debug.LogError("[KspBridge] app launcher repair failed, stopped until reload_mod: " + e);
            }
        }

        private static void Repair(ApplicationLauncher launcher, List<ApplicationLauncherButton> shown, List<ApplicationLauncherButton> hidden)
        {
            ApplicationLauncherButton[] all = shown.Concat(hidden).ToArray();
            int orphan = 0, duplicate = 0;
            List<ApplicationLauncherButton> remove = new List<ApplicationLauncherButton>();
            foreach (ApplicationLauncherButton b in all)
            {
                if (!(Owner(b) is UIApp app))
                {
                    continue;   // not a UIApp button (other stock or mod buttons): leave it
                }
                if (app == null)
                {
                    orphan++;   // owner destroyed (Unity null)
                    remove.Add(b);
                }
                else if (!ReferenceEquals(app.appLauncherButton, b))
                {
                    duplicate++;   // a second button of a live app
                    remove.Add(b);
                }
            }
            List<Delegate> hideDrop = DeadOrExtra(OnHide.GetValue(launcher) as Delegate);
            List<Delegate> showDrop = DeadOrExtra(OnShow.GetValue(launcher) as Delegate);
            List<Delegate> repoDrop = DeadOrExtra(OnReposition.GetValue(launcher) as Delegate);
            int callbacks = hideDrop.Count + showDrop.Count + repoDrop.Count;
            if (remove.Count == 0 && callbacks == 0)
            {
                return;
            }

            string before = Describe(all);
            int otherBefore = all.Count(b => Owner(b) is null);
            foreach (ApplicationLauncherButton b in remove)
            {
                launcher.RemoveApplication(b);   // the game's own removal: both lists, layout item, mutual exclusion
            }
            foreach (Delegate d in hideDrop)
            {
                launcher.RemoveOnHideCallback((ApplicationLauncher.OnHide)d);
            }
            foreach (Delegate d in showDrop)
            {
                launcher.RemoveOnShowCallback((ApplicationLauncher.OnShow)d);
            }
            foreach (Delegate d in repoDrop)
            {
                launcher.RemoveOnRepositionCallback((ApplicationLauncher.OnReposition)d);
            }

            ApplicationLauncherButton[] after = shown.Concat(hidden).ToArray();
            Dictionary<UIApp, int> perApp = new Dictionary<UIApp, int>();
            foreach (ApplicationLauncherButton b in after)
            {
                if (Owner(b) is UIApp app && app != null)
                {
                    perApp.TryGetValue(app, out int n);
                    perApp[app] = n + 1;
                }
            }
            int deadLeft = DeadOrExtra(OnHide.GetValue(launcher) as Delegate).Count
                + DeadOrExtra(OnShow.GetValue(launcher) as Delegate).Count
                + DeadOrExtra(OnReposition.GetValue(launcher) as Delegate).Count;
            int otherAfter = after.Count(b => Owner(b) is null);
            Debug.Log("[KspBridge] app launcher before: " + before);
            Debug.Log("[KspBridge] app launcher repaired: buttons removed " + remove.Count + " (orphan " + orphan + ", duplicate "
                + duplicate + "), callbacks removed " + callbacks);
            Debug.Log("[KspBridge] app launcher after: live UIApp buttons " + perApp.Values.Sum() + " for " + perApp.Count
                + " apps, apps with !=1 button " + perApp.Values.Count(n => n != 1) + ", dead-target callbacks " + deadLeft
                + ", other buttons " + otherBefore + "->" + otherAfter);
        }

        /// <summary>
        /// The UIApp whose Show the button calls (possibly destroyed: compare with null), or null for any other button. onTrue is a
        /// multicast: the button starts it with its own empty lambda and Setup combines the app's callback onto it (decompile
        /// checked; measured: Target of the combined delegate is not the app), so every invocation entry is looked at.
        /// </summary>
        private static object Owner(ApplicationLauncherButton b)
        {
            if (b == null || b.onTrue == null)
            {
                return null;
            }
            foreach (Delegate d in b.onTrue.GetInvocationList())
            {
                if (d.Target is UIApp app)
                {
                    return app;
                }
            }
            return null;
        }

        /// <summary>Invocation entries of a launcher callback whose UIApp target is destroyed, or a repeat of an earlier entry.</summary>
        private static List<Delegate> DeadOrExtra(Delegate multicast)
        {
            List<Delegate> drop = new List<Delegate>();
            if (multicast == null)
            {
                return drop;
            }
            List<Delegate> kept = new List<Delegate>();
            foreach (Delegate d in multicast.GetInvocationList())
            {
                if (!(d.Target is UIApp app))
                {
                    continue;   // not a UIApp callback: leave it
                }
                if (app == null || kept.Exists(k => ReferenceEquals(k.Target, d.Target) && k.Method == d.Method))
                {
                    drop.Add(d);
                }
                else
                {
                    kept.Add(d);
                }
            }
            return drop;
        }

        /// <summary>"AlarmClock×2, AlarmClock(dead)×1, other×5": UIApp buttons by app type and liveness, other buttons together.</summary>
        private static string Describe(IEnumerable<ApplicationLauncherButton> buttons)
        {
            Dictionary<string, int> groups = new Dictionary<string, int>();
            List<string> order = new List<string>();
            foreach (ApplicationLauncherButton b in buttons)
            {
                string key = Owner(b) is UIApp app ? app.GetType().Name + (app == null ? "(dead)" : "") : "other";
                if (!groups.ContainsKey(key))
                {
                    groups[key] = 0;
                    order.Add(key);
                }
                groups[key]++;
            }
            StringBuilder s = new StringBuilder();
            foreach (string key in order)
            {
                s.Append(s.Length > 0 ? ", " : "").Append(key).Append('×').Append(groups[key]);
            }
            return s.ToString();
        }
    }
}
