using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KspBridge
{
    /// <summary>
    /// Loader: created once at game start and kept across scenes. Hosts the HTTP server and the main-thread queue,
    /// and drives the replaceable logic assembly (see LogicHost).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class KspBridgeAddon : MonoBehaviour
    {
        private readonly MainThreadDispatcher _dispatcher = new MainThreadDispatcher();
        private HttpServer _server;

        private void Awake()
        {
            DontDestroyOnLoad(this);
            Settings settings = Settings.Load(KSPUtil.ApplicationRootPath + "GameData/KspBridge/settings.cfg");
            _server = new HttpServer(settings.Port, _dispatcher, LogicHost.BaseRoutes().ToList());
            try
            {
                _server.Start();
                Debug.Log("[KspBridge] listening on http://127.0.0.1:" + settings.Port + "/ (loader " + LogicHost.LoaderVersion + ")");
            }
            catch (Exception e)
            {
                _server = null;
                Debug.LogError("[KspBridge] failed to start HTTP server on port " + settings.Port + ": " + e);
                return;
            }
            LogicHost.Init(_server, new LogicContext { Dispatcher = _dispatcher, Settings = settings });
        }

        private void Update()
        {
            _dispatcher.Pump();
        }

        private void FixedUpdate()
        {
            try
            {
                LogicHost.FixedUpdate();
            }
            catch (Exception e)
            {
                Debug.LogError("[KspBridge] logic FixedUpdate: " + e);
            }
        }

        private void OnDestroy()
        {
            LogicHost.Shutdown();
            _server?.Stop();
            _server = null;
        }
    }
}
