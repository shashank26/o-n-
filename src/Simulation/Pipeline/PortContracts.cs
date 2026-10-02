using System;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>Stable identifier for a node port (e.g. "in", "out").</summary>
public readonly record struct PortId(string Value)
{
    public static PortId In { get; } = new("in");
    public static PortId Out { get; } = new("out");
    public static PortId Stream { get; } = new("stream");

    public override string ToString() => Value;

    public static implicit operator string(PortId id) => id.Value;
}

public enum PortDirection
{
    Input,
    Output
}

/// <summary>Semantic payload kinds carried by a <see cref="DataStream"/>.</summary>
public enum PortDataType
{
    IntegerStream,
    // Reserved for future machines — not implemented in this milestone:
    Graph,
    GraphNode,
    NodeStream,
    Path,
    Number,
    Any
}

public enum PortRole
{
    DataStream,
    GraphTopology,
    SeedNode,
    Metric,
    Generic
}

public enum PortCardinality
{
    Single,
    Multiple
}

/// <summary>Typed contract for a machine port.</summary>
public sealed class PortDefinition
{
    public required PortId Id { get; init; }
    public required PortDirection Direction { get; init; }
    public required PortDataType DataType { get; init; }
    public required PortRole Role { get; init; }
    public PortCardinality Cardinality { get; init; } = PortCardinality.Single;

    public bool IsCompatibleWith(PortDefinition other)
    {
        if (Direction == other.Direction)
        {
            return false;
        }

        PortDefinition output = Direction == PortDirection.Output ? this : other;
        PortDefinition input = Direction == PortDirection.Input ? this : other;
        if (output.Direction != PortDirection.Output || input.Direction != PortDirection.Input)
        {
            return false;
        }

        return output.DataType == input.DataType
            || output.DataType == PortDataType.Any
            || input.DataType == PortDataType.Any;
    }
}
