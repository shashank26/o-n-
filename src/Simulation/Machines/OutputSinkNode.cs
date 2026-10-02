using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.Simulation.Machines;

/// <summary>
/// Terminal sink that receives an IntegerStream for level evaluation.
/// </summary>
public sealed class OutputSinkNode : IPipelineNode
{
    public OutputSinkNode(string nodeId)
    {
        NodeId = nodeId;
        Ports = new[]
        {
            new PortDefinition
            {
                Id = PortId.In,
                Direction = PortDirection.Input,
                DataType = PortDataType.IntegerStream,
                Role = PortRole.DataStream
            }
        };
    }

    public string NodeId { get; }
    public PipelineNodeKind Kind => PipelineNodeKind.OutputSink;
    public IReadOnlyList<PortDefinition> Ports { get; }

    public DataStream? LastReceived { get; private set; }

    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context)
    {
        if (!context.Inputs.TryGetValue(PortId.In, out DataStream? incoming) || incoming is null)
        {
            return Task.FromResult(NodeExecutionResult.Fail(new ExecutionFailure
            {
                NodeId = NodeId,
                FailureType = ExecutionFailureType.MissingInput,
                Reason = "Output received no input stream.",
                RelatedPort = PortId.In
            }));
        }

        if (incoming.DataType != PortDataType.IntegerStream)
        {
            return Task.FromResult(NodeExecutionResult.Fail(new ExecutionFailure
            {
                NodeId = NodeId,
                FailureType = ExecutionFailureType.IncompatibleType,
                Reason = $"Output expected IntegerStream but received {incoming.DataType}.",
                RelatedPort = PortId.In
            }));
        }

        LastReceived = incoming;
        int[] values = incoming.IntegerValues.ToArray();
        context.Trace.Record(new ExecutionStep
        {
            Sequence = context.Trace.Count,
            NodeId = NodeId,
            StepType = ExecutionStepType.OutputReceived,
            StateBefore = values,
            StateAfter = values,
            Values = values,
            RelatedPort = PortId.In.Value,
            CpuCycles = context.GetCpuCycles(),
            RamSlots = context.GetRamSlots(),
            PeakRamSlots = context.GetPeakRamSlots()
        });

        return Task.FromResult(NodeExecutionResult.Ok(new Dictionary<PortId, DataStream>()));
    }
}
