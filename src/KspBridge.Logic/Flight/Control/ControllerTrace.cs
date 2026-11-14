using UnityEngine;

namespace KspBridge.Flight.Control
{
    /// <summary>
    /// Ring buffer of the attitude controller's ticks (design 0053 decision 2): what Steer actually computed and output, recorded
    /// only on ticks it steers. Observation only. Holds one vessel's samples: cleared when a plan starts, on a scene load request,
    /// and when another vessel records. Main thread only.
    /// </summary>
    internal static class ControllerTrace
    {
        internal const int Capacity = 1000;   // ~20 s at 50 Hz
        internal const int Kinds = 5;         // wheel, gimbal, surface, rcs, other
        internal static readonly string[] KindNames = { "wheel", "gimbal", "surface", "rcs", "other" };

        /// <summary>One steered tick. Vectors are per axis (x pitch, y roll, z yaw) in Steer's frame and signs.</summary>
        internal struct Sample
        {
            public double Ut, Throttle, Q;
            public Vector3 Err, Omega, Want, Input, Alpha, Moi;   // rad, rad/s, rad/s, ±1, rad/s², t·m²
            public Vector3[] Torque;   // available torque per kind (kN·m), Kinds long, a new array per sample
        }

        private static readonly Sample[] _buf = new Sample[Capacity];
        private static int _start, _count;
        private static uint _vessel;   // persistentId of the recorded vessel

        internal static int Count => _count;

        internal static void Record(uint vessel, in Sample s)
        {
            if (_count > 0 && vessel != _vessel)
            {
                Clear();   // never mix vessels
            }
            _vessel = vessel;
            _buf[(_start + _count) % Capacity] = s;
            if (_count < Capacity)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % Capacity;
            }
        }

        internal static void Clear()
        {
            _start = 0;
            _count = 0;
        }
    }
}
