using System;
using System.Collections.Generic;
using System.Linq;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>Typed edge between two machine ports.</summary>
public sealed class PipelineEdge
{
    public required string EdgeId { get; init; }
    public required string FromNodeId { get; init; }
    public required PortId FromPortId { get; init; }
    public required string ToNodeId { get; init; }
    public required PortId ToPortId { get; init; }
}

/// <summary>Executable graph assembled from the player's machinery.</summary>
public sealed class PipelineGraph
{
    private readonly Dictionary<string, IPipelineNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<PipelineEdge> _edges = new();

    public IReadOnlyDictionary<string, IPipelineNode> Nodes => _nodes;
    public IReadOnlyList<PipelineEdge> Edges => _edges;

    public void AddNode(IPipelineNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_nodes.TryAdd(node.NodeId, node))
        {
            throw new InvalidOperationException($"Duplicate pipeline node id '{node.NodeId}'.");
        }
    }

    public void AddEdge(PipelineEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _edges.Add(edge);
    }

    public PortDefinition? FindPort(string nodeId, PortId portId)
    {
        if (!_nodes.TryGetValue(nodeId, out IPipelineNode? node))
        {
            return null;
        }

        return node.Ports.FirstOrDefault(p => p.Id.Value == portId.Value);
    }

    public IEnumerable<PipelineEdge> Incoming(string nodeId) =>
        _edges.Where(e => e.ToNodeId == nodeId);

    public IEnumerable<PipelineEdge> Outgoing(string nodeId) =>
        _edges.Where(e => e.FromNodeId == nodeId);

    public IPipelineNode? FindByKind(PipelineNodeKind kind) =>
        _nodes.Values.FirstOrDefault(n => n.Kind == kind);
}
