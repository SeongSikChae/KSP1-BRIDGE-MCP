namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>The first node as plain values when execute_node or warp (to node) starts.</summary>
    public sealed class NodeStartReading
    {
        public double Now;

        /// <summary>Why nodes are not usable (spec terms "노드 사용 가능"); null when they are.</summary>
        public string Unavailable;

        /// <summary>Landed, splashed or on the launch pad.</summary>
        public bool OnSurface;

        public bool HasNode;
        public double NodeUt;

        /// <summary>The game computed an orbit after the first node.</summary>
        public bool HasAfter;

        /// <summary>The estimated burn time of the first node (delta-v left); null when it cannot be estimated.</summary>
        public double? BurnSeconds;
    }

    /// <summary>Why the first node cannot be started, or when its burn starts.</summary>
    public sealed class NodeStartResult
    {
        /// <summary>Null when it can start.</summary>
        public string Failure;

        public double NodeUt;
        public double BurnSeconds;

        /// <summary>Node UT − burn time ÷ 2 (spec terms "연소 시작 시각").</summary>
        public double StartUt;
    }

    /// <summary>The start checks shared by execute_node (spec C5) and warp to node (spec C6), and the burn start (D014).</summary>
    public static class NodeStart
    {
        public static NodeStartResult Judge(NodeStartReading r)
        {
            string failure = Failure(r);
            if (failure != null)
                return new NodeStartResult { Failure = failure };
            double burn = r.BurnSeconds.Value;
            return new NodeStartResult { NodeUt = r.NodeUt, BurnSeconds = burn, StartUt = r.NodeUt - burn / 2 };
        }

        private static string Failure(NodeStartReading r)
        {
            if (r.Unavailable != null)
                return r.Unavailable;
            if (!r.HasNode)
                return "there is no maneuver node; make one with create_node";
            if (r.OnSurface)
                return "the vessel is landed, splashed or on the launch pad";
            if (r.Now >= r.NodeUt)
                return "the first node's time has passed (D009); delete it and make a new one";
            if (!r.HasAfter)
                return "the game computed no orbit after the first node";
            if (!r.BurnSeconds.HasValue)
                return "the burn time cannot be estimated: no delta-v calculator stages and no running engine";
            return null;
        }
    }
}
