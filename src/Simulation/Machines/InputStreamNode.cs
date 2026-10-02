using System.Collections.Generic;
using System.Threading.Tasks;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.Simulation.Machines;

/// <summary>
/// Source machine that emits the level's input array as a single IntegerStream.
/// </summary>
public sealed class InputStreamNode : IPipelineNode
{
    private readonly IReadOnlyList<int> _sourceValues;

    public InputStreamNode(string nodeId, IReadOnlyList<int> sourceValues)
    {
        NodeId = nodeId;
        _sourceValues = sourceValues;
        Ports = new[]
        {
            new PortDefinition
            {
                Id = PortId.Out,
                Direction = PortDirection.Output,
                DataType = PortDataType.IntegerStream,
                Role = PortRole.DataStream
            }
        };
    }

    public string NodeId { get; }
    public PipelineNodeKind Kind => PipelineNodeKind.InputStream;
    public IReadOnlyList<PortDefinition> Ports { get; }

    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context)
    {
        int[] values = new int[_sourceValues.Count];
        for (int i = 0; i < _sourceValues.Count; i++)
        {
            values[i] = _sourceValues[i];
        }

        var outputs = new Dictionary<PortId, DataStream>
        {
            [PortId.Out] = DataStream.FromIntegers(values, label: "level_input")
        };
        return Task.FromResult(NodeExecutionResult.Ok(outputs));
    }
}
