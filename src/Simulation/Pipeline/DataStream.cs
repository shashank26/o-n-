using System;
using System.Collections.Generic;
using System.Linq;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>
/// Immutable payload flowing between machines. An integer array is the CONTENT of an IntegerStream —
/// connections carry streams, not individual elements.
/// </summary>
public sealed class DataStream
{
    public required PortDataType DataType { get; init; }
    public required IReadOnlyList<int> IntegerValues { get; init; }
    public string? Label { get; init; }

    public static DataStream FromIntegers(IEnumerable<int> values, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new DataStream
        {
            DataType = PortDataType.IntegerStream,
            IntegerValues = values.ToArray(),
            Label = label
        };
    }

    public static DataStream EmptyInteger(string? label = null) =>
        FromIntegers(Array.Empty<int>(), label);

    public override string ToString() =>
        $"{DataType}[{string.Join(", ", IntegerValues)}]";
}
