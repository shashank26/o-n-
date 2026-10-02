using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation.Machines;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>Stable GraphEdit / simulation node identifiers.</summary>
public static class PipelineNodeIds
{
    public const string InputStream = "InputStream";
    public const string OutputSink = "OutputSink";

    public static string Engine(string blockId) => $"Engine_{blockId}";

    public static bool IsInputStream(string nodeId) =>
        string.Equals(nodeId, InputStream, StringComparison.Ordinal);

    public static bool IsOutputSink(string nodeId) =>
        string.Equals(nodeId, OutputSink, StringComparison.Ordinal);

    public static bool IsEngine(string nodeId) =>
        nodeId.StartsWith("Engine_", StringComparison.Ordinal);

    public static bool IsBubbleSortEngine(string nodeId) =>
        IsEngine(nodeId) && nodeId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase);

    public static string? ExtractMachineId(string engineNodeId)
    {
        if (!IsEngine(engineNodeId))
        {
            return null;
        }

        return engineNodeId["Engine_".Length..];
    }
}

/// <summary>
/// Assembles an executable <see cref="PipelineGraph"/> from level data and player routes.
/// Level input values become the Input Stream machine's payload — never fed directly to algorithms.
/// </summary>
public static class PipelineGraphBuilder
{
    public static PipelineGraph Build(LevelData level, IReadOnlyList<PipelineRouteBinding> routes)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(routes);

        var graph = new PipelineGraph();
        graph.AddNode(new InputStreamNode(PipelineNodeIds.InputStream, level.InputStream));
        graph.AddNode(new OutputSinkNode(PipelineNodeIds.OutputSink));

        var machinesNeeded = new HashSet<string>(StringComparer.Ordinal);
        foreach (PipelineRouteBinding route in routes)
        {
            CollectMachine(route.SourceNodeId, machinesNeeded);
            CollectMachine(route.TargetNodeId, machinesNeeded);
            if (!string.IsNullOrWhiteSpace(route.TargetMachineId)
                && route.TargetNodeId.StartsWith("Engine_", StringComparison.Ordinal))
            {
                machinesNeeded.Add(route.TargetNodeId);
            }
        }

        foreach (string engineNodeId in machinesNeeded)
        {
            string? machineId = PipelineNodeIds.ExtractMachineId(engineNodeId);
            if (machineId is null)
            {
                continue;
            }

            graph.AddNode(CreateMachineNode(engineNodeId, machineId));
        }

        int edgeIndex = 0;
        foreach (PipelineRouteBinding route in routes)
        {
            string? fromNodeId = NormalizeNodeId(route.SourceNodeId);
            string? toNodeId = NormalizeNodeId(route.TargetNodeId);
            if (fromNodeId is null || toNodeId is null)
            {
                continue;
            }

            // Ensure machine nodes referenced only via TargetMachineId still exist.
            EnsureMachinePresent(graph, fromNodeId);
            EnsureMachinePresent(graph, toNodeId);

            PortId fromPort = ResolveOutputPort(fromNodeId, route.SourcePort);
            PortId toPort = ResolveInputPort(toNodeId, route.TargetPort);

            graph.AddEdge(new PipelineEdge
            {
                EdgeId = $"e{edgeIndex++}",
                FromNodeId = fromNodeId,
                FromPortId = fromPort,
                ToNodeId = toNodeId,
                ToPortId = toPort
            });
        }

        return graph;
    }

    /// <summary>Builds a linear Input → BubbleSort → Output graph for tests and headless runs.</summary>
    public static PipelineGraph BuildLinearBubbleSort(LevelData level, string bubbleSortNodeId = "Engine_Bubble_Sort_Machine")
    {
        ArgumentNullException.ThrowIfNull(level);
        var routes = new List<PipelineRouteBinding>
        {
            new()
            {
                SourceNodeId = PipelineNodeIds.InputStream,
                SourcePort = 0,
                TargetNodeId = bubbleSortNodeId,
                TargetPort = 0,
                TargetMachineId = "Bubble_Sort_Machine",
                SourceDataSlotIndex = null
            },
            new()
            {
                SourceNodeId = bubbleSortNodeId,
                SourcePort = 0,
                TargetNodeId = PipelineNodeIds.OutputSink,
                TargetPort = 0,
                TargetMachineId = string.Empty,
                SourceDataSlotIndex = null
            }
        };
        return Build(level, routes);
    }

    private static void CollectMachine(string nodeId, HashSet<string> machines)
    {
        if (PipelineNodeIds.IsEngine(nodeId))
        {
            machines.Add(nodeId);
        }
    }

    private static void EnsureMachinePresent(PipelineGraph graph, string nodeId)
    {
        if (!PipelineNodeIds.IsEngine(nodeId) || graph.Nodes.ContainsKey(nodeId))
        {
            return;
        }

        string? machineId = PipelineNodeIds.ExtractMachineId(nodeId);
        if (machineId is null)
        {
            return;
        }

        graph.AddNode(CreateMachineNode(nodeId, machineId));
    }

    private static IPipelineNode CreateMachineNode(string nodeId, string machineId)
    {
        if (machineId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase))
        {
            return new BubbleSortMachineNode(nodeId);
        }

        // Unknown toolbox ids become opaque stubs that reject execution with a typed failure.
        return new UnsupportedMachineNode(nodeId, machineId);
    }

    /// <summary>
    /// Maps legacy DataSlot_* sources onto the single logical InputStream node
    /// so older visual wiring still means "the input stream feeds this machine".
    /// </summary>
    private static string? NormalizeNodeId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        if (nodeId.StartsWith("DataSlot_", StringComparison.Ordinal))
        {
            return PipelineNodeIds.InputStream;
        }

        return nodeId;
    }

    private static PortId ResolveOutputPort(string nodeId, int portIndex)
    {
        _ = portIndex;
        if (PipelineNodeIds.IsInputStream(nodeId) || PipelineNodeIds.IsEngine(nodeId))
        {
            return PortId.Out;
        }

        return PortId.Out;
    }

    private static PortId ResolveInputPort(string nodeId, int portIndex)
    {
        _ = portIndex;
        if (PipelineNodeIds.IsOutputSink(nodeId) || PipelineNodeIds.IsEngine(nodeId))
        {
            return PortId.In;
        }

        return PortId.In;
    }
}

/// <summary>
/// Placeholder for toolbox machines that are not yet implemented.
/// Execution stops here with a structured failure instead of silently succeeding.
/// </summary>
public sealed class UnsupportedMachineNode : IPipelineNode
{
    private readonly string _machineId;

    public UnsupportedMachineNode(string nodeId, string machineId)
    {
        NodeId = nodeId;
        _machineId = machineId;
        Ports = new[]
        {
            new PortDefinition
            {
                Id = PortId.In,
                Direction = PortDirection.Input,
                DataType = PortDataType.Any,
                Role = PortRole.Generic
            },
            new PortDefinition
            {
                Id = PortId.Out,
                Direction = PortDirection.Output,
                DataType = PortDataType.Any,
                Role = PortRole.Generic
            }
        };
    }

    public string NodeId { get; }
    public PipelineNodeKind Kind => PipelineNodeKind.Unsupported;
    public IReadOnlyList<PortDefinition> Ports { get; }

    public System.Threading.Tasks.Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context)
    {
        return System.Threading.Tasks.Task.FromResult(NodeExecutionResult.Fail(new ExecutionFailure
        {
            NodeId = NodeId,
            FailureType = ExecutionFailureType.InvalidGraph,
            Reason = $"Machine '{_machineId}' is not available in this build.",
            RelatedPort = PortId.In
        }));
    }
}
