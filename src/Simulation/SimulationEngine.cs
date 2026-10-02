using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.Simulation;

public enum SimulationResultState
{
    Success,
    FailedCpuOverheat,
    FailedRamOverflow,
    FailedOutputMismatch,
    FailedInvalidPipeline,
    FailedExecutionBlocked,
    Cancelled
}

public enum SimulationPlayMode
{
    Continuous,
    Step
}

public sealed class SimulationResult
{
    public required SimulationResultState State { get; init; }
    public required bool IsSuccess { get; init; }
    public required string Message { get; init; }
    public required IReadOnlyList<int> FinalOutput { get; init; }
    public required int CpuCyclesConsumed { get; init; }
    public required int PeakRamSlotsUsed { get; init; }
    public ExecutionTrace? Trace { get; init; }
    public ExecutionFailure? Failure { get; init; }
    public string? ProducingNodeId { get; init; }
}

public sealed class PipelineRouteBinding
{
    public required string SourceNodeId { get; init; }
    public required int SourcePort { get; init; }
    public required string TargetNodeId { get; init; }
    public required int TargetPort { get; init; }
    public required string TargetMachineId { get; init; }
    public int? SourceDataSlotIndex { get; init; }
}

/// <summary>
/// Conducts graph recording into a deterministic <see cref="ExecutionTrace"/>, then plays it back.
/// Presentation speed never changes CPU/RAM or algorithm results.
/// </summary>
public sealed class SimulationEngine
{
    private readonly LevelData _level;
    private readonly List<PipelineRouteBinding> _pipelineRoutes = new();
    private readonly object _controlLock = new();

    private int[] _workingBuffer = Array.Empty<int>();
    private bool _paused;
    private bool _isPlaying;
    private SimulationPlayMode _playMode = SimulationPlayMode.Continuous;
    private float _playbackSpeed = 1f;
    private TaskCompletionSource<bool>? _stepGate;
    private CancellationTokenSource? _runCts;
    private TracePlaybackController? _playback;
    private SimulationResult? _recordedResult;
    private string? _producingNodeId;
    private bool _manualStepConsumed;

    public int CurrentCPUCycles { get; private set; }
    public int CurrentRAMSlots { get; private set; }
    public int PeakRAMSlots { get; private set; }
    public IReadOnlyList<int> WorkingBuffer => _workingBuffer;
    public IReadOnlyList<PipelineRouteBinding> PipelineRoutes => _pipelineRoutes;
    public ExecutionTrace? LastTrace => _playback?.Trace ?? _recordedResult?.Trace;
    public TracePlaybackController? Playback => _playback;
    public SimulationPlayMode PlayMode => _playMode;
    public bool IsPaused => _paused;
    public bool IsPlaying => _isPlaying;
    public float PlaybackSpeed => _playbackSpeed;
    public int CurrentStepIndex => _playback?.CurrentIndex ?? -1;
    public int TotalSteps => _playback?.TotalSteps ?? 0;
    public bool CanStepForward => _playback?.CanStepForward == true;
    public bool CanStepBackward => _playback?.CanStepBackward == true;
    public string PositionLabel => _playback?.PositionLabel ?? "Step 0 / 0";

    public SimulationEngine(LevelData level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _level = level;
        ResetMetrics();
    }

    public void SetPipelineConnections(IEnumerable<PipelineRouteBinding> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _pipelineRoutes.Clear();
        _pipelineRoutes.AddRange(routes);
    }

    public bool HasRouteToMachine(string machineIdFragment)
    {
        if (string.IsNullOrWhiteSpace(machineIdFragment))
        {
            return false;
        }

        return _pipelineRoutes.Any(route =>
            (!string.IsNullOrWhiteSpace(route.TargetMachineId)
             && route.TargetMachineId.Contains(machineIdFragment, StringComparison.OrdinalIgnoreCase))
            || route.TargetNodeId.Contains(machineIdFragment, StringComparison.OrdinalIgnoreCase));
    }

    public void ResetMetrics()
    {
        CurrentCPUCycles = 0;
        CurrentRAMSlots = 0;
        PeakRAMSlots = 0;
    }

    public void SetPlaybackSpeed(float speed)
    {
        if (speed is 0.5f or 1f or 2f)
        {
            _playbackSpeed = speed;
        }
        else if (speed > 0f)
        {
            _playbackSpeed = speed;
        }
    }

    public void SetPlayMode(SimulationPlayMode mode)
    {
        lock (_controlLock)
        {
            _playMode = mode;
        }
    }

    public void Pause()
    {
        lock (_controlLock)
        {
            _paused = true;
            _playMode = SimulationPlayMode.Step;
        }
    }

    public void Resume()
    {
        lock (_controlLock)
        {
            _paused = false;
            _playMode = SimulationPlayMode.Continuous;
        }

        ReleaseStepGate();
    }

    public bool StepForward()
    {
        lock (_controlLock)
        {
            _paused = true;
            _playMode = SimulationPlayMode.Step;
        }

        ExecutionStep? step = ApplyStepForwardInternal();
        if (step is not null)
        {
            _manualStepConsumed = true;
        }

        ReleaseStepGate();
        return step is not null;
    }

    public bool StepBackward()
    {
        lock (_controlLock)
        {
            _paused = true;
            _playMode = SimulationPlayMode.Step;
        }

        return ApplyStepBackwardInternal();
    }

    /// <summary>Alias used by older step-mode API.</summary>
    public void Step() => StepForward();

    public void Reset()
    {
        _runCts?.Cancel();
        ReleaseStepGate();
        ResetMetrics();
        _workingBuffer = Array.Empty<int>();
        _playback = null;
        _recordedResult = null;
        _producingNodeId = null;
        _isPlaying = false;
        lock (_controlLock)
        {
            _paused = false;
            _playMode = SimulationPlayMode.Continuous;
        }

        SimulationEvents.RaiseTracePositionChanged(-1, 0);
    }

    /// <summary>
    /// Snaps working buffers/metrics to the recorded final result without emitting per-step events.
    /// Used when the player skips educational playback.
    /// </summary>
    public void SnapToFinalResultState()
    {
        if (_recordedResult is null)
        {
            return;
        }

        if (_playback is not null && _playback.TotalSteps > 0)
        {
            _playback.Seek(_playback.TotalSteps - 1);
        }

        _workingBuffer = _recordedResult.FinalOutput.ToArray();
        CurrentCPUCycles = _recordedResult.CpuCyclesConsumed;
        PeakRAMSlots = _recordedResult.PeakRamSlotsUsed;
        CurrentRAMSlots = _playback?.CurrentStep?.RamSlots ?? 0;
        PublishMetrics();
        SimulationEvents.RaiseTracePositionChanged(
            _playback?.CurrentIndex ?? -1,
            _playback?.TotalSteps ?? 0);
    }

    public void ResetPlaybackToStart()
    {
        if (_playback is null)
        {
            return;
        }

        _playback.ResetToStart();
        _workingBuffer = Array.Empty<int>();
        CurrentCPUCycles = 0;
        CurrentRAMSlots = 0;
        PeakRAMSlots = 0;
        PublishMetrics();
        SimulationEvents.RaiseTracePositionChanged(-1, _playback.TotalSteps);
    }

    /// <summary>True when a recorded educational trace is available for SHOW ME.</summary>
    public bool HasWatchableTrace => ExecutionTrace.IsWatchable(LastTrace);

    public ExecutionExplanationContext? GetExplanationContext() =>
        _playback?.TryBuildExplanationContext();

    public Task<SimulationResult> RunPipelineAsync(
        TimeSpan? stepDelay = null,
        CancellationToken cancellationToken = default)
    {
        PipelineGraph graph = PipelineGraphBuilder.Build(_level, _pipelineRoutes);
        return ExecuteGraphAsync(graph, stepDelay, cancellationToken);
    }

    public async Task<SimulationResult> ExecuteGraphAsync(
        PipelineGraph graph,
        TimeSpan? stepDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        SimulationResult recorded = await RecordGraphAsync(graph, cancellationToken).ConfigureAwait(false);
        _recordedResult = recorded;
        _playback = recorded.Trace is null
            ? null
            : new TracePlaybackController(_level, recorded.Trace);

        // Headless / zero-delay callers apply the full trace immediately (tests).
        // Presentation layer uses PlayRecordedTraceAsync for educational pacing.
        bool instant = stepDelay is null || stepDelay <= TimeSpan.Zero;
        if (instant && _playback is not null)
        {
            ApplyEntireTraceImmediately();
        }

        return recorded;
    }

    /// <summary>
    /// Plays the already-recorded trace with presentation delays. Safe to pause/step.
    /// </summary>
    public async Task PlayRecordedTraceAsync(
        CancellationToken cancellationToken = default,
        bool resetToStart = true)
    {
        if (_playback is null)
        {
            return;
        }

        _runCts?.Cancel();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _runCts.Token;

        if (resetToStart)
        {
            ResetPlaybackToStart();
        }

        lock (_controlLock)
        {
            _paused = false;
            _playMode = SimulationPlayMode.Continuous;
            _isPlaying = true;
        }

        try
        {
            while (CanStepForward)
            {
                token.ThrowIfCancellationRequested();
                await WaitForPlaybackGateAsync(token).ConfigureAwait(false);

                ExecutionStep? step;
                if (_manualStepConsumed)
                {
                    _manualStepConsumed = false;
                    step = _playback.CurrentStep;
                }
                else
                {
                    step = ApplyStepForwardInternal();
                }

                if (step is null)
                {
                    break;
                }

                TimeSpan delay = PlaybackTimingConfig.DurationFor(step.StepType, _playbackSpeed);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
            }

            if (_playback.IsAtEnd)
            {
                RaiseTerminalEventFromResult();
            }
        }
        catch (OperationCanceledException)
        {
            // Paused cancel / reset — leave state as-is.
        }
        finally
        {
            lock (_controlLock)
            {
                _isPlaying = false;
            }
        }
    }

    public async Task ReplayAsync(CancellationToken cancellationToken = default)
    {
        if (_playback is null)
        {
            return;
        }

        await PlayRecordedTraceAsync(cancellationToken, resetToStart: true).ConfigureAwait(false);
    }

    public SimulationResult RunPipeline()
    {
        return RunPipelineAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
    }

    public SimulationResult RunLinearBubbleSortPipeline()
    {
        PipelineGraph graph = PipelineGraphBuilder.BuildLinearBubbleSort(_level);
        return ExecuteGraphAsync(graph, TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task<SimulationResult> RecordGraphAsync(
        PipelineGraph graph,
        CancellationToken cancellationToken)
    {
        ResetMetrics();
        _workingBuffer = Array.Empty<int>();
        _producingNodeId = null;

        var executor = new PipelineGraphExecutor(
            tryConsumeCpu: TryConsumeCpuQuiet,
            tryAllocateRam: TryAllocateRamQuiet,
            releaseRam: () => { CurrentRAMSlots = 0; },
            getCpu: () => CurrentCPUCycles,
            getRam: () => CurrentRAMSlots,
            getPeakRam: () => PeakRAMSlots,
            onNodeStarted: _ => { },
            onNodeCompleted: _ => { },
            onStreamProduced: (_, stream) => { _workingBuffer = stream.IntegerValues.ToArray(); },
            onConnectionTraversed: (_, _) => { },
            onBlocked: _ => { },
            raiseCompared: (_, _) => { },
            raiseSwapped: (_, _) => { },
            suppressLiveEvents: true);

        PipelineGraphExecutor.ExecutionOutcome outcome = await executor
            .ExecuteAsync(graph, cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            ExecutionFailure failure = outcome.Failure!;
            SimulationResultState state = MapFailureState(failure.FailureType);
            var result = new SimulationResult
            {
                State = state,
                IsSuccess = false,
                Message = failure.Reason,
                FinalOutput = _workingBuffer.ToArray(),
                CpuCyclesConsumed = CurrentCPUCycles,
                PeakRamSlotsUsed = PeakRAMSlots,
                Trace = outcome.Trace,
                Failure = failure,
                ProducingNodeId = failure.NodeId
            };
            return result;
        }

        IReadOnlyList<int> finalValues = outcome.FinalOutput!.IntegerValues;
        _workingBuffer = finalValues.ToArray();
        _producingNodeId = outcome.ProducingNodeId;

        if (!OutputsMatch(finalValues, _level.ExpectedOutput))
        {
            var mismatch = new ExecutionFailure
            {
                NodeId = PipelineNodeIds.OutputSink,
                FailureType = ExecutionFailureType.EvaluationMismatch,
                Reason =
                    $"Output mismatch. Got [{string.Join(", ", finalValues)}] " +
                    $"expected [{string.Join(", ", _level.ExpectedOutput)}].",
                CpuCycles = CurrentCPUCycles,
                PeakRamSlots = PeakRAMSlots
            };
            outcome.Trace.Record(new ExecutionStep
            {
                Sequence = outcome.Trace.Count,
                NodeId = PipelineNodeIds.OutputSink,
                StepType = ExecutionStepType.ValidationFailure,
                Failure = mismatch,
                StateBefore = finalValues.ToArray(),
                StateAfter = finalValues.ToArray(),
                Values = finalValues.ToArray(),
                CpuCycles = CurrentCPUCycles,
                RamSlots = CurrentRAMSlots,
                PeakRamSlots = PeakRAMSlots
            });
            outcome.Trace.MarkFailure(mismatch);
            return new SimulationResult
            {
                State = SimulationResultState.FailedOutputMismatch,
                IsSuccess = false,
                Message = mismatch.Reason,
                FinalOutput = finalValues.ToArray(),
                CpuCyclesConsumed = CurrentCPUCycles,
                PeakRamSlotsUsed = PeakRAMSlots,
                Trace = outcome.Trace,
                Failure = mismatch,
                ProducingNodeId = outcome.ProducingNodeId
            };
        }

        outcome.Trace.Record(new ExecutionStep
        {
            Sequence = outcome.Trace.Count,
            NodeId = "evaluation",
            StepType = ExecutionStepType.ValidationSuccess,
            StateBefore = finalValues.ToArray(),
            StateAfter = finalValues.ToArray(),
            Values = finalValues.ToArray(),
            CpuCycles = CurrentCPUCycles,
            RamSlots = CurrentRAMSlots,
            PeakRamSlots = PeakRAMSlots
        });
        outcome.Trace.MarkSuccess();

        return new SimulationResult
        {
            State = SimulationResultState.Success,
            IsSuccess = true,
            Message =
                $"Pipeline succeeded in {CurrentCPUCycles} CPU cycles " +
                $"(peak RAM={PeakRAMSlots}). Output produced by '{outcome.ProducingNodeId}'.",
            FinalOutput = finalValues.ToArray(),
            CpuCyclesConsumed = CurrentCPUCycles,
            PeakRamSlotsUsed = PeakRAMSlots,
            Trace = outcome.Trace,
            ProducingNodeId = outcome.ProducingNodeId
        };
    }

    private void ApplyEntireTraceImmediately()
    {
        if (_playback is null)
        {
            return;
        }

        ResetPlaybackToStart();
        while (CanStepForward)
        {
            ApplyStepForwardInternal();
        }

        RaiseTerminalEventFromResult();
    }

    private ExecutionStep? ApplyStepForwardInternal()
    {
        if (_playback is null)
        {
            return null;
        }

        ExecutionStep? step = _playback.StepForward();
        if (step is null)
        {
            return null;
        }

        ApplyStepVisualState(step, movingForward: true);
        SimulationEvents.RaiseTraceStepApplied(step, movingForward: true);
        SimulationEvents.RaiseTracePositionChanged(_playback.CurrentIndex, _playback.TotalSteps);
        return step;
    }

    private bool ApplyStepBackwardInternal()
    {
        if (_playback is null || !_playback.CanStepBackward)
        {
            return false;
        }

        ExecutionStep? leaving = _playback.StepBackward();
        if (leaving is null)
        {
            return false;
        }

        if (_playback.CurrentIndex < 0)
        {
            _workingBuffer = Array.Empty<int>();
            CurrentCPUCycles = 0;
            CurrentRAMSlots = 0;
            PeakRAMSlots = 0;
            PublishMetrics();
            SimulationEvents.RaiseTraceStepApplied(leaving, movingForward: false);
            SimulationEvents.RaiseTracePositionChanged(-1, _playback.TotalSteps);
            return true;
        }

        ExecutionStep restored = _playback.CurrentStep!;
        ApplyStepVisualState(restored, movingForward: false);
        SimulationEvents.RaiseTraceStepApplied(leaving, movingForward: false);
        SimulationEvents.RaiseTracePositionChanged(_playback.CurrentIndex, _playback.TotalSteps);
        return true;
    }

    private void ApplyStepVisualState(ExecutionStep step, bool movingForward)
    {
        IReadOnlyList<int> state = movingForward ? step.StateAfter : step.StateBefore;
        // When stepping back onto a prior step, CurrentStep's StateAfter is the restored world.
        if (!movingForward && _playback?.CurrentStep is { } current)
        {
            state = current.StateAfter;
            CurrentCPUCycles = current.CpuCycles;
            CurrentRAMSlots = current.RamSlots;
            PeakRAMSlots = Math.Max(PeakRAMSlots, current.PeakRamSlots);
        }
        else
        {
            CurrentCPUCycles = step.CpuCycles;
            CurrentRAMSlots = step.RamSlots;
            PeakRAMSlots = Math.Max(PeakRAMSlots, step.PeakRamSlots);
        }

        _workingBuffer = state.ToArray();
        PublishMetrics();

        switch (step.StepType)
        {
            case ExecutionStepType.NodeStarted:
                SimulationEvents.RaiseNodeExecutionStarted(step.NodeId);
                break;
            case ExecutionStepType.NodeCompleted:
                SimulationEvents.RaiseNodeExecutionCompleted(step.NodeId);
                break;
            case ExecutionStepType.Compare when step.Indexes.Count >= 2:
                SimulationEvents.RaiseElementCompared(step.Indexes[0], step.Indexes[1]);
                break;
            case ExecutionStepType.Swap when step.Indexes.Count >= 2:
                SimulationEvents.RaiseElementsSwapped(step.Indexes[0], step.Indexes[1]);
                break;
            case ExecutionStepType.DataStreamProduced:
                SimulationEvents.RaiseDataStreamProduced(
                    step.NodeId,
                    step.RelatedPort ?? PortId.Out.Value,
                    PortDataType.IntegerStream.ToString(),
                    step.StateAfter);
                break;
            case ExecutionStepType.ConnectionTraversed:
                SimulationEvents.RaisePipelineConnectionTraversed(
                    step.FromNodeId ?? step.NodeId,
                    step.ToNodeId ?? step.NodeId,
                    step.RelatedConnectionId ?? string.Empty);
                break;
            case ExecutionStepType.ExecutionBlocked:
            case ExecutionStepType.ValidationFailure:
                if (step.Failure is not null)
                {
                    SimulationEvents.RaiseExecutionBlocked(step.Failure);
                }

                break;
        }

        SimulationEvents.RaiseNodeExecutionStep(step.NodeId, step.StepKindLabel, step.DetailSummary);
    }

    private void RaiseTerminalEventFromResult()
    {
        if (_recordedResult is null)
        {
            return;
        }

        SimulationEvents.RaiseSimulationEnded(_recordedResult.IsSuccess, _recordedResult.Message);
    }

    private async Task WaitForPlaybackGateAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimulationPlayMode mode;
            bool paused;
            lock (_controlLock)
            {
                mode = _playMode;
                paused = _paused;
            }

            if (!paused && mode == SimulationPlayMode.Continuous)
            {
                return;
            }

            TaskCompletionSource<bool> gate;
            lock (_controlLock)
            {
                _stepGate ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                gate = _stepGate;
            }

            await using (cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken)))
            {
                await gate.Task.ConfigureAwait(false);
            }

            lock (_controlLock)
            {
                _stepGate = null;
                if (_playMode == SimulationPlayMode.Step)
                {
                    // Manual StepForward already applied the step; auto-loop should wait again.
                    return;
                }
            }
        }
    }

    private void ReleaseStepGate()
    {
        TaskCompletionSource<bool>? gate;
        lock (_controlLock)
        {
            gate = _stepGate;
        }

        gate?.TrySetResult(true);
    }

    private bool TryConsumeCpuQuiet(int cycles)
    {
        CurrentCPUCycles += cycles;
        return CurrentCPUCycles <= _level.Budgets.MaxCPUCycles;
    }

    private bool TryAllocateRamQuiet(int slotNumber, string label)
    {
        _ = slotNumber;
        _ = label;
        CurrentRAMSlots++;
        if (CurrentRAMSlots > PeakRAMSlots)
        {
            PeakRAMSlots = CurrentRAMSlots;
        }

        return CurrentRAMSlots <= _level.Budgets.MaxRAMSlots;
    }

    private void PublishMetrics()
    {
        SimulationEvents.RaiseMetricsUpdated(CurrentCPUCycles, CurrentRAMSlots);
    }

    private static SimulationResultState MapFailureState(ExecutionFailureType type) => type switch
    {
        ExecutionFailureType.CpuBudgetExceeded => SimulationResultState.FailedCpuOverheat,
        ExecutionFailureType.RamBudgetExceeded => SimulationResultState.FailedRamOverflow,
        ExecutionFailureType.InvalidGraph
            or ExecutionFailureType.IncompatibleType
            or ExecutionFailureType.UnreachableNode
            or ExecutionFailureType.MissingInput
            or ExecutionFailureType.NoOutputProduced => SimulationResultState.FailedInvalidPipeline,
        _ => SimulationResultState.FailedExecutionBlocked
    };

    private static bool OutputsMatch(IReadOnlyList<int> actual, IReadOnlyList<int> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (int i = 0; i < actual.Count; i++)
        {
            if (actual[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }
}
