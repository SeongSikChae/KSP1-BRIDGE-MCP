using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace KspBridge
{
    public delegate object RouteHandler(Match path, RequestBody body, NameValueCollection query);

    public sealed class Route
    {
        /// <param name="onHttpThread">true: run on the HTTP thread (the handler must use the dispatcher itself for KSP calls).</param>
        public Route(string method, string pattern, RouteHandler handler, bool onHttpThread = false)
        {
            Method = method;
            Pattern = new Regex("^" + pattern + "/?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
            Handler = handler;
            OnHttpThread = onHttpThread;
        }

        public string Method { get; }
        public Regex Pattern { get; }
        public RouteHandler Handler { get; }
        public bool OnHttpThread { get; }
    }

    /// <summary>HttpListener on 127.0.0.1. Each request's handler runs on the main thread via the dispatcher.</summary>
    public sealed class HttpServer
    {
        private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(5);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        private readonly int _port;
        private readonly MainThreadDispatcher _dispatcher;
        private volatile List<Route> _routes;
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public HttpServer(int port, MainThreadDispatcher dispatcher, List<Route> routes)
        {
            _port = port;
            _dispatcher = dispatcher;
            _routes = routes;
        }

        /// <summary>Replaces the route table atomically (requests in progress keep the previous table).</summary>
        public void SetRoutes(List<Route> routes)
        {
            _routes = routes;
        }

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
            _listener.Start();
            _running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "KspBridge.Http" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KspBridge] stop: " + e.Message);
            }
            _dispatcher.FailAll("KspBridge is shutting down");
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception e)
                {
                    if (_running)
                    {
                        Debug.LogWarning("[KspBridge] accept failed: " + e.Message);
                    }
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(context));
            }
        }

        private void Handle(HttpListenerContext context)
        {
            int status = 200;
            object payload;
            try
            {
                payload = new Dictionary<string, object> { ["ok"] = true, ["data"] = Dispatch(context.Request) };
            }
            catch (ApiException e)
            {
                status = e.Status;
                payload = Error(e.Code, e.Message);
            }
            catch (Exception e)
            {
                status = 500;
                payload = Error("internal", e.GetType().Name + ": " + e.Message);
                Debug.LogError("[KspBridge] " + context.Request.HttpMethod + " " + context.Request.Url.AbsolutePath + " failed: " + e);
            }

            try
            {
                byte[] bytes = Utf8.GetBytes(Json.Json.Serialize(payload));
                HttpListenerResponse response = context.Response;
                response.StatusCode = status;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.OutputStream.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KspBridge] response write failed: " + e.Message);
            }
        }

        private object Dispatch(HttpListenerRequest request)
        {
            string path = request.Url.AbsolutePath;
            bool pathMatched = false;
            List<Route> routes = _routes;
            foreach (Route route in routes)
            {
                Match match = route.Pattern.Match(path);
                if (!match.Success)
                {
                    continue;
                }
                pathMatched = true;
                if (!string.Equals(route.Method, request.HttpMethod, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                RequestBody body = ReadBody(request);
                NameValueCollection query = request.QueryString;
                if (route.OnHttpThread)
                {
                    return route.Handler(match, body, query);
                }
                return _dispatcher.Invoke(() => route.Handler(match, body, query), MainThreadTimeout);
            }

            if (pathMatched)
            {
                throw new ApiException(405, "method_not_allowed", request.HttpMethod + " is not allowed on " + path);
            }
            throw ApiException.NotFound("no route for " + request.HttpMethod + " " + path);
        }

        private static RequestBody ReadBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
            {
                return new RequestBody(null);
            }

            string text;
            using (StreamReader reader = new StreamReader(request.InputStream, Utf8))
            {
                text = reader.ReadToEnd();
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                return new RequestBody(null);
            }

            object parsed;
            try
            {
                parsed = Json.Json.Parse(text);
            }
            catch (FormatException e)
            {
                throw ApiException.BadRequest("invalid JSON body: " + e.Message);
            }
            if (!(parsed is Dictionary<string, object> obj))
            {
                throw ApiException.BadRequest("JSON body must be an object");
            }
            return new RequestBody(obj);
        }

        private static Dictionary<string, object> Error(string code, string message)
        {
            return new Dictionary<string, object>
            {
                ["ok"] = false,
                ["error"] = new Dictionary<string, object> { ["code"] = code, ["message"] = message },
            };
        }
    }
}
