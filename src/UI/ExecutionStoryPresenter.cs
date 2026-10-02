using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.UI;

/// <summary>
/// Presentation-only storytelling for recorded <see cref="ExecutionStep"/>s.
/// Inspired by deliberate visual teaching (cause → action → result), not debug logs.
/// Never writes simulation state.
/// </summary>
public sealed class ExecutionStoryPresenter
{
    private static readonly Color ActiveNode = new(1.0f, 0.92f, 0.55f, 1.0f);
    private static readonly Color FlowNode = new(0.45f, 0.90f, 1.0f, 1.0f);
    private static readonly Color QuietNode = new(1f, 1f, 1f, 0.42f);
    private static readonly Color IdleNode = Colors.White;
    private static readonly Color Danger = new(1.0f, 0.40f, 0.32f, 1.0f);

    private readonly Node _host;
    private readonly Func<string, GraphNode?> _findNode;
    private readonly Func<PipelineEngineNode?> _getActiveEngine;
    private readonly Func<PipelineInputStreamGraphNode?> _getInput;
    private readonly Func<PipelineOutputSinkGraphNode?> _getOutput;
    private readonly Action<string, string?, string> _setStatus;
    private CancellationTokenSource? _storyCts;

    public ExecutionStoryPresenter(
        Node host,
        Func<string, GraphNode?> findNode,
        Func<PipelineEngineNode?> getActiveEngine,
        Func<PipelineInputStreamGraphNode?> getInput,
        Func<PipelineOutputSinkGraphNode?> getOutput,
        Action<string, string?, string> setStatus)
    {
        _host = host;
        _findNode = findNode;
        _getActiveEngine = getActiveEngine;
        _getInput = getInput;
        _getOutput = getOutput;
        _setStatus = setStatus;
    }

    public void Cancel()
    {
        _storyCts?.Cancel();
        _storyCts = null;
    }

    /// <summary>
    /// Plays the visual narrative for one logical step. Duration follows presentation config × speed.
    /// </summary>
    public async Task PresentAsync(ExecutionStep step, bool movingForward, float speedMultiplier)
    {
        Cancel();
        _storyCts = new CancellationTokenSource();
        CancellationToken token = _storyCts.Token;
        float speed = speedMultiplier <= 0f ? 1f : speedMultiplier;
        float scale = 1f / speed;

        try
        {
            if (movingForward)
            {
                await PresentForwardAsync(step, scale, token).ConfigureAwait(true);
            }
            else
            {
                await PresentBackwardAsync(step, scale, token).ConfigureAwait(true);
            }
        }
        catch (TaskCanceledException)
        {
            // Newer step superseded this story.
        }
    }

    private async Task PresentForwardAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        switch (step.StepType)
        {
            case ExecutionStepType.NodeStarted:
                await PresentNodeStartedAsync(step, scale, token).ConfigureAwait(true);
                break;
            case ExecutionStepType.ConnectionTraversed:
                await PresentDataFlowAsync(step, scale, token).ConfigureAwait(true);
                break;
            case ExecutionStepType.Compare:
                await PresentCompareAsync(step, scale, token).ConfigureAwait(true);
                break;
            case ExecutionStepType.Swap:
                await PresentSwapAsync(step, scale, token).ConfigureAwait(true);
                break;
            case ExecutionStepType.DataStreamProduced:
                await PresentProducedAsync(step, scale, token).ConfigureAwait(true);
                break;
            case ExecutionStepType.OutputReceived:
                await PresentOutputReceivedAsync(step, scale, token).ConfigureAwait(true);
                break;
            case ExecutionStepType.NodeCompleted:
                await PresentNodeCompletedAsync(step, scale, token).ConfigureAwait(true);
                break;
            case ExecutionStepType.ValidationSuccess:
                EmphasizeNode(PipelineNodeIds.OutputSink, ActiveNode);
                _getOutput()?.PlaySuccessPop();
                _getActiveEngine()?.PlaySuccessPop();
                _setStatus("FACTORY COMPLETE", Friendly(step.NodeId), "Shipment sorted.");
                await Delay(0.45f * scale, token).ConfigureAwait(true);
                ResetNodeEmphasis();
                break;
            case ExecutionStepType.ValidationFailure:
            case ExecutionStepType.ExecutionBlocked:
                EmphasizeNode(step.NodeId, Danger);
                _setStatus("PIPELINE STOPPED", Friendly(step.NodeId), step.Failure?.Reason ?? "Stopped.");
                await Delay(0.55f * scale, token).ConfigureAwait(true);
                break;
            default:
                await Delay(0.25f * scale, token).ConfigureAwait(true);
                break;
        }
    }

    private async Task PresentBackwardAsync(ExecutionStep leavingStep, float scale, CancellationToken token)
    {
        PipelineEngineNode? engine = _getActiveEngine();
        PipelineStreamChips? chips = engine?.Chips;
        _setStatus("REWIND", Friendly(leavingStep.NodeId), "Restoring previous state…");

        SoftenUnrelated(leavingStep.NodeId);
        EmphasizeNode(leavingStep.NodeId, FlowNode);

        if (chips is not null && leavingStep.StateBefore.Count > 0)
        {
            await chips.AnimateRestoreAsync(leavingStep.StateBefore, 0.55f * scale).ConfigureAwait(true);
            engine?.SetOperationText("Restored");
        }

        await Delay(0.20f * scale, token).ConfigureAwait(true);
        ResetNodeEmphasis();
        chips?.ClearHighlights();
    }

    private async Task PresentNodeStartedAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        SoftenUnrelated(step.NodeId);
        EmphasizeNode(step.NodeId, ActiveNode);
        _setStatus("EXECUTING", Friendly(step.NodeId), "Machine waking up…");
        if (_getActiveEngine() is { } engine && step.NodeId == engine.Name)
        {
            engine.SetOperationText("Receiving shipment…");
            if (step.StateAfter.Count > 0)
            {
                engine.SetWorkingValues(step.StateAfter);
            }
        }

        await Delay(0.55f * scale, token).ConfigureAwait(true);
    }

    private async Task PresentDataFlowAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        string from = step.FromNodeId ?? step.NodeId;
        string to = step.ToNodeId ?? step.NodeId;
        SoftenUnrelated(from, to);
        EmphasizeNode(from, FlowNode);
        _setStatus("EXECUTING", Friendly(to), $"Shipment leaving {Friendly(from)}…");
        await Delay(0.28f * scale, token).ConfigureAwait(true);
        EmphasizeNode(to, ActiveNode);
        _setStatus("EXECUTING", Friendly(to), $"Shipment arriving at {Friendly(to)}…");
        await Delay(0.32f * scale, token).ConfigureAwait(true);
    }

    private async Task PresentCompareAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        PipelineEngineNode? engine = ResolveEngine(step.NodeId);
        if (engine is null || step.Indexes.Count < 2 || step.Values.Count < 2)
        {
            await Delay(0.35f * scale, token).ConfigureAwait(true);
            return;
        }

        SoftenUnrelated(step.NodeId);
        EmphasizeNode(step.NodeId, ActiveNode);

        // Ensure chips show StateBefore for the comparison narrative.
        if (step.StateBefore.Count > 0)
        {
            engine.SetWorkingValues(step.StateBefore);
        }

        int a = step.Indexes[0];
        int b = step.Indexes[1];
        int left = step.Values[0];
        int right = step.Values[1];

        // APPROACH / FOCUS
        engine.Chips.FocusIndices(a, b, forSwap: false);
        engine.SetOperationText("Looking here…");
        _setStatus("EXECUTING", "Bubble Sort", $"Focusing on {left} and {right}");
        await Delay(0.28f * scale, token).ConfigureAwait(true);

        // OPERATION — relationship
        string relation = step.CompareResult switch
        {
            CompareOutcome.GreaterThan => $"{left}  >  {right}",
            CompareOutcome.LessThan => $"{left}  <  {right}",
            CompareOutcome.Equal => $"{left}  =  {right}",
            _ => $"{left}  ?  {right}"
        };
        engine.SetOperationText(relation);
        _setStatus("EXECUTING", "Bubble Sort", $"Compare: {relation}");
        await Delay(0.55f * scale, token).ConfigureAwait(true);

        // RESULT — what it implies
        string implication = step.CompareResult == CompareOutcome.GreaterThan
            ? "Out of order — swap needed"
            : "In order — keep going";
        engine.SetOperationText(implication);
        await Delay(0.35f * scale, token).ConfigureAwait(true);
    }

    private async Task PresentSwapAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        PipelineEngineNode? engine = ResolveEngine(step.NodeId);
        if (engine is null || step.Indexes.Count < 2)
        {
            await Delay(0.35f * scale, token).ConfigureAwait(true);
            return;
        }

        SoftenUnrelated(step.NodeId);
        EmphasizeNode(step.NodeId, ActiveNode);

        if (step.StateBefore.Count > 0)
        {
            engine.SetWorkingValues(step.StateBefore);
        }

        int a = step.Indexes[0];
        int b = step.Indexes[1];
        engine.SetOperationText("SWAP");
        _setStatus("EXECUTING", "Bubble Sort", "Exchanging positions…");

        await engine.Chips.AnimateSwapAsync(a, b, 0.70f * scale).ConfigureAwait(true);
        if (step.StateAfter.Count > 0)
        {
            // Guarantee labels match authoritative after-state without recreating chips when possible.
            engine.SetWorkingValues(step.StateAfter);
        }

        engine.SetOperationText("Swapped");
        await Delay(0.28f * scale, token).ConfigureAwait(true);
        engine.Chips.ClearHighlights();
    }

    private async Task PresentProducedAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        SoftenUnrelated(step.NodeId);
        EmphasizeNode(step.NodeId, FlowNode);
        if (ResolveEngine(step.NodeId) is { } engine && step.StateAfter.Count > 0)
        {
            engine.SetWorkingValues(step.StateAfter);
            engine.SetOperationText("Sending result…");
        }

        if (PipelineNodeIds.IsInputStream(step.NodeId) && step.StateAfter.Count > 0)
        {
            _getInput()?.SetStreamValues(step.StateAfter);
        }

        _setStatus("EXECUTING", Friendly(step.NodeId), "Stream leaving the machine…");
        await Delay(0.40f * scale, token).ConfigureAwait(true);
    }

    private async Task PresentOutputReceivedAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        SoftenUnrelated(PipelineNodeIds.OutputSink, step.NodeId);
        EmphasizeNode(PipelineNodeIds.OutputSink, ActiveNode);
        if (step.StateAfter.Count > 0)
        {
            _getOutput()?.SetReceivedValues(step.StateAfter);
        }

        _setStatus("EXECUTING", "Output", "Shipment received.");
        await Delay(0.45f * scale, token).ConfigureAwait(true);
    }

    private async Task PresentNodeCompletedAsync(ExecutionStep step, float scale, CancellationToken token)
    {
        EmphasizeNode(step.NodeId, FlowNode);
        if (ResolveEngine(step.NodeId) is { } engine)
        {
            engine.SetOperationText("Done");
            engine.Chips.ClearHighlights();
        }

        _setStatus("EXECUTING", Friendly(step.NodeId), "Machine finished.");
        await Delay(0.30f * scale, token).ConfigureAwait(true);
        ResetNodeEmphasis();
    }

    private PipelineEngineNode? ResolveEngine(string nodeId)
    {
        if (_findNode(nodeId) is PipelineEngineNode engine)
        {
            return engine;
        }

        return _getActiveEngine();
    }

    private void SoftenUnrelated(params string[] keepHot)
    {
        foreach (string id in new[]
                 {
                     PipelineNodeIds.InputStream,
                     PipelineNodeIds.OutputSink
                 })
        {
            if (Array.IndexOf(keepHot, id) >= 0)
            {
                continue;
            }

            GraphNode? node = _findNode(id);
            if (node is not null)
            {
                node.Modulate = QuietNode;
            }
        }

        PipelineEngineNode? engine = _getActiveEngine();
        if (engine is not null && Array.IndexOf(keepHot, engine.Name) < 0)
        {
            // Keep engine slightly visible if it's the only machine.
            bool keepEngine = false;
            foreach (string id in keepHot)
            {
                if (id.StartsWith("Engine_", StringComparison.Ordinal))
                {
                    keepEngine = true;
                    break;
                }
            }

            engine.Modulate = keepEngine ? IdleNode : QuietNode;
        }
    }

    private void EmphasizeNode(string nodeId, Color color)
    {
        GraphNode? node = _findNode(nodeId);
        if (node is not null)
        {
            node.Modulate = color;
        }
    }

    private void ResetNodeEmphasis()
    {
        foreach (string id in new[] { PipelineNodeIds.InputStream, PipelineNodeIds.OutputSink })
        {
            GraphNode? node = _findNode(id);
            if (node is not null)
            {
                node.Modulate = IdleNode;
            }
        }

        PipelineEngineNode? engine = _getActiveEngine();
        if (engine is not null)
        {
            engine.Modulate = IdleNode;
        }
    }

    private async Task Delay(float seconds, CancellationToken token)
    {
        if (seconds <= 0.01f)
        {
            return;
        }

        SceneTree? tree = _host.GetTree();
        if (tree is null)
        {
            return;
        }

        SceneTreeTimer timer = tree.CreateTimer(seconds);
        await _host.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        token.ThrowIfCancellationRequested();
    }

    private static string Friendly(string nodeId)
    {
        if (PipelineNodeIds.IsInputStream(nodeId))
        {
            return "Input Shipment";
        }

        if (PipelineNodeIds.IsOutputSink(nodeId))
        {
            return "Output";
        }

        if (nodeId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase))
        {
            return "Bubble Sort";
        }

        if (nodeId.StartsWith("Engine_", StringComparison.Ordinal))
        {
            return nodeId["Engine_".Length..].Replace('_', ' ');
        }

        return nodeId;
    }
}
