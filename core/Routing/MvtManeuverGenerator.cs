using MaplibreNative.Routing.Core.Graph;
using MaplibreNative.Routing.Core.Models;
using MaplibreNative.Routing.Core.Utils;

namespace MaplibreNative.Routing.Core.Routing;

internal static class MvtManeuverGenerator
{
    private static readonly string[] Cardinals = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
    private const double TinyStepM = 3;   // a step this short (two pieces nearly meeting) is no maneuver of its own

    public static (IReadOnlyList<LegStep> Steps, IReadOnlyList<(double Lon, double Lat)> Shape)
        Generate(List<RoadEdge> edgePath, RoadGraph graph)
    {
        if (edgePath.Count == 0)
            return ([], []);

        var nodeMap = new Dictionary<int, GraphNode>();
        foreach (var n in graph.Nodes)
            nodeMap[n.Id] = n;

        var shape = BuildShape(edgePath, nodeMap);
        var rawSteps = ClassifyEdges(edgePath, nodeMap, shape, graph);
        var merged = MergeSteps(rawSteps);
        var steps = GenerateInstructions(merged, shape);

        return (steps, shape);
    }

    private static List<(double Lon, double Lat)> BuildShape(
        List<RoadEdge> edges, Dictionary<int, GraphNode> nodeMap)
    {
        var shape = new List<(double Lon, double Lat)>();

        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            var fromNode = nodeMap[edge.FromNodeId];

            if (i == 0 || shape.Count == 0)
                shape.Add((fromNode.Lon, fromNode.Lat));

            foreach (var pt in edge.IntermediatePoints)
                shape.Add(pt);

            var toNode = nodeMap[edge.ToNodeId];
            shape.Add((toNode.Lon, toNode.Lat));
        }

        return shape;
    }

    private sealed class RawStep
    {
        public ManeuverType Type;
        public int EdgeIndex;
        public int BeginShapeIndex;
        public int EndShapeIndex;
        public double Distance;
        public double Duration;
        public string? StreetName;
        public string? Ref;
        public string RoadClass = "";
        public (double Lon, double Lat) ManeuverLocation;
        public string? TravelMode;
        /// <summary>Its start is where roads meet: somewhere a driver chooses, not only a bend.</summary>
        public bool Junction;
        /// <summary>Of the ways on from its start, it's the straightest: where a road bends at a junction.</summary>
        public bool Straightest = true;
    }

    private static List<RawStep> ClassifyEdges(
        List<RoadEdge> edges, Dictionary<int, GraphNode> nodeMap,
        List<(double Lon, double Lat)> shape, RoadGraph graph)
    {
        // How many other nodes a node joins: more than two, and it's a junction
        bool IsJunction(int node) =>
            graph.ForwardAdjacency[node].Select(e => e.ToNodeId)
                .Concat(graph.BackwardAdjacency[node].Select(e => e.FromNodeId))
                .Distinct().Count() > 2;

        var steps = new List<RawStep>();
        int shapeIdx = 0;

        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            int beginIdx = shapeIdx;
            int endIdx = beginIdx + 1 + edge.IntermediatePoints.Count;

            ManeuverType type;
            bool straightest = true;
            if (i == 0)
            {
                type = ManeuverType.Start;
            }
            else
            {
                var prevEdge = edges[i - 1];
                var prevTo = nodeMap[prevEdge.ToNodeId];
                var prevFrom = nodeMap[prevEdge.FromNodeId];

                double prevBearing;
                if (prevEdge.IntermediatePoints.Count > 0)
                {
                    var lastIntermediate = prevEdge.IntermediatePoints[^1];
                    prevBearing = RouteUtils.InitialBearing(lastIntermediate.Lat, lastIntermediate.Lon, prevTo.Lat, prevTo.Lon);
                }
                else
                {
                    prevBearing = RouteUtils.InitialBearing(prevFrom.Lat, prevFrom.Lon, prevTo.Lat, prevTo.Lon);
                }

                var curFrom = nodeMap[edge.FromNodeId];
                double nextBearing;
                if (edge.IntermediatePoints.Count > 0)
                {
                    var firstIntermediate = edge.IntermediatePoints[0];
                    nextBearing = RouteUtils.InitialBearing(curFrom.Lat, curFrom.Lon, firstIntermediate.Lat, firstIntermediate.Lon);
                }
                else
                {
                    var curTo = nodeMap[edge.ToNodeId];
                    nextBearing = RouteUtils.InitialBearing(curFrom.Lat, curFrom.Lon, curTo.Lat, curTo.Lon);
                }

                double angle = RouteUtils.TurnAngle(prevBearing, nextBearing);
                type = ClassifyTurn(angle);
                // Another way on that's straighter than this one: here the road turns off it
                foreach (var other in graph.ForwardAdjacency[edge.FromNodeId])
                {
                    if (other.ToNodeId == edge.ToNodeId || other.ToNodeId == prevEdge.FromNodeId) continue;
                    var to = other.IntermediatePoints.Count > 0 ? other.IntermediatePoints[0]
                        : (nodeMap[other.ToNodeId].Lon, nodeMap[other.ToNodeId].Lat);
                    double b = RouteUtils.InitialBearing(curFrom.Lat, curFrom.Lon, to.Lat, to.Lon);
                    if (Math.Abs(RouteUtils.TurnAngle(prevBearing, b)) < Math.Abs(angle)) straightest = false;
                }
            }

            steps.Add(new RawStep
            {
                Type = type,
                EdgeIndex = i,
                BeginShapeIndex = beginIdx,
                EndShapeIndex = endIdx,
                Distance = edge.DistanceMeters,
                Duration = edge.TravelSeconds,
                StreetName = edge.StreetName,
                Ref = edge.Ref,
                RoadClass = edge.RoadClass,
                ManeuverLocation = shape[beginIdx],
                TravelMode = edge.RoadClass is "path" ? "pedestrian" : "drive",
                Junction = i > 0 && IsJunction(edge.FromNodeId),
                Straightest = straightest,
            });

            shapeIdx = endIdx;
        }

        return steps;
    }

    private static ManeuverType ClassifyTurn(double angle)
    {
        double abs = Math.Abs(angle);
        bool right = angle >= 0;

        if (abs <= 10) return ManeuverType.Continue;
        if (abs <= 45) return right ? ManeuverType.SlightRight : ManeuverType.SlightLeft;
        if (abs <= 120) return right ? ManeuverType.Right : ManeuverType.Left;
        if (abs <= 170) return right ? ManeuverType.SharpRight : ManeuverType.SharpLeft;
        return right ? ManeuverType.UturnRight : ManeuverType.UturnLeft;
    }

    private static List<RawStep> MergeSteps(List<RawStep> steps)
    {
        if (steps.Count <= 1) return steps;

        var merged = new List<RawStep> { steps[0] };

        for (int i = 1; i < steps.Count; i++)
        {
            var prev = merged[^1];
            var curr = steps[i];

            bool sameStreet = string.Equals(prev.StreetName, curr.StreetName, StringComparison.Ordinal)
                              && string.Equals(prev.Ref, curr.Ref, StringComparison.Ordinal);
            // On the same road, only a real turn where roads meet is a maneuver: a bend with nothing to choose, or
            // a slight one through a junction, is the road going on; and a step of a metre or two is none at all
            bool goesOn = curr.Type is ManeuverType.Continue or ManeuverType.SlightLeft or ManeuverType.SlightRight
                          || !curr.Junction || curr.Straightest;
            bool tiny = curr.Distance < TinyStepM && i + 1 < steps.Count;

            if ((sameStreet && goesOn) || tiny)
            {
                prev.EndShapeIndex = curr.EndShapeIndex;
                prev.Distance += curr.Distance;
                prev.Duration += curr.Duration;
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }

    private static List<LegStep> GenerateInstructions(
        List<RawStep> steps, List<(double Lon, double Lat)> shape)
    {
        var result = new List<LegStep>(steps.Count + 1);

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var names = new List<string>();
            if (!string.IsNullOrEmpty(step.StreetName)) names.Add(step.StreetName!);
            if (!string.IsNullOrEmpty(step.Ref) && step.Ref != step.StreetName) names.Add(step.Ref!);

            string streetLabel = names.Count > 0 ? string.Join(" / ", names) : "";
            string instruction = BuildInstruction(step.Type, streetLabel, step.BeginShapeIndex, shape);

            string? verbalPre = null;
            if (i + 1 < steps.Count)
            {
                var nextStep = steps[i + 1];
                var nextNames = new List<string>();
                if (!string.IsNullOrEmpty(nextStep.StreetName)) nextNames.Add(nextStep.StreetName!);
                if (!string.IsNullOrEmpty(nextStep.Ref) && nextStep.Ref != nextStep.StreetName) nextNames.Add(nextStep.Ref!);
                string nextLabel = nextNames.Count > 0 ? string.Join(" / ", nextNames) : "";
                string distLabel = FormatDistance(step.Distance);
                string turnVerb = TurnVerb(nextStep.Type);
                verbalPre = string.IsNullOrEmpty(nextLabel)
                    ? $"In {distLabel}, {turnVerb}"
                    : $"In {distLabel}, {turnVerb} onto {nextLabel}";
            }

            string? verbalPost = i < steps.Count - 1
                ? $"Continue for {FormatDistance(step.Distance)}"
                : null;

            result.Add(new LegStep
            {
                Distance = step.Distance,
                Duration = step.Duration,
                StreetNames = names,
                Instruction = instruction,
                VerbalPreInstruction = verbalPre,
                VerbalPostInstruction = verbalPost,
                Type = step.Type,
                RoadClass = step.RoadClass,
                BeginShapeIndex = step.BeginShapeIndex,
                EndShapeIndex = step.EndShapeIndex,
                ManeuverLocation = step.ManeuverLocation,
                TravelMode = step.TravelMode,
            });
        }

        // destination step
        if (shape.Count > 0)
        {
            var lastPt = shape[^1];
            result.Add(new LegStep
            {
                Distance = 0,
                Duration = 0,
                StreetNames = [],
                Instruction = "Arrive at destination",
                Type = ManeuverType.Destination,
                BeginShapeIndex = shape.Count - 1,
                EndShapeIndex = shape.Count - 1,
                ManeuverLocation = lastPt,
            });
        }

        return result;
    }

    private static string BuildInstruction(ManeuverType type, string streetLabel,
        int shapeIndex, List<(double Lon, double Lat)> shape)
    {
        if (type == ManeuverType.Start)
        {
            string dir = "";
            if (shapeIndex + 1 < shape.Count)
            {
                double bearing = RouteUtils.InitialBearing(
                    shape[shapeIndex].Lat, shape[shapeIndex].Lon,
                    shape[shapeIndex + 1].Lat, shape[shapeIndex + 1].Lon);
                dir = BearingToCardinal(bearing);
            }
            return string.IsNullOrEmpty(streetLabel)
                ? $"Head {dir}"
                : $"Head {dir} on {streetLabel}";
        }

        if (type == ManeuverType.Continue)
        {
            return string.IsNullOrEmpty(streetLabel)
                ? "Continue"
                : $"Continue onto {streetLabel}";
        }

        string verb = TurnVerb(type);
        return string.IsNullOrEmpty(streetLabel)
            ? char.ToUpper(verb[0]) + verb[1..]
            : $"{char.ToUpper(verb[0])}{verb[1..]} onto {streetLabel}";
    }

    private static string TurnVerb(ManeuverType type) => type switch
    {
        ManeuverType.SlightRight => "bear right",
        ManeuverType.Right => "turn right",
        ManeuverType.SharpRight => "make a sharp right",
        ManeuverType.UturnRight => "make a U-turn",
        ManeuverType.SlightLeft => "bear left",
        ManeuverType.Left => "turn left",
        ManeuverType.SharpLeft => "make a sharp left",
        ManeuverType.UturnLeft => "make a U-turn",
        ManeuverType.Continue => "continue",
        ManeuverType.Start => "depart",
        ManeuverType.Destination => "arrive",
        _ => "continue",
    };

    private static string BearingToCardinal(double bearing)
    {
        int idx = (int)Math.Round(bearing / 45.0) % 8;
        return Cardinals[idx];
    }

    private static string FormatDistance(double meters)
    {
        if (meters >= 1000)
            return $"{meters / 1000:0.#} km";
        if (meters >= 200)
            return $"{Math.Round(meters / 100) * 100:0} m";
        if (meters >= 50)
            return $"{Math.Round(meters / 10) * 10:0} m";
        return "shortly";
    }
}
