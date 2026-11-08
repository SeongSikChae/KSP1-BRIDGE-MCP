using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge
{
    /// <summary>An error returned to the client as {"ok":false,"error":{code,message}}.</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }
        public string Code { get; }

        public static ApiException BadRequest(string message) => new ApiException(400, "bad_request", message);
        public static ApiException NotFound(string message) => new ApiException(404, "not_found", message);
        public static ApiException NotCareer() => new ApiException(409, "not_career", "available in career mode only");
        public static ApiException SceneNotAllowed(string what) =>
            new ApiException(409, "scene_not_allowed", what + " is not allowed in scene " + HighLogic.LoadedScene);
    }

    public static class Career
    {
        public static bool IsCareer => HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER;

        public static void Require()
        {
            if (!IsCareer)
            {
                throw ApiException.NotCareer();
            }
        }
    }

    /// <summary>Typed access to a parsed JSON request body.</summary>
    public sealed class RequestBody
    {
        private readonly Dictionary<string, object> _values;

        public RequestBody(Dictionary<string, object> values)
        {
            _values = values ?? new Dictionary<string, object>();
        }

        public bool Has(string name) => _values.TryGetValue(name, out object v) && v != null;

        public double GetDouble(string name)
        {
            if (!_values.TryGetValue(name, out object v) || v == null)
            {
                throw ApiException.BadRequest($"'{name}' is required");
            }
            if (v is double d)
            {
                return d;
            }
            throw ApiException.BadRequest($"'{name}' must be a number");
        }

        public double GetDouble(string name, double defaultValue) => Has(name) ? GetDouble(name) : defaultValue;

        public bool GetBool(string name)
        {
            if (_values.TryGetValue(name, out object v) && v is bool b)
            {
                return b;
            }
            throw ApiException.BadRequest($"'{name}' must be true or false");
        }

        public Dictionary<string, object> GetObject(string name)
        {
            if (_values.TryGetValue(name, out object v) && v is Dictionary<string, object> d)
            {
                return d;
            }
            throw ApiException.BadRequest($"'{name}' must be an object");
        }

        public object GetRaw(string name) => _values.TryGetValue(name, out object v) ? v : null;

        public List<object> GetList(string name)
        {
            if (_values.TryGetValue(name, out object v) && v is List<object> l)
            {
                return l;
            }
            throw ApiException.BadRequest($"'{name}' must be an array");
        }

        public string GetString(string name)
        {
            if (_values.TryGetValue(name, out object v) && v is string s && s.Length > 0)
            {
                return s;
            }
            throw ApiException.BadRequest($"'{name}' must be a non-empty string");
        }
    }

    public static class Guard
    {
        /// <summary>Active vessel in the flight scene, or 409 not_in_flight / no_active_vessel. Main thread only.</summary>
        public static Vessel ActiveVessel()
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                throw new ApiException(409, "not_in_flight", "not in the flight scene (current: " + HighLogic.LoadedScene + ")");
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                throw new ApiException(409, "no_active_vessel", "there is no active vessel");
            }
            return vessel;
        }

        public static int ParseIndex(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                throw ApiException.BadRequest("index must be an integer");
            }
            return index;
        }
    }
}
