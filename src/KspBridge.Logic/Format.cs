using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspBridge
{
    /// <summary>JSON-friendly values for responses.</summary>
    public static class Format
    {
        /// <summary>[x, y, z] rounded to millimetres.</summary>
        public static List<object> Vec(Vector3 v) => new List<object> { Round(v.x), Round(v.y), Round(v.z) };

        public static double Round(float value) => Math.Round(value, 3);
    }
}
