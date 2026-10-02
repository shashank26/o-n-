using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation;
using PipelineArchitect.Simulation.Machines;
using PipelineArchitect.Simulation.Pipeline;
using PipelineArchitect.UI;
using Xunit;

namespace PipelineArchitect.Tests;

/// <summary>
/// Verifies simulation completes without auto-playback, and SHOW ME / SKIP reuse the recorded trace.
/// </summary>
public sealed class ReplayChoiceTests : IDisposable
{
    public ReplayChoiceTests()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    public void Dispose()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    [Fact]
    public async Task SimulationCompletes_WithoutPlayback_ResultAndTraceAvailable()
    {
        LevelData level = CreateLevel(new[] { 5, 1, 4, 2, 8 }, new[] { 1, 2, 4, 5, 8 }, cpu: 30);
        var engine = new SimulationEngine(level);
        bool ended = false;
        SimulationEvents.OnSimulationEnded += (_, _) => ended = true;

        SimulationResult result = await RecordWithoutPlaybackAsync(engine, level);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Trace);
        Assert.True(result.Trace!.Count > 0);
        Assert.True(ExecutionTrace.IsWatchable(result.Trace));
        Assert.Equal(level.ExpectedOutput, result.FinalOutput);
        Assert.False(engine.IsPlaying);
        Assert.False(ended);
        Assert.Same(result.Trace, engine.LastTrace);
    }

    [Fact]
    public async Task ShowMe_ReplaysExistingTrace_DoesNotRerunAlgorithm()
    {
        LevelData level = CreateLevel(new[] { 3, 1, 2 }, new[] { 1, 2, 3 }, cpu: 30);
        var engine = new SimulationEngine(level);

        SimulationResult recorded = await RecordWithoutPlaybackAsync(engine, level);

        Assert.True(recorded.IsSuccess, recorded.Message);
        ExecutionTrace originalTrace = recorded.Trace!;
        int recordedCpu = recorded.CpuCyclesConsumed;
        int recordedPeakRam = recorded.PeakRamSlotsUsed;
        int originalStepCount = originalTrace.Count;

        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);
        lifecycle.EnterAwaitingReplayChoice();
        Assert.Equal(PipelineState.AwaitingReplayChoice, lifecycle.Current);
        Assert.True(lifecycle.BlocksNewExecution);

        lifecycle.ChooseShowMe();
        Assert.Equal(PipelineState.PlayingTrace, lifecycle.Current);

        await engine.PlayRecordedTraceAsync(resetToStart: true);

        Assert.Same(originalTrace, engine.LastTrace);
        Assert.Equal(originalStepCount, engine.LastTrace!.Count);
        Assert.Equal(recordedCpu, engine.CurrentCPUCycles);
        Assert.Equal(recordedPeakRam, engine.PeakRAMSlots);
        Assert.Equal(recorded.FinalOutput, engine.WorkingBuffer);
        Assert.False(engine.IsPlaying);

        lifecycle.ApplySimulationOutcome(recorded.IsSuccess);
        Assert.Equal(PipelineState.Success, lifecycle.Current);
    }

    [Fact]
    public async Task Skip_SnapsToFinalState_WithoutPlayback()
    {
        LevelData level = CreateLevel(new[] { 2, 1 }, new[] { 1, 2 }, cpu: 30);
        var engine = new SimulationEngine(level);
        bool ended = false;
        SimulationEvents.OnSimulationEnded += (_, _) => ended = true;

        SimulationResult recorded = await RecordWithoutPlaybackAsync(engine, level);

        Assert.True(recorded.IsSuccess, recorded.Message);
        Assert.False(ended);

        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);
        lifecycle.EnterAwaitingReplayChoice();

        engine.SnapToFinalResultState();
        lifecycle.SkipReplay(recorded.IsSuccess);

        Assert.False(engine.IsPlaying);
        Assert.False(ended);
        Assert.Equal(recorded.CpuCyclesConsumed, engine.CurrentCPUCycles);
        Assert.Equal(recorded.FinalOutput, engine.WorkingBuffer);
        Assert.Equal(PipelineState.Success, lifecycle.Current);
        Assert.True(ExecutionTrace.IsWatchable(engine.LastTrace));
    }

    [Fact]
    public async Task FailureDuringExecution_HasWatchableTrace()
    {
        LevelData level = CreateLevel(new[] { 5, 1, 4, 2, 8 }, new[] { 1, 2, 4, 5, 8 }, cpu: 5);
        var engine = new SimulationEngine(level);

        SimulationResult result = await RecordWithoutPlaybackAsync(engine, level);

        Assert.False(result.IsSuccess);
        Assert.Equal(SimulationResultState.FailedCpuOverheat, result.State);
        Assert.NotNull(result.Trace);
        Assert.True(ExecutionTrace.IsWatchable(result.Trace));
        Assert.True(engine.HasWatchableTrace);

        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);
        lifecycle.EnterAwaitingReplayChoice();
        Assert.True(lifecycle.BlocksNewExecution);
        Assert.True(PipelineLifecycle.HasWatchableTrace(result.Trace));
    }

    [Fact]
    public async Task ValidationFailure_NotWatchable_NoReplayOffered()
    {
        LevelData level = CreateLevel(new[] { 5, 1, 4 }, new[] { 1, 4, 5 }, cpu: 30);
        var graph = new PipelineGraph();
        graph.AddNode(new InputStreamNode(PipelineNodeIds.InputStream, level.InputStream));
        graph.AddNode(new BubbleSortMachineNode("Engine_Bubble_Sort_Machine"));
        graph.AddNode(new OutputSinkNode(PipelineNodeIds.OutputSink));
        graph.AddEdge(new PipelineEdge
        {
            EdgeId = "e0",
            FromNodeId = PipelineNodeIds.InputStream,
            FromPortId = PortId.Out,
            ToNodeId = PipelineNodeIds.OutputSink,
            ToPortId = PortId.In
        });

        var engine = new SimulationEngine(level);
        SimulationResult result = await engine.ExecuteGraphAsync(graph, TimeSpan.FromMilliseconds(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(SimulationResultState.FailedInvalidPipeline, result.State);
        Assert.NotNull(result.Trace);
        Assert.False(ExecutionTrace.IsWatchable(result.Trace));
        Assert.False(engine.HasWatchableTrace);
        Assert.False(PipelineLifecycle.HasWatchableTrace(result.Trace));
        Assert.Contains(result.Trace!.Steps, s => s.StepType == ExecutionStepType.ExecutionBlocked);
    }

    [Fact]
    public async Task WatchAfterSkip_ReplaysSameTrace()
    {
        LevelData level = CreateLevel(new[] { 4, 2 }, new[] { 2, 4 }, cpu: 30);
        var engine = new SimulationEngine(level);

        SimulationResult recorded = await RecordWithoutPlaybackAsync(engine, level);
        Assert.True(recorded.IsSuccess, recorded.Message);

        ExecutionTrace original = recorded.Trace!;
        int cpu = recorded.CpuCyclesConsumed;

        engine.SnapToFinalResultState();
        Assert.Equal(cpu, engine.CurrentCPUCycles);

        await engine.PlayRecordedTraceAsync(resetToStart: true);

        Assert.Same(original, engine.LastTrace);
        Assert.Equal(cpu, engine.CurrentCPUCycles);
        Assert.Equal(recorded.FinalOutput, engine.WorkingBuffer);
    }

    [Fact]
    public async Task ShowMe_DoesNotIncrementMetricsBeyondRecorded()
    {
        LevelData level = CreateLevel(new[] { 5, 1, 4, 2, 8 }, new[] { 1, 2, 4, 5, 8 }, cpu: 30);
        var engine = new SimulationEngine(level);

        SimulationResult recorded = await RecordWithoutPlaybackAsync(engine, level);

        int recordedCpu = recorded.CpuCyclesConsumed;
        Assert.Equal(10, recordedCpu); // 5*(5-1)/2

        await engine.PlayRecordedTraceAsync(resetToStart: true);
        Assert.Equal(recordedCpu, engine.CurrentCPUCycles);

        await engine.ReplayAsync();
        Assert.Equal(recordedCpu, engine.CurrentCPUCycles);
        Assert.Equal(recorded.PeakRamSlotsUsed, engine.PeakRAMSlots);
    }

    [Fact]
    public async Task ShowMeOrSkip_DeterministicIdenticalResult()
    {
        LevelData level = CreateLevel(new[] { 5, 1, 4, 2, 8 }, new[] { 1, 2, 4, 5, 8 }, cpu: 30);

        var showEngine = new SimulationEngine(level);
        SimulationResult showResult = await RecordWithoutPlaybackAsync(showEngine, level);
        await showEngine.PlayRecordedTraceAsync(resetToStart: true);

        var skipEngine = new SimulationEngine(level);
        SimulationResult skipResult = await RecordWithoutPlaybackAsync(skipEngine, level);
        skipEngine.SnapToFinalResultState();

        Assert.Equal(showResult.IsSuccess, skipResult.IsSuccess);
        Assert.Equal(showResult.CpuCyclesConsumed, skipResult.CpuCyclesConsumed);
        Assert.Equal(showResult.PeakRamSlotsUsed, skipResult.PeakRamSlotsUsed);
        Assert.Equal(showResult.FinalOutput, skipResult.FinalOutput);
        Assert.Equal(showEngine.CurrentCPUCycles, skipEngine.CurrentCPUCycles);
        Assert.Equal(showEngine.WorkingBuffer, skipEngine.WorkingBuffer);
        Assert.Equal(showResult.Trace!.Count, skipResult.Trace!.Count);
    }

    [Fact]
    public void Lifecycle_BlocksExecuteWhileAwaitingOrPlaying()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);
        lifecycle.EnterAwaitingReplayChoice();
        Assert.True(lifecycle.BlocksNewExecution);
        Assert.True(lifecycle.IsGraphLocked);

        lifecycle.ChooseShowMe();
        Assert.True(lifecycle.BlocksNewExecution);
        Assert.True(lifecycle.IsGraphLocked);

        lifecycle.ApplySimulationOutcome(true);
        Assert.True(lifecycle.BlocksNewExecution);
        Assert.Equal(PipelineState.Success, lifecycle.Current);
    }

    /// <summary>
    /// Records a valid linear Bubble Sort graph with a non-zero delay so the engine
    /// does not auto-apply / auto-raise SimulationEnded (presentation stays optional).
    /// </summary>
    private static async Task<SimulationResult> RecordWithoutPlaybackAsync(
        SimulationEngine engine,
        LevelData level)
    {
        PipelineGraph graph = PipelineGraphBuilder.BuildLinearBubbleSort(level);
        SimulationResult result = await engine.ExecuteGraphAsync(graph, TimeSpan.FromMilliseconds(1));
        // Educational pacing is irrelevant for replay-choice unit tests.
        engine.SetPlaybackSpeed(10_000f);
        return result;
    }

    private static LevelData CreateLevel(int[] input, int[] expected, int cpu)
    {
        return new LevelData
        {
            LevelId = "replay_choice_test",
            Tier = LevelTier.Intern,
            Title = "Replay Choice",
            Description = "Test level",
            InputStream = input.ToList(),
            ExpectedOutput = expected.ToList(),
            Budgets = new LevelBudgets { MaxCPUCycles = cpu, MaxRAMSlots = 2 },
            AllowedToolbox = new List<string> { "Bubble_Sort_Machine" }
        };
    }
}
