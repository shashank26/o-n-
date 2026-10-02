using System;
using System.Collections.Generic;

namespace PipelineArchitect.Simulation.Pipeline;

public enum PipelineNodeKind
{
    InputStream,
    BubbleSort,
    OutputSink,
    Unsupported
    // Future: MergeSort, Queue, Bfs, Dijkstra, etc.
}

public enum ExecutionFailureType
{
    MissingInput,
    IncompatibleType,
    NoOutputProduced,
    UnreachableNode,
    CpuBudgetExceeded,
    RamBudgetExceeded,
    EvaluationMismatch,
    InvalidGraph,
    Cancelled
}

/// <summary>Structured execution stop — UI formats presentation; engine stays string-light.</summary>
public sealed class ExecutionFailure
{
    public required string NodeId { get; init; }
    public required ExecutionFailureType FailureType { get; init; }
    public required string Reason { get; init; }
    public PortId? RelatedPort { get; init; }
    public string? RelatedConnectionId { get; init; }
    public int CpuCycles { get; set; }
    public int PeakRamSlots { get; set; }
}

/// <summary>Shared runtime context passed into each machine during a pipeline run.</summary>
public sealed class NodeExecutionContext
{
    public required string NodeId { get; init; }
    public required IReadOnlyDictionary<PortId, DataStream> Inputs { get; init; }
    public required ExecutionTrace Trace { get; init; }
    public required Func<int, bool> TryConsumeCpu { get; init; }
    public required Func<int, string, bool> TryAllocateRam { get; init; }
    public required Action ReleaseRam { get; init; }
    public required Func<int> GetCpuCycles { get; init; }
    public required Func<int> GetRamSlots { get; init; }
    public required Func<int> GetPeakRamSlots { get; init; }
    public required Action<int, int> RaiseCompared { get; init; }
    public required Action<int, int> RaiseSwapped { get; init; }
    public required Func<TimeSpan, System.Threading.CancellationToken, System.Threading.Tasks.Task> YieldAsync { get; init; }
    public TimeSpan StepDelay { get; init; }
    public System.Threading.CancellationToken CancellationToken { get; init; }
    public bool SuppressLiveEvents { get; init; }
}

public sealed class NodeExecutionResult
{
    public bool Succeeded { get; init; }
    public IReadOnlyDictionary<PortId, DataStream> Outputs { get; init; }
        = new Dictionary<PortId, DataStream>();
    public ExecutionFailure? Failure { get; init; }

    public static NodeExecutionResult Ok(IReadOnlyDictionary<PortId, DataStream> outputs) =>
        new() { Succeeded = true, Outputs = outputs };

    public static NodeExecutionResult Fail(ExecutionFailure failure) =>
        new() { Succeeded = false, Failure = failure };
}

/// <summary>Generic executable pipeline machine contract.</summary>
public interface IPipelineNode
{
    string NodeId { get; }
    PipelineNodeKind Kind { get; }
    IReadOnlyList<PortDefinition> Ports { get; }

    System.Threading.Tasks.Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context);
}
