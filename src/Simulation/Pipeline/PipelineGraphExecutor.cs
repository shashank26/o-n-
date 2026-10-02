using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PipelineArchitect.Simulation.Machines;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>
/// Walks a validated <see cref="PipelineGraph"/>, recording a deterministic <see cref="ExecutionTrace"/>.
/// Presentation timing is not applied here — yields are no-ops during recording.
/// </summary>
public sealed class PipelineGraphExecutor
{
    public sealed class ExecutionOutcome
    {
        public required bool Succeeded { get; init; }
        public required ExecutionTrace Trace { get; init; }
        public DataStream? FinalOutput { get; init; }
        public ExecutionFailure? Failure { get; init; }
        public required string ProducingNodeId { get; init; }
    }

    private readonly Func<int, bool> _tryConsumeCpu;
    private readonly Func<int, string, bool> _tryAllocateRam;
    private readonly Action _releaseRam;
    private readonly Func<int> _getCpu;
    private readonly Func<int> _getRam;
    private readonly Func<int> _getPeakRam;
    private readonly bool _suppressLiveEvents;
    private readonly Action<string> _onNodeStarted;
    private readonly Action<string> _onNodeCompleted;
    private readonly Action<string, DataStream> _onStreamProduced;
    private readonly Action<PipelineEdge, DataStream> _onConnectionTraversed;
    private readonly Action<ExecutionFailure> _onBlocked;
    private readonly Action<int, int> _raiseCompared;
    private readonly Action<int, int> _raiseSwapped;

    public PipelineGraphExecutor(
        Func<int, bool> tryConsumeCpu,
        Func<int, string, bool> tryAllocateRam,
        Action releaseRam,
        Func<int> getCpu,
        Func<int> getRam,
        Func<int> getPeakRam,
        Action<string> onNodeStarted,
        Action<string> onNodeCompleted,
        Action<string, DataStream> onStreamProduced,
        Action<PipelineEdge, DataStream> onConnectionTraversed,
        Action<ExecutionFailure> onBlocked,
        Action<int, int> raiseCompared,
        Action<int, int> raiseSwapped,
        bool suppressLiveEvents = true)
    {
        _tryConsumeCpu = tryConsumeCpu;
        _tryAllocateRam = tryAllocateRam;
        _releaseRam = releaseRam;
        _getCpu = getCpu;
        _getRam = getRam;
        _getPeakRam = getPeakRam;
        _onNodeStarted = onNodeStarted;
        _onNodeCompleted = onNodeCompleted;
        _onStreamProduced = onStreamProduced;
        _onConnectionTraversed = onConnectionTraversed;
        _onBlocked = onBlocked;
        _raiseCompared = raiseCompared;
        _raiseSwapped = raiseSwapped;
        _suppressLiveEvents = suppressLiveEvents;
    }

    public async Task<ExecutionOutcome> ExecuteAsync(
        PipelineGraph graph,
        CancellationToken cancellationToken)
    {
        var trace = new ExecutionTrace();
        PipelineGraphValidator.ValidationResult validation = PipelineGraphValidator.Validate(graph);
        if (!validation.IsValid)
        {
            ExecutionFailure failure = validation.Failure!;
            failure.CpuCycles = _getCpu();
            failure.PeakRamSlots = _getPeakRam();
            RecordBlocked(trace, failure);
            if (!_suppressLiveEvents)
            {
                _onBlocked(failure);
            }

            return new ExecutionOutcome
            {
                Succeeded = false,
                Trace = trace,
                Failure = failure,
                ProducingNodeId = failure.NodeId
            };
        }

        IPipelineNode input = graph.FindByKind(PipelineNodeKind.InputStream)!;
        IPipelineNode output = graph.FindByKind(PipelineNodeKind.OutputSink)!;

        List<string>? order = TryTopologicalOrder(graph, input.NodeId);
        if (order is null)
        {
            var failure = new ExecutionFailure
            {
                NodeId = input.NodeId,
                FailureType = ExecutionFailureType.InvalidGraph,
                Reason = "Pipeline contains a cycle; cyclic graphs are not supported yet.",
                CpuCycles = _getCpu(),
                PeakRamSlots = _getPeakRam()
            };
            return Fail(trace, failure);
        }

        if (!order.Contains(output.NodeId))
        {
            var failure = new ExecutionFailure
            {
                NodeId = output.NodeId,
                FailureType = ExecutionFailureType.UnreachableNode,
                Reason = "Output is unreachable during execution ordering.",
                CpuCycles = _getCpu(),
                PeakRamSlots = _getPeakRam()
            };
            return Fail(trace, failure);
        }

        var producedByNode = new Dictionary<string, IReadOnlyDictionary<PortId, DataStream>>(StringComparer.Ordinal);
        int[] lastState = Array.Empty<int>();

        foreach (string nodeId in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IPipelineNode node = graph.Nodes[nodeId];

            var inputs = new Dictionary<PortId, DataStream>();
            foreach (PipelineEdge edge in graph.Incoming(nodeId))
            {
                if (!producedByNode.TryGetValue(edge.FromNodeId, out IReadOnlyDictionary<PortId, DataStream>? fromOutputs)
                    || !fromOutputs.TryGetValue(edge.FromPortId, out DataStream? stream))
                {
                    var failure = new ExecutionFailure
                    {
                        NodeId = nodeId,
                        FailureType = ExecutionFailureType.MissingInput,
                        Reason = $"Machine '{nodeId}' expected a stream on port '{edge.ToPortId}' but none arrived.",
                        RelatedPort = edge.ToPortId,
                        RelatedConnectionId = edge.EdgeId,
                        CpuCycles = _getCpu(),
                        PeakRamSlots = _getPeakRam()
                    };
                    return Fail(trace, failure);
                }

                int[] payload = stream.IntegerValues.ToArray();
                trace.Record(new ExecutionStep
                {
                    Sequence = trace.Count,
                    NodeId = nodeId,
                    StepType = ExecutionStepType.ConnectionTraversed,
                    FromNodeId = edge.FromNodeId,
                    ToNodeId = edge.ToNodeId,
                    RelatedConnectionId = edge.EdgeId,
                    RelatedPort = edge.ToPortId.Value,
                    StateBefore = payload,
                    StateAfter = payload,
                    Values = payload,
                    CpuCycles = _getCpu(),
                    RamSlots = _getRam(),
                    PeakRamSlots = _getPeakRam()
                });

                if (!_suppressLiveEvents)
                {
                    _onConnectionTraversed(edge, stream);
                }

                inputs[edge.ToPortId] = stream;
                lastState = payload;
            }

            if (node.Kind == PipelineNodeKind.InputStream)
            {
                inputs = new Dictionary<PortId, DataStream>();
            }

            trace.Record(new ExecutionStep
            {
                Sequence = trace.Count,
                NodeId = nodeId,
                StepType = ExecutionStepType.NodeStarted,
                StateBefore = lastState,
                StateAfter = lastState,
                Values = lastState,
                CpuCycles = _getCpu(),
                RamSlots = _getRam(),
                PeakRamSlots = _getPeakRam()
            });

            if (!_suppressLiveEvents)
            {
                _onNodeStarted(nodeId);
            }

            var context = new NodeExecutionContext
            {
                NodeId = nodeId,
                Inputs = inputs,
                Trace = trace,
                TryConsumeCpu = _tryConsumeCpu,
                TryAllocateRam = _tryAllocateRam,
                ReleaseRam = _releaseRam,
                GetCpuCycles = _getCpu,
                GetRamSlots = _getRam,
                GetPeakRamSlots = _getPeakRam,
                RaiseCompared = _raiseCompared,
                RaiseSwapped = _raiseSwapped,
                YieldAsync = static (_, _) => Task.CompletedTask,
                StepDelay = TimeSpan.Zero,
                CancellationToken = cancellationToken,
                SuppressLiveEvents = _suppressLiveEvents
            };

            NodeExecutionResult result = await node.ExecuteAsync(context).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                ExecutionFailure failure = result.Failure!;
                failure.CpuCycles = _getCpu();
                failure.PeakRamSlots = _getPeakRam();
                return Fail(trace, failure);
            }

            producedByNode[nodeId] = result.Outputs;
            foreach ((PortId portId, DataStream stream) in result.Outputs)
            {
                int[] values = stream.IntegerValues.ToArray();
                lastState = values;
                trace.Record(new ExecutionStep
                {
                    Sequence = trace.Count,
                    NodeId = nodeId,
                    StepType = ExecutionStepType.DataStreamProduced,
                    RelatedPort = portId.Value,
                    StateBefore = values,
                    StateAfter = values,
                    Values = values,
                    CpuCycles = _getCpu(),
                    RamSlots = _getRam(),
                    PeakRamSlots = _getPeakRam()
                });

                if (!_suppressLiveEvents)
                {
                    _onStreamProduced(nodeId, stream);
                }
            }

            trace.Record(new ExecutionStep
            {
                Sequence = trace.Count,
                NodeId = nodeId,
                StepType = ExecutionStepType.NodeCompleted,
                StateBefore = lastState,
                StateAfter = lastState,
                Values = lastState,
                CpuCycles = _getCpu(),
                RamSlots = _getRam(),
                PeakRamSlots = _getPeakRam()
            });

            if (!_suppressLiveEvents)
            {
                _onNodeCompleted(nodeId);
            }
        }

        DataStream? final = null;
        string producingNodeId = output.NodeId;
        if (output is OutputSinkNode sink && sink.LastReceived is not null)
        {
            final = sink.LastReceived;
            PipelineEdge? inbound = graph.Incoming(output.NodeId).FirstOrDefault();
            if (inbound is not null)
            {
                producingNodeId = inbound.FromNodeId;
            }
        }

        if (final is null)
        {
            var failure = new ExecutionFailure
            {
                NodeId = output.NodeId,
                FailureType = ExecutionFailureType.NoOutputProduced,
                Reason = "Pipeline finished without delivering an IntegerStream to Output.",
                CpuCycles = _getCpu(),
                PeakRamSlots = _getPeakRam()
            };
            return Fail(trace, failure);
        }

        return new ExecutionOutcome
        {
            Succeeded = true,
            Trace = trace,
            FinalOutput = final,
            ProducingNodeId = producingNodeId
        };
    }

    private static List<string>? TryTopologicalOrder(PipelineGraph graph, string startId)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(startId);
        reachable.Add(startId);
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

        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in reachable)
        {
            indegree[id] = 0;
        }

        foreach (string id in reachable)
        {
            foreach (PipelineEdge edge in graph.Outgoing(id))
            {
                if (!reachable.Contains(edge.ToNodeId))
                {
                    continue;
                }

                indegree[edge.ToNodeId] = indegree[edge.ToNodeId] + 1;
            }
        }

        var ready = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<string>();
        while (ready.Count > 0)
        {
            string nodeId = ready.Dequeue();
            order.Add(nodeId);
            foreach (PipelineEdge edge in graph.Outgoing(nodeId))
            {
                if (!indegree.ContainsKey(edge.ToNodeId))
                {
                    continue;
                }

                indegree[edge.ToNodeId]--;
                if (indegree[edge.ToNodeId] == 0)
                {
                    ready.Enqueue(edge.ToNodeId);
                }
            }
        }

        return order.Count == reachable.Count ? order : null;
    }

    private ExecutionOutcome Fail(ExecutionTrace trace, ExecutionFailure failure)
    {
        RecordBlocked(trace, failure);
        if (!_suppressLiveEvents)
        {
            _onBlocked(failure);
        }

        return new ExecutionOutcome
        {
            Succeeded = false,
            Trace = trace,
            Failure = failure,
            ProducingNodeId = failure.NodeId
        };
    }

    private void RecordBlocked(ExecutionTrace trace, ExecutionFailure failure)
    {
        int[] state = Array.Empty<int>();
        trace.Record(new ExecutionStep
        {
            Sequence = trace.Count,
            NodeId = failure.NodeId,
            StepType = ExecutionStepType.ExecutionBlocked,
            Failure = failure,
            RelatedPort = failure.RelatedPort?.Value,
            RelatedConnectionId = failure.RelatedConnectionId,
            StateBefore = state,
            StateAfter = state,
            CpuCycles = failure.CpuCycles,
            RamSlots = _getRam(),
            PeakRamSlots = failure.PeakRamSlots
        });
        trace.MarkFailure(failure);
    }
}
