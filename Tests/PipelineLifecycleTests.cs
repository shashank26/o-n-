using System.Collections.Generic;
using PipelineArchitect.Simulation;
using PipelineArchitect.Simulation.Pipeline;
using PipelineArchitect.UI;
using Xunit;

namespace PipelineArchitect.Tests;

/// <summary>
/// Unit tests for the pure <see cref="PipelineLifecycle"/> workspace state machine.
/// </summary>
public sealed class PipelineLifecycleTests
{
    [Fact]
    public void NewLifecycle_StartsInEditing()
    {
        var lifecycle = new PipelineLifecycle();
        Assert.Equal(PipelineState.Editing, lifecycle.Current);
        Assert.False(lifecycle.IsGraphLocked);
    }

    [Fact]
    public void ValidInputBubbleSortOutputRoute_BecomesReady()
    {
        var lifecycle = new PipelineLifecycle();
        Assert.True(PipelineLifecycle.IsPipelineReady(CreateValidBubbleSortRoutes()));
        lifecycle.ReevaluateAfterGraphChange(structurallyReady: true);
        Assert.Equal(PipelineState.Ready, lifecycle.Current);
    }

    [Fact]
    public void IncompleteGraph_RemainsEditing()
    {
        var lifecycle = new PipelineLifecycle();
        Assert.False(PipelineLifecycle.IsPipelineReady(System.Array.Empty<PipelineRouteBinding>()));
        Assert.False(PipelineLifecycle.IsPipelineReady(CreateInputToBubbleOnlyRoutes()));
        Assert.False(PipelineLifecycle.IsPipelineReady(CreateInputToOutputOnlyRoutes()));
        lifecycle.ReevaluateAfterGraphChange(structurallyReady: false);
        Assert.Equal(PipelineState.Editing, lifecycle.Current);
    }

    [Fact]
    public void Ready_To_Running_WhenExecutionStarts()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.ReevaluateAfterGraphChange(true);
        Assert.Equal(PipelineState.Ready, lifecycle.Current);

        Assert.True(lifecycle.SetState(PipelineState.Running));
        Assert.Equal(PipelineState.Running, lifecycle.Current);
        Assert.True(lifecycle.IsGraphLocked);
        Assert.True(lifecycle.RejectGraphMutation());
    }

    [Fact]
    public void GraphMutationReevaluate_IgnoredWhileRunning()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);

        lifecycle.ReevaluateAfterGraphChange(structurallyReady: false);
        Assert.Equal(PipelineState.Running, lifecycle.Current);

        lifecycle.ReevaluateAfterGraphChange(structurallyReady: true);
        Assert.Equal(PipelineState.Running, lifecycle.Current);
    }

    [Fact]
    public void SuccessfulSimulation_Running_Completed_Success()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);

        var transitions = new List<(PipelineState From, PipelineState To)>();
        lifecycle.StateChanged += (from, to) => transitions.Add((from, to));

        lifecycle.ApplySimulationOutcome(isSuccess: true);

        Assert.Equal(PipelineState.Success, lifecycle.Current);
        Assert.Equal(2, transitions.Count);
        Assert.Equal((PipelineState.Running, PipelineState.Completed), transitions[0]);
        Assert.Equal((PipelineState.Completed, PipelineState.Success), transitions[1]);
    }

    [Fact]
    public void FailedSimulation_Running_Completed_Failed()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);

        lifecycle.ApplySimulationOutcome(isSuccess: false);

        Assert.Equal(PipelineState.Failed, lifecycle.Current);
        Assert.False(lifecycle.IsGraphLocked);
        Assert.False(lifecycle.RejectGraphMutation());
    }

    [Fact]
    public void AfterFailed_GraphChange_ReturnsToEditingOrReady()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);
        lifecycle.ApplySimulationOutcome(isSuccess: false);
        Assert.Equal(PipelineState.Failed, lifecycle.Current);

        lifecycle.ReevaluateAfterGraphChange(structurallyReady: false);
        Assert.Equal(PipelineState.Editing, lifecycle.Current);

        lifecycle.ReevaluateAfterGraphChange(structurallyReady: true);
        Assert.Equal(PipelineState.Ready, lifecycle.Current);
    }

    [Fact]
    public void AwaitingReplayChoice_ThenShowMe_ThenSuccess()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);
        lifecycle.EnterAwaitingReplayChoice();
        Assert.Equal(PipelineState.AwaitingReplayChoice, lifecycle.Current);
        Assert.True(lifecycle.BlocksNewExecution);

        lifecycle.ChooseShowMe();
        Assert.Equal(PipelineState.PlayingTrace, lifecycle.Current);

        lifecycle.ApplySimulationOutcome(isSuccess: true);
        Assert.Equal(PipelineState.Success, lifecycle.Current);
    }

    [Fact]
    public void AwaitingReplayChoice_Skip_GoesToFailed()
    {
        var lifecycle = new PipelineLifecycle();
        lifecycle.SetState(PipelineState.Running);
        lifecycle.EnterAwaitingReplayChoice();
        lifecycle.SkipReplay(isSuccess: false);
        Assert.Equal(PipelineState.Failed, lifecycle.Current);
        Assert.False(lifecycle.IsGraphLocked);
    }

    [Fact]
    public void SetState_IgnoresRedundantTransitions()
    {
        var lifecycle = new PipelineLifecycle();
        int changes = 0;
        lifecycle.StateChanged += (_, _) => changes++;

        Assert.False(lifecycle.SetState(PipelineState.Editing));
        Assert.Equal(0, changes);

        Assert.True(lifecycle.SetState(PipelineState.Ready));
        Assert.Equal(1, changes);
        Assert.False(lifecycle.SetState(PipelineState.Ready));
        Assert.Equal(1, changes);
    }

    private static List<PipelineRouteBinding> CreateValidBubbleSortRoutes()
    {
        return new List<PipelineRouteBinding>
        {
            new()
            {
                SourceNodeId = PipelineNodeIds.InputStream,
                SourcePort = 0,
                TargetNodeId = "Engine_Bubble_Sort_Machine",
                TargetPort = 0,
                TargetMachineId = "Bubble_Sort_Machine"
            },
            new()
            {
                SourceNodeId = "Engine_Bubble_Sort_Machine",
                SourcePort = 0,
                TargetNodeId = PipelineNodeIds.OutputSink,
                TargetPort = 0,
                TargetMachineId = string.Empty
            }
        };
    }

    private static List<PipelineRouteBinding> CreateInputToBubbleOnlyRoutes()
    {
        return new List<PipelineRouteBinding>
        {
            new()
            {
                SourceNodeId = PipelineNodeIds.InputStream,
                SourcePort = 0,
                TargetNodeId = "Engine_Bubble_Sort_Machine",
                TargetPort = 0,
                TargetMachineId = "Bubble_Sort_Machine"
            }
        };
    }

    private static List<PipelineRouteBinding> CreateInputToOutputOnlyRoutes()
    {
        return new List<PipelineRouteBinding>
        {
            new()
            {
                SourceNodeId = PipelineNodeIds.InputStream,
                SourcePort = 0,
                TargetNodeId = PipelineNodeIds.OutputSink,
                TargetPort = 0,
                TargetMachineId = string.Empty
            }
        };
    }
}
