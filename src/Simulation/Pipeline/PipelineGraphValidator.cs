using System;
using System.Collections.Generic;
using System.Linq;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>
/// Graph validation answers: "Can this machinery execute?"
/// Does not run algorithms or evaluate level success.
/// </summary>
public static class PipelineGraphValidator
{
    public sealed class ValidationResult
    {
        public required bool IsValid { get; init; }
        public ExecutionFailure? Failure { get; init; }
    }

    public static ValidationResult Validate(PipelineGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.Nodes.Count == 0)
        {
            return Invalid("graph", ExecutionFailureType.InvalidGraph, "Pipeline has no machines.");
        }

        IPipelineNode? input = graph.FindByKind(PipelineNodeKind.InputStream);
        IPipelineNode? output = graph.FindByKind(PipelineNodeKind.OutputSink);
        if (input is null)
        {
            return Invalid("graph", ExecutionFailureType.InvalidGraph, "Pipeline is missing an Input Stream source.");
        }

        if (output is null)
        {
            return Invalid("graph", ExecutionFailureType.InvalidGraph, "Pipeline is missing an Output sink.");
        }

        foreach (PipelineEdge edge in graph.Edges)
        {
            PortDefinition? fromPort = graph.FindPort(edge.FromNodeId, edge.FromPortId);
            PortDefinition? toPort = graph.FindPort(edge.ToNodeId, edge.ToPortId);
            if (fromPort is null || toPort is null)
            {
                return Invalid(
                    edge.ToNodeId,
                    ExecutionFailureType.InvalidGraph,
                    $"Connection '{edge.EdgeId}' references an unknown port.",
                    relatedConnectionId: edge.EdgeId);
            }

            if (!fromPort.IsCompatibleWith(toPort))
            {
                return Invalid(
                    edge.ToNodeId,
                    ExecutionFailureType.IncompatibleType,
                    $"Incompatible connection: {fromPort.DataType} cannot feed {toPort.DataType}.",
                    relatedPort: toPort.Id,
                    relatedConnectionId: edge.EdgeId);
            }

            if (fromPort.Direction != PortDirection.Output || toPort.Direction != PortDirection.Input)
            {
                return Invalid(
                    edge.ToNodeId,
                    ExecutionFailureType.InvalidGraph,
                    "Connections must run from an Output port into an Input port.",
                    relatedConnectionId: edge.EdgeId);
            }
        }

        // Single-cardinality inputs may not have multiple drivers.
        foreach (IPipelineNode node in graph.Nodes.Values)
        {
            foreach (PortDefinition port in node.Ports.Where(p => p.Direction == PortDirection.Input))
            {
                int drivers = graph.Incoming(node.NodeId).Count(e => e.ToPortId.Value == port.Id.Value);
                if (port.Cardinality == PortCardinality.Single && drivers > 1)
                {
                    return Invalid(
                        node.NodeId,
                        ExecutionFailureType.InvalidGraph,
                        $"Port '{port.Id}' allows only one incoming stream but has {drivers}.",
                        relatedPort: port.Id);
                }
            }
        }

        // Reachability from Input along directed edges.
        var reachable = new HashSet<string>(StringComparer.Ordinal) { input.NodeId };
        var queue = new Queue<string>();
        queue.Enqueue(input.NodeId);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (PipelineEdge edge in graph.Outgoing(current))
            {
                if (reachable.Add(edge.ToNodeId))
                {
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        if (!reachable.Contains(output.NodeId))
        {
            return Invalid(
                output.NodeId,
                ExecutionFailureType.UnreachableNode,
                "Output is not reachable from the Input Stream.");
        }

        foreach (IPipelineNode node in graph.Nodes.Values)
        {
            if (node.Kind is PipelineNodeKind.InputStream or PipelineNodeKind.OutputSink)
            {
                continue;
            }

            if (!reachable.Contains(node.NodeId))
            {
                return Invalid(
                    node.NodeId,
                    ExecutionFailureType.UnreachableNode,
                    $"Machine '{node.NodeId}' is unreachable from the Input Stream.");
            }
        }

        return new ValidationResult { IsValid = true };
    }

    private static ValidationResult Invalid(
        string nodeId,
        ExecutionFailureType type,
        string reason,
        PortId? relatedPort = null,
        string? relatedConnectionId = null)
    {
        return new ValidationResult
        {
            IsValid = false,
            Failure = new ExecutionFailure
            {
                NodeId = nodeId,
                FailureType = type,
                Reason = reason,
                RelatedPort = relatedPort,
                RelatedConnectionId = relatedConnectionId
            }
        };
    }
}
