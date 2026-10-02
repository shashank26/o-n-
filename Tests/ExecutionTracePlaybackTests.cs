using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation;
using PipelineArchitect.Simulation.Pipeline;
using Xunit;

namespace PipelineArchitect.Tests;

/// <summary>
/// Verifies deterministic execution traces, step forward/backward, replay, and speed independence.
/// </summary>
public sealed class ExecutionTracePlaybackTests : IDisposable
{
    public ExecutionTracePlaybackTests()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    public void Dispose()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    [Fact]
    public void DeterministicTrace_SameGraph_ProducesIdenticalSteps()
    {
        LevelData level = CreateLevel(new[] { 5, 1, 4, 2, 8 }, new[] { 1, 2, 4, 5, 8 }, cpu: 30);
        var engineA = new SimulationEngine(level);
        var engineB = new SimulationEngine(level);

        SimulationResult a = engineA.RunLinearBubbleSortPipeline();
        SimulationResult b = engineB.RunLinearBubbleSortPipeline();

        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        Assert.NotNull(a.Trace);
        Assert.NotNull(b.Trace);
        Assert.Equal(a.Trace!.Count, b.Trace!.Count);
        Assert.Equal(a.CpuCyclesConsumed, b.CpuCyclesConsumed);

        for (int i = 0; i < a.Trace.Count; i++)
        {
            ExecutionStep sa = a.Trace.Steps[i];
            ExecutionStep sb = b.Trace.Steps[i];
            Assert.Equal(sa.StepType, sb.StepType);
            Assert.Equal(sa.NodeId, sb.NodeId);
            Assert.Equal(sa.StateAfter, sb.StateAfter);
            Assert.Equal(sa.CpuCycles, sb.CpuCycles);
        }
    }

    [Fact]
    public void Trace_ContainsMeaningfulOperations()
    {
        LevelData level = CreateLevel(new[] { 3, 1, 2 }, new[] { 1, 2, 3 }, cpu: 30);
        var engine = new SimulationEngine(level);
        SimulationResult result = engine.RunLinearBubbleSortPipeline();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Trace);
        IReadOnlyList<ExecutionStepType> types = result.Trace!.Steps.Select(s => s.StepType).ToArray();
        Assert.Contains(ExecutionStepType.NodeStarted, types);
        Assert.Contains(ExecutionStepType.Compare, types);
        Assert.Contains(ExecutionStepType.Swap, types);
        Assert.Contains(ExecutionStepType.NodeCompleted, types);
        Assert.Contains(ExecutionStepType.DataStreamProduced, types);
        Assert.Contains(ExecutionStepType.OutputReceived, types);
        Assert.Contains(ExecutionStepType.ValidationSuccess, types);
    }

    [Fact]
    public void StepForward_AdvancesExactlyOneStep()
    {
        LevelData level = CreateLevel(new[] { 2, 1 }, new[] { 1, 2 }, cpu: 30);
        var engine = new SimulationEngine(level);
        engine.RunLinearBubbleSortPipeline();
        engine.ResetPlaybackToStart();

        Assert.Equal(-1, engine.CurrentStepIndex);
        Assert.True(engine.StepForward());
        Assert.Equal(0, engine.CurrentStepIndex);
        ExecutionStep first = engine.Playback!.CurrentStep!;
        Assert.True(engine.StepForward());
        Assert.Equal(1, engine.CurrentStepIndex);
        Assert.NotEqual(first.Sequence, engine.Playback.CurrentStep!.Sequence);
    }

    [Fact]
    public void StepBackward_RestoresPreviousState()
    {
        LevelData level = CreateLevel(new[] { 2, 1 }, new[] { 1, 2 }, cpu: 30);
        var engine = new SimulationEngine(level);
        engine.RunLinearBubbleSortPipeline();
        engine.ResetPlaybackToStart();

        engine.StepForward();
        int[] afterFirst = engine.WorkingBuffer.ToArray();
        int cpuAfterFirst = engine.CurrentCPUCycles;

        engine.StepForward();
        Assert.NotEqual(afterFirst, engine.WorkingBuffer);

        Assert.True(engine.StepBackward());
        Assert.Equal(afterFirst, engine.WorkingBuffer.ToArray());
        Assert.Equal(cpuAfterFirst, engine.CurrentCPUCycles);
    }

    [Fact]
    public void MultipleBackwardSteps_MatchEarlierForwardState()
    {
        LevelData level = CreateLevel(new[] { 3, 2, 1 }, new[] { 1, 2, 3 }, cpu: 30);
        var engine = new SimulationEngine(level);
        engine.RunLinearBubbleSortPipeline();
        engine.ResetPlaybackToStart();

        engine.StepForward();
        int[] afterOne = engine.WorkingBuffer.ToArray();
        int idxOne = engine.CurrentStepIndex;

        engine.StepForward();
        engine.StepForward();
        Assert.True(engine.StepBackward());
        Assert.True(engine.StepBackward());

        Assert.Equal(idxOne, engine.CurrentStepIndex);
        Assert.Equal(afterOne, engine.WorkingBuffer.ToArray());
    }

    [Fact]
    public void BeginningBoundary_CannotStepBackward()
    {
        LevelData level = CreateLevel(new[] { 1, 2 }, new[] { 1, 2 }, cpu: 30);
        var engine = new SimulationEngine(level);
        engine.RunLinearBubbleSortPipeline();
        engine.ResetPlaybackToStart();

        Assert.False(engine.CanStepBackward);
        Assert.False(engine.StepBackward());
        Assert.Equal(-1, engine.CurrentStepIndex);
    }

    [Fact]
    public void EndBoundary_CannotStepForward()
    {
        LevelData level = CreateLevel(new[] { 1, 2 }, new[] { 1, 2 }, cpu: 30);
        var engine = new SimulationEngine(level);
        engine.RunLinearBubbleSortPipeline();

        Assert.True(engine.Playback!.IsAtEnd);
        Assert.False(engine.CanStepForward);
        Assert.False(engine.StepForward());
    }

    [Fact]
    public void Replay_ReproducesSameFinalState()
    {
        LevelData level = CreateLevel(new[] { 4, 3, 2, 1 }, new[] { 1, 2, 3, 4 }, cpu: 40);
        var engine = new SimulationEngine(level);
        SimulationResult first = engine.RunLinearBubbleSortPipeline();
        int[] final = first.FinalOutput.ToArray();
        int cpu = first.CpuCyclesConsumed;

        engine.ResetPlaybackToStart();
        while (engine.CanStepForward)
        {
            engine.StepForward();
        }

        Assert.Equal(final, engine.WorkingBuffer.ToArray());
        Assert.Equal(cpu, engine.CurrentCPUCycles);
    }

    [Fact]
    public void PlaybackSpeed_DoesNotAffectSimulationMetrics()
    {
        LevelData level = CreateLevel(new[] { 5, 1, 4 }, new[] { 1, 4, 5 }, cpu: 30);

        var slow = new SimulationEngine(level);
        slow.SetPlaybackSpeed(0.5f);
        SimulationResult slowResult = slow.RunLinearBubbleSortPipeline();

        var fast = new SimulationEngine(level);
        fast.SetPlaybackSpeed(2f);
        SimulationResult fastResult = fast.RunLinearBubbleSortPipeline();

        Assert.Equal(slowResult.CpuCyclesConsumed, fastResult.CpuCyclesConsumed);
        Assert.Equal(slowResult.PeakRamSlotsUsed, fastResult.PeakRamSlotsUsed);
        Assert.Equal(slowResult.FinalOutput, fastResult.FinalOutput);
        Assert.Equal(slowResult.Trace!.Count, fastResult.Trace!.Count);
    }

    [Fact]
    public void FailureTrace_IdentifiesNodeAndReason()
    {
        LevelData level = CreateLevel(new[] { 3, 2, 1 }, new[] { 1, 2, 3 }, cpu: 1);
        var engine = new SimulationEngine(level);
        SimulationResult result = engine.RunLinearBubbleSortPipeline();

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Trace);
        Assert.NotNull(result.Failure);
        Assert.Equal(ExecutionFailureType.CpuBudgetExceeded, result.Failure!.FailureType);
        Assert.Contains("Bubble_Sort", result.Failure.NodeId, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Trace!.Steps, s => s.StepType == ExecutionStepType.ExecutionBlocked);
        Assert.False(result.Trace.IsSuccess);
        Assert.NotNull(result.Trace.TerminalFailure);
    }

    [Fact]
    public void ExplanationContext_ExposesStructuredStepData()
    {
        LevelData level = CreateLevel(new[] { 2, 1 }, new[] { 1, 2 }, cpu: 30);
        var engine = new SimulationEngine(level);
        engine.RunLinearBubbleSortPipeline();
        engine.ResetPlaybackToStart();

        ExecutionStep? compare = null;
        while (engine.CanStepForward)
        {
            engine.StepForward();
            if (engine.Playback?.CurrentStep?.StepType == ExecutionStepType.Compare)
            {
                compare = engine.Playback.CurrentStep;
                break;
            }
        }

        Assert.NotNull(compare);
        ExecutionExplanationContext? ctx = engine.GetExplanationContext();
        Assert.NotNull(ctx);
        Assert.Equal(level.LevelId, ctx!.LevelId);
        Assert.Equal(ExecutionStepType.Compare, ctx.Step.StepType);
        Assert.True(ctx.Step.Indexes.Count >= 2);
        Assert.True(ctx.Step.Values.Count >= 2);
        Assert.NotEmpty(ctx.Step.StateBefore);
        Assert.NotEmpty(ctx.Step.StateAfter);
        Assert.Equal(ctx.StepIndex, engine.CurrentStepIndex);
        Assert.True(ctx.TotalSteps > 0);
        Assert.True(ctx.MaxCpuCycles > 0);
    }

    private static LevelData CreateLevel(int[] input, int[] expected, int cpu)
    {
        return new LevelData
        {
            LevelId = "trace_fixture",
            Tier = LevelTier.Intern,
            Title = "Trace Fixture",
            Description = "Deterministic playback fixture.",
            InputStream = input.ToList(),
            ExpectedOutput = expected.ToList(),
            Budgets = new LevelBudgets { MaxCPUCycles = cpu, MaxRAMSlots = 2 },
            AllowedToolbox = new List<string> { "Bubble_Sort_Machine" }
        };
    }
}
