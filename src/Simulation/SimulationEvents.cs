using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.Simulation;

/// <summary>
/// Centralized, Godot-agnostic event broker that relays step-by-step simulation
/// signals from the pure C# data model to any subscribed visual View layer.
///
/// The SimulationEngine raises these events; PipelineWorkspace (and other UI
/// listeners) subscribe to drive animations, metric meters, and end-of-run feedback.
/// All payloads use strict value types — no magic strings in the contract itself.
/// </summary>
public static class SimulationEvents
{
    public static event Action<int, int>? OnElementCompared;
    public static event Action<int, int>? OnElementsSwapped;
    public static event Action<int, string>? OnElementAllocated;
    public static event Action<float, float>? OnMetricsUpdated;
    public static event Action<bool, string>? OnSimulationEnded;

    /// <summary>Raised when a pipeline machine begins execution.</summary>
    public static event Action<string>? OnNodeExecutionStarted;

    /// <summary>Raised when a pipeline machine finishes successfully.</summary>
    public static event Action<string>? OnNodeExecutionCompleted;

    /// <summary>
    /// Raised when a machine emits a stream on an output port.
    /// Args: (NodeId, PortIdValue, DataTypeName, IntegerValues).
    /// </summary>
    public static event Action<string, string, string, IReadOnlyList<int>>? OnDataStreamProduced;

    /// <summary>
    /// Raised when a stream crosses a pipeline edge.
    /// Args: (FromNodeId, ToNodeId, EdgeId).
    /// </summary>
    public static event Action<string, string, string>? OnPipelineConnectionTraversed;

    /// <summary>Raised when execution stops at a specific machine/connection.</summary>
    public static event Action<ExecutionFailure>? OnExecutionBlocked;

    /// <summary>
    /// Structured step within a node (compare/swap/produce/etc.).
    /// Args: (NodeId, StepKind, Detail).
    /// </summary>
    public static event Action<string, string, string?>? OnNodeExecutionStep;

    /// <summary>
    /// Raised when the playback cursor applies or restores a recorded step.
    /// Args: (Step, MovingForward).
    /// </summary>
    public static event Action<Pipeline.ExecutionStep, bool>? OnTraceStepApplied;

    /// <summary>Raised when the trace cursor moves. Args: (CurrentIndex, TotalSteps).</summary>
    public static event Action<int, int>? OnTracePositionChanged;

    public static void RaiseElementCompared(int indexA, int indexB) =>
        OnElementCompared?.Invoke(indexA, indexB);

    public static void RaiseElementsSwapped(int fromIndex, int toIndex) =>
        OnElementsSwapped?.Invoke(fromIndex, toIndex);

    public static void RaiseElementAllocated(int ramSlotNumber, string valueLabel)
    {
        ArgumentNullException.ThrowIfNull(valueLabel);
        OnElementAllocated?.Invoke(ramSlotNumber, valueLabel);
    }

    public static void RaiseMetricsUpdated(float currentCpu, float currentRam) =>
        OnMetricsUpdated?.Invoke(currentCpu, currentRam);

    public static void RaiseSimulationEnded(bool isSuccess, string evaluationMessage)
    {
        ArgumentNullException.ThrowIfNull(evaluationMessage);
        OnSimulationEnded?.Invoke(isSuccess, evaluationMessage);
    }

    public static void RaiseNodeExecutionStarted(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        OnNodeExecutionStarted?.Invoke(nodeId);
    }

    public static void RaiseNodeExecutionCompleted(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        OnNodeExecutionCompleted?.Invoke(nodeId);
    }

    public static void RaiseDataStreamProduced(
        string nodeId,
        string portId,
        string dataTypeName,
        IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(portId);
        ArgumentNullException.ThrowIfNull(dataTypeName);
        ArgumentNullException.ThrowIfNull(values);
        OnDataStreamProduced?.Invoke(nodeId, portId, dataTypeName, values);
    }

    public static void RaisePipelineConnectionTraversed(string fromNodeId, string toNodeId, string edgeId)
    {
        ArgumentNullException.ThrowIfNull(fromNodeId);
        ArgumentNullException.ThrowIfNull(toNodeId);
        ArgumentNullException.ThrowIfNull(edgeId);
        OnPipelineConnectionTraversed?.Invoke(fromNodeId, toNodeId, edgeId);
    }

    public static void RaiseExecutionBlocked(ExecutionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        OnExecutionBlocked?.Invoke(failure);
    }

    public static void RaiseNodeExecutionStep(string nodeId, string stepKind, string? detail)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(stepKind);
        OnNodeExecutionStep?.Invoke(nodeId, stepKind, detail);
    }

    public static void RaiseTraceStepApplied(Pipeline.ExecutionStep step, bool movingForward)
    {
        ArgumentNullException.ThrowIfNull(step);
        OnTraceStepApplied?.Invoke(step, movingForward);
    }

    public static void RaiseTracePositionChanged(int currentIndex, int totalSteps) =>
        OnTracePositionChanged?.Invoke(currentIndex, totalSteps);

    public static void ClearAllSubscribers()
    {
        OnElementCompared = null;
        OnElementsSwapped = null;
        OnElementAllocated = null;
        OnMetricsUpdated = null;
        OnSimulationEnded = null;
        OnNodeExecutionStarted = null;
        OnNodeExecutionCompleted = null;
        OnDataStreamProduced = null;
        OnPipelineConnectionTraversed = null;
        OnExecutionBlocked = null;
        OnNodeExecutionStep = null;
        OnTraceStepApplied = null;
        OnTracePositionChanged = null;
    }
}
