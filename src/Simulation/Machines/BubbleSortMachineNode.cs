using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.Simulation.Machines;

/// <summary>
/// Bubble Sort machine: sorts an IntegerStream received on its input port.
/// Records deterministic checkpoints; does not own presentation timing.
/// </summary>
public sealed class BubbleSortMachineNode : IPipelineNode
{
    private const int TempRamSlot = 0;

    public BubbleSortMachineNode(string nodeId)
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
            },
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
    public PipelineNodeKind Kind => PipelineNodeKind.BubbleSort;
    public IReadOnlyList<PortDefinition> Ports { get; }

    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context)
    {
        if (!context.Inputs.TryGetValue(PortId.In, out DataStream? incoming) || incoming is null)
        {
            return Task.FromResult(NodeExecutionResult.Fail(new ExecutionFailure
            {
                NodeId = NodeId,
                FailureType = ExecutionFailureType.MissingInput,
                Reason = "Bubble Sort received no input stream.",
                RelatedPort = PortId.In
            }));
        }

        if (incoming.DataType != PortDataType.IntegerStream)
        {
            return Task.FromResult(NodeExecutionResult.Fail(new ExecutionFailure
            {
                NodeId = NodeId,
                FailureType = ExecutionFailureType.IncompatibleType,
                Reason = $"Bubble Sort expected IntegerStream but received {incoming.DataType}.",
                RelatedPort = PortId.In
            }));
        }

        int[] working = incoming.IntegerValues.ToArray();
        // NodeStarted is recorded by the executor so the machine can focus on algorithm steps.

        if (!context.TryAllocateRam(TempRamSlot, "swap_temp"))
        {
            return Task.FromResult(NodeExecutionResult.Fail(new ExecutionFailure
            {
                NodeId = NodeId,
                FailureType = ExecutionFailureType.RamBudgetExceeded,
                Reason = "Bubble Sort could not allocate a temporary RAM slot.",
                RelatedPort = PortId.In
            }));
        }

        int n = working.Length;
        for (int pass = 0; pass < n - 1; pass++)
        {
            for (int index = 0; index < n - 1 - pass; index++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (!context.TryConsumeCpu(1))
                {
                    context.ReleaseRam();
                    return Task.FromResult(NodeExecutionResult.Fail(new ExecutionFailure
                    {
                        NodeId = NodeId,
                        FailureType = ExecutionFailureType.CpuBudgetExceeded,
                        Reason = "CPU budget exceeded while executing Bubble Sort.",
                        RelatedPort = PortId.In
                    }));
                }

                int left = working[index];
                int right = working[index + 1];
                int[] stateBefore = working.ToArray();
                CompareOutcome outcome = left > right
                    ? CompareOutcome.GreaterThan
                    : left < right
                        ? CompareOutcome.LessThan
                        : CompareOutcome.Equal;

                context.Trace.Record(new ExecutionStep
                {
                    Sequence = context.Trace.Count,
                    NodeId = NodeId,
                    StepType = ExecutionStepType.Compare,
                    Indexes = new[] { index, index + 1 },
                    Values = new[] { left, right },
                    ValuesAfter = new[] { left, right },
                    StateBefore = stateBefore,
                    StateAfter = stateBefore,
                    CompareResult = outcome,
                    CpuCycles = context.GetCpuCycles(),
                    RamSlots = context.GetRamSlots(),
                    PeakRamSlots = context.GetPeakRamSlots()
                });

                if (!context.SuppressLiveEvents)
                {
                    context.RaiseCompared(index, index + 1);
                }

                if (left > right)
                {
                    working[index] = right;
                    working[index + 1] = left;
                    int[] stateAfter = working.ToArray();
                    context.Trace.Record(new ExecutionStep
                    {
                        Sequence = context.Trace.Count,
                        NodeId = NodeId,
                        StepType = ExecutionStepType.Swap,
                        Indexes = new[] { index, index + 1 },
                        Values = new[] { left, right },
                        ValuesAfter = new[] { right, left },
                        StateBefore = stateBefore,
                        StateAfter = stateAfter,
                        CpuCycles = context.GetCpuCycles(),
                        RamSlots = context.GetRamSlots(),
                        PeakRamSlots = context.GetPeakRamSlots()
                    });

                    if (!context.SuppressLiveEvents)
                    {
                        context.RaiseSwapped(index, index + 1);
                    }
                }
            }
        }

        context.ReleaseRam();
        DataStream output = DataStream.FromIntegers(working, label: "bubble_sort_out");

        return Task.FromResult(NodeExecutionResult.Ok(new Dictionary<PortId, DataStream>
        {
            [PortId.Out] = output
        }));
    }
}
