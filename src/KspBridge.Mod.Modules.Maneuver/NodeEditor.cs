using KspBridge.Mod.Core;
using KspBridge.Mod.Modules.Flight;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace KspBridge.Mod.Modules.Maneuver
{
    /// <summary>Makes and removes maneuver nodes on the active vessel (KSP side, spec C2, C4).</summary>
    internal static class NodeEditor
    {
        /// <summary>The goal is met within this (m) without correcting; past it the prograde component is corrected.</summary>
        public const double CorrectM = 0.5;

        /// <summary>A goal the game's orbit after the node misses by more than this (m) is refused (spec C2).</summary>
        public const double GoalM = 1;

        public const int Corrections = 5;

        /// <summary>Probe step (m/s) of the first secant correction.</summary>
        private const double ProbeMps = 0.01;

        /// <summary>Adds the node and returns it; bad_request (and no node) when it cannot be made.</summary>
        public static ManeuverNode Create(Vessel vessel, CreateNodeArgs args)
        {
            PatchedConicSolver solver = vessel.patchedConicSolver;
            double now = Planetarium.GetUniversalTime();
            List<ManeuverNode> nodes = NodeReader.Nodes(vessel);
            ManeuverNode last = nodes.Count > 0 ? nodes[nodes.Count - 1] : null;
            Orbit reference = last != null ? last.nextPatch : vessel.orbit;
            if (reference == null)
                throw BridgeException.BadRequest("the last node has no orbit after it, so a node cannot follow it");

            double ut = NodeTime.Resolve(args, now, last?.UT, NodeReader.OnSurface(vessel), Reference(reference));
            NodeReader.State(reference, ut, out Vec r, out Vec v);
            NodeFrame frame = NodeFrame.At(r, v);
            Vector3d deltaV;
            if (args.Goal == NodeGoalKind.Components)
            {
                deltaV = new Vector3d(args.RadialMps, args.NormalMps, args.ProgradeMps);
            }
            else
            {
                GoalBody body = new GoalBody
                {
                    Gm = reference.referenceBody.gravParameter,
                    Radius = reference.referenceBody.Radius,
                    SoiRadius = reference.referenceBody.sphereOfInfluence,
                };
                Vec dv = NodeGoal.DeltaV(args.Goal, args.GoalValue, r, v, body);
                deltaV = new Vector3d(frame.RadialOf(dv), frame.NormalOf(dv), frame.ProgradeOf(dv));
            }

            ManeuverNode node = solver.AddManeuverNode(ut);
            try
            {
                node.OnGizmoUpdated(deltaV, ut);
                if (node.nextPatch == null)
                    throw BridgeException.BadRequest(NodeInfo.NoAfterReason);
                Meet(node, args, r.Length - reference.referenceBody.Radius);
                return node;
            }
            catch (Exception)
            {
                Remove(solver, node);
                throw;
            }
        }

        /// <summary>Corrects an apoapsis or periapsis goal on the game's own orbit after the node, and checks every goal (spec C2, D015).</summary>
        private static void Meet(ManeuverNode node, CreateNodeArgs args, double nodeAltitude)
        {
            switch (args.Goal)
            {
                case NodeGoalKind.Circular:
                    double ap = node.nextPatch.ApA, pe = node.nextPatch.PeA;
                    if (!(Math.Abs(ap - nodeAltitude) <= GoalM && Math.Abs(pe - nodeAltitude) <= GoalM))
                        throw BridgeException.BadRequest("the game's orbit after the node is not circular within 1 m (apoapsis " + Text(ap) +
                                                         ", periapsis " + Text(pe) + ", node altitude " + Text(nodeAltitude) + ")");
                    return;
                case NodeGoalKind.Apoapsis:
                case NodeGoalKind.Periapsis:
                    Secant(node, args.Goal == NodeGoalKind.Apoapsis, args.GoalValue);
                    return;
            }
        }

        private static void Secant(ManeuverNode node, bool apoapsis, double target)
        {
            Func<double> miss = () => node.nextPatch == null ? double.NaN : (apoapsis ? node.nextPatch.ApA : node.nextPatch.PeA) - target;
            double p0 = node.DeltaV.z;
            double e0 = miss();
            double p1 = p0 + ProbeMps;
            for (int i = 0; i < Corrections && !(Math.Abs(e0) <= CorrectM); i++)
            {
                Set(node, p1);
                double e1 = miss();
                if (double.IsNaN(e1) || e1 == e0)
                    break;
                double next = p1 - e1 * (p1 - p0) / (e1 - e0);
                p0 = p1;
                e0 = e1;
                p1 = next;
            }
            if (!(Math.Abs(e0) <= CorrectM))
            {
                // The last probe may be the better one; keep the closer of what was tried.
                Set(node, p0);
                e0 = miss();
            }
            if (!(Math.Abs(e0) <= GoalM))
                throw BridgeException.BadRequest("the game's orbit after the node misses the " + (apoapsis ? "apoapsis" : "periapsis") +
                                                 " goal by " + Text(e0) + " m after correction; the reference orbit cannot reach it");
        }

        private static void Set(ManeuverNode node, double prograde)
            => node.OnGizmoUpdated(new Vector3d(node.DeltaV.x, node.DeltaV.y, prograde), node.UT);

        /// <summary>Removes the node at <paramref name="index"/> (UT order) or every node; bad_request for a number that is not there.</summary>
        public static void Delete(Vessel vessel, DeleteNodeArgs args)
        {
            PatchedConicSolver solver = vessel.patchedConicSolver;
            List<ManeuverNode> nodes = NodeReader.Nodes(vessel);
            if (args.All)
            {
                foreach (ManeuverNode n in nodes)
                    Remove(solver, n);
                return;
            }
            int index = args.Index.Value;
            if (index >= nodes.Count)
                throw BridgeException.BadRequest("there is no node " + index + " (" + nodes.Count + " node" + (nodes.Count == 1 ? "" : "s") + ")");
            Remove(solver, nodes[index]);
        }

        /// <summary>
        /// Removes the node the way the game does (decompiled ManeuverNode.RemoveSelf: detaches the editing gizmo and removes the
        /// node's map object, then PatchedConicSolver.RemoveManeuverNode), so no map icon or gizmo is left behind.
        /// </summary>
        public static void Remove(PatchedConicSolver solver, ManeuverNode node)
        {
            if (solver != null && node != null && solver.maneuverNodes.Contains(node))
                node.RemoveSelf();
        }

        private static ReferenceOrbit Reference(Orbit o) => new ReferenceOrbit
        {
            Eccentricity = o.eccentricity,
            Period = o.period,
            ObtAt = o.getObtAtUT,
            End = o.patchEndTransition == Orbit.PatchTransitionType.ESCAPE ? SegmentEnd.Escape
                : o.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER ? SegmentEnd.Encounter
                : o.patchEndTransition == Orbit.PatchTransitionType.FINAL ? SegmentEnd.Final
                : SegmentEnd.Other,
            EndUt = o.EndUT,
        };

        private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
