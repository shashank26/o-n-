using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Simulation;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.UI;

/// <summary>
/// Player/editor lifecycle for the pipeline workspace.
/// Distinguishes simulation execution from optional educational playback.
/// </summary>
public enum PipelineState
{
    Editing,
    Ready,
    /// <summary>Deterministic simulation is recording the execution trace.</summary>
    Running,
    /// <summary>Simulation finished; player may choose to watch or skip the replay.</summary>
    AwaitingReplayChoice,
    /// <summary>Educational playback of the recorded trace is in progress.</summary>
    PlayingTrace,
    Completed,
    Success,
    Failed
}

/// <summary>
/// Pure C# lifecycle controller used by <c>PipelineWorkspace</c>.
/// No Godot dependencies — unit-testable structural readiness and transitions.
/// </summary>
public sealed class PipelineLifecycle
{
    public PipelineState Current { get; private set; } = PipelineState.Editing;

    /// <summary>True while the executing graph must not be mutated.</summary>
    public bool IsGraphLocked =>
        Current is PipelineState.Running
            or PipelineState.Completed
            or PipelineState.AwaitingReplayChoice
            or PipelineState.PlayingTrace;

    /// <summary>True while Execute must not start another simulation.</summary>
    public bool BlocksNewExecution =>
        Current is PipelineState.Running
            or PipelineState.AwaitingReplayChoice
            or PipelineState.PlayingTrace
            or PipelineState.Success;

    public event Action<PipelineState, PipelineState>? StateChanged;

    public bool SetState(PipelineState newState)
    {
        if (Current == newState)
        {
            return false;
        }

        PipelineState previous = Current;
        Current = newState;
        StateChanged?.Invoke(previous, newState);
        return true;
    }

    public void ReevaluateAfterGraphChange(bool structurallyReady)
    {
        if (IsGraphLocked || Current == PipelineState.Success)
        {
            return;
        }

        SetState(structurallyReady ? PipelineState.Ready : PipelineState.Editing);
    }

    public static bool IsPipelineReady(IEnumerable<PipelineRouteBinding>? routes)
    {
        if (routes is null)
        {
            return false;
        }

        List<PipelineRouteBinding> list = routes as List<PipelineRouteBinding> ?? routes.ToList();
        if (list.Count == 0)
        {
            return false;
        }

        bool inputToBubble = list.Any(route =>
            IsInputSource(route.SourceNodeId)
            && IsBubbleSortTarget(route));

        bool bubbleToOutput = list.Any(route =>
            IsBubbleSortSource(route)
            && PipelineNodeIds.IsOutputSink(route.TargetNodeId));

        return inputToBubble && bubbleToOutput;
    }

    /// <summary>
    /// True when the recorded trace contains educational execution content worth watching.
    /// </summary>
    public static bool HasWatchableTrace(ExecutionTrace? trace) =>
        ExecutionTrace.IsWatchable(trace);

    private static bool IsInputSource(string nodeId) =>
        PipelineNodeIds.IsInputStream(nodeId)
        || nodeId.StartsWith("DataSlot_", StringComparison.Ordinal);

    private static bool IsBubbleSortTarget(PipelineRouteBinding route) =>
        PipelineNodeIds.IsBubbleSortEngine(route.TargetNodeId)
        || (!string.IsNullOrWhiteSpace(route.TargetMachineId)
            && route.TargetMachineId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase));

    private static bool IsBubbleSortSource(PipelineRouteBinding route) =>
        PipelineNodeIds.IsBubbleSortEngine(route.SourceNodeId)
        || route.SourceNodeId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase);

    /// <summary>Simulation finished — wait for SHOW ME / SKIP when a watchable trace exists.</summary>
    public void EnterAwaitingReplayChoice()
    {
        SetState(PipelineState.AwaitingReplayChoice);
    }

    public void BeginTracePlayback()
    {
        SetState(PipelineState.PlayingTrace);
    }

    /// <summary>After skip or finished playback, settle on Success or Failed.</summary>
    public void ApplySimulationOutcome(bool isSuccess)
    {
        SetState(PipelineState.Completed);
        SetState(isSuccess ? PipelineState.Success : PipelineState.Failed);
    }

    /// <summary>Skip educational playback and settle on the recorded outcome.</summary>
    public void SkipReplay(bool isSuccess) => ApplySimulationOutcome(isSuccess);

    /// <summary>Player chose SHOW ME — enter educational playback of the recorded trace.</summary>
    public void ChooseShowMe() => BeginTracePlayback();

    public bool RejectGraphMutation() => IsGraphLocked;
}
