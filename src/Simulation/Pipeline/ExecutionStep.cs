using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Data;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>Structured operation kinds recorded in an <see cref="ExecutionStep"/>.</summary>
public enum ExecutionStepType
{
    NodeStarted,
    NodeCompleted,
    Compare,
    Swap,
    DataStreamProduced,
    ConnectionTraversed,
    OutputReceived,
    ValidationSuccess,
    ValidationFailure,
    ExecutionBlocked
}

public enum CompareOutcome
{
    LessThan,
    Equal,
    GreaterThan
}

/// <summary>
/// Immutable, UI-agnostic checkpoint for one simulation step.
/// Authoritative for forward/backward playback and future explanation features.
/// </summary>
public sealed class ExecutionStep
{
    public required int Sequence { get; init; }
    public required string NodeId { get; init; }
    public required ExecutionStepType StepType { get; init; }
    public IReadOnlyList<int> Indexes { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> Values { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> ValuesAfter { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> StateBefore { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> StateAfter { get; init; } = Array.Empty<int>();
    public int CpuCycles { get; init; }
    public int RamSlots { get; init; }
    public int PeakRamSlots { get; init; }
    public string? RelatedPort { get; init; }
    public string? RelatedConnectionId { get; init; }
    public CompareOutcome? CompareResult { get; init; }
    public ExecutionFailure? Failure { get; init; }
    public string? FromNodeId { get; init; }
    public string? ToNodeId { get; init; }

    /// <summary>Legacy string label used by older tests/UI (not for AI parsing).</summary>
    public string StepKindLabel => StepType switch
    {
        ExecutionStepType.NodeStarted => "Started",
        ExecutionStepType.NodeCompleted => "Completed",
        ExecutionStepType.Compare => "Compared",
        ExecutionStepType.Swap => "Swapped",
        ExecutionStepType.DataStreamProduced => "DataStreamProduced",
        ExecutionStepType.ConnectionTraversed => "ConnectionTraversed",
        ExecutionStepType.OutputReceived => "Received",
        ExecutionStepType.ValidationSuccess => "Validation",
        ExecutionStepType.ValidationFailure => "Validation",
        ExecutionStepType.ExecutionBlocked => "Blocked",
        _ => StepType.ToString()
    };

    public string? DetailSummary => StepType switch
    {
        ExecutionStepType.Compare when Values.Count >= 2 => $"{Values[0]} vs {Values[1]}",
        ExecutionStepType.Swap => $"[{string.Join(", ", StateAfter)}]",
        ExecutionStepType.DataStreamProduced => $"[{string.Join(", ", StateAfter)}]",
        ExecutionStepType.ConnectionTraversed => $"{FromNodeId} → {ToNodeId}",
        ExecutionStepType.ValidationSuccess => "SUCCESS",
        ExecutionStepType.ValidationFailure or ExecutionStepType.ExecutionBlocked => Failure?.Reason,
        _ => null
    };

    /// <summary>
    /// Structured implication for compare steps — for UI and future explanation consumers.
    /// Not a prose sentence for AI to parse; a typed outcome already exists in <see cref="CompareResult"/>.
    /// </summary>
    public string? CompareImplicationKey => StepType != ExecutionStepType.Compare
        ? null
        : CompareResult switch
        {
            CompareOutcome.GreaterThan => "OutOfOrderNeedsSwap",
            CompareOutcome.LessThan => "InOrderContinue",
            CompareOutcome.Equal => "EqualContinue",
            _ => null
        };
}

/// <summary>
/// Structured payload a future explanation feature can consume — no AI/network types.
/// </summary>
public sealed class ExecutionExplanationContext
{
    public required string LevelId { get; init; }
    public required string LevelTitle { get; init; }
    public required string LevelDescription { get; init; }
    public required IReadOnlyList<int> LevelInput { get; init; }
    public required IReadOnlyList<int> LevelExpectedOutput { get; init; }
    public required int MaxCpuCycles { get; init; }
    public required int MaxRamSlots { get; init; }
    public required ExecutionStep Step { get; init; }
    public ExecutionStep? PreviousStep { get; init; }
    public ExecutionStep? NextStep { get; init; }
    public required int StepIndex { get; init; }
    public required int TotalSteps { get; init; }
    public ExecutionFailure? FailureContext { get; init; }
}

/// <summary>Presentation-only durations. Never affect CPU/RAM or algorithm results.</summary>
public static class PlaybackTimingConfig
{
    /// <summary>Short beat — machine wakes / finishes.</summary>
    public static readonly TimeSpan NodeActivation = TimeSpan.FromMilliseconds(650);

    /// <summary>Medium — shipment travels a connection.</summary>
    public static readonly TimeSpan DataTransfer = TimeSpan.FromMilliseconds(700);

    /// <summary>Longer — focus, show relation, state implication.</summary>
    public static readonly TimeSpan Comparison = TimeSpan.FromMilliseconds(1400);

    /// <summary>Longer — identity-preserving exchange path.</summary>
    public static readonly TimeSpan Swap = TimeSpan.FromMilliseconds(1200);

    public static readonly TimeSpan NodeCompletion = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan Default = TimeSpan.FromMilliseconds(500);

    public static TimeSpan DurationFor(ExecutionStepType stepType, float speedMultiplier)
    {
        float speed = speedMultiplier <= 0f ? 1f : speedMultiplier;
        TimeSpan baseDuration = stepType switch
        {
            ExecutionStepType.NodeStarted => NodeActivation,
            ExecutionStepType.NodeCompleted => NodeCompletion,
            ExecutionStepType.Compare => Comparison,
            ExecutionStepType.Swap => Swap,
            ExecutionStepType.ConnectionTraversed => DataTransfer,
            ExecutionStepType.DataStreamProduced => DataTransfer,
            ExecutionStepType.OutputReceived => DataTransfer,
            ExecutionStepType.ValidationSuccess => TimeSpan.FromMilliseconds(600),
            ExecutionStepType.ValidationFailure or ExecutionStepType.ExecutionBlocked =>
                TimeSpan.FromMilliseconds(700),
            _ => Default
        };

        return TimeSpan.FromMilliseconds(baseDuration.TotalMilliseconds / speed);
    }
}

/// <summary>
/// Authoritative recorded run. Steps are immutable checkpoints; Entries remain for legacy asserts.
/// </summary>
public sealed class ExecutionTrace
{
    private readonly List<ExecutionStep> _steps = new();
    private readonly List<ExecutionTraceEntry> _entries = new();

    public IReadOnlyList<ExecutionStep> Steps => _steps;
    public IReadOnlyList<ExecutionTraceEntry> Entries => _entries;
    public int Count => _steps.Count;
    public ExecutionFailure? TerminalFailure { get; private set; }
    public bool IsSuccess { get; private set; }

    public void Record(ExecutionStep step)
    {
        _steps.Add(step);
        _entries.Add(new ExecutionTraceEntry
        {
            Sequence = step.Sequence + 1,
            NodeId = step.NodeId,
            StepKind = step.StepKindLabel,
            Detail = step.DetailSummary,
            CpuCycles = step.CpuCycles,
            RamSlots = step.RamSlots
        });
    }

    /// <summary>Legacy helper — prefer structured <see cref="Record(ExecutionStep)"/>.</summary>
    public void Add(string nodeId, string stepKind, string? detail, int cpu, int ram)
    {
        ExecutionStepType type = stepKind switch
        {
            "Started" => ExecutionStepType.NodeStarted,
            "Completed" => ExecutionStepType.NodeCompleted,
            "Compared" => ExecutionStepType.Compare,
            "Swapped" => ExecutionStepType.Swap,
            "Produced" or "DataStreamProduced" => ExecutionStepType.DataStreamProduced,
            "ConnectionTraversed" => ExecutionStepType.ConnectionTraversed,
            "Received" => ExecutionStepType.OutputReceived,
            "Validation" when string.Equals(detail, "SUCCESS", StringComparison.Ordinal) => ExecutionStepType.ValidationSuccess,
            "Validation" => ExecutionStepType.ValidationFailure,
            "Blocked" => ExecutionStepType.ExecutionBlocked,
            _ => ExecutionStepType.NodeCompleted
        };

        Record(new ExecutionStep
        {
            Sequence = _steps.Count,
            NodeId = nodeId,
            StepType = type,
            CpuCycles = cpu,
            RamSlots = ram,
            Failure = type is ExecutionStepType.ValidationFailure or ExecutionStepType.ExecutionBlocked
                ? new ExecutionFailure
                {
                    NodeId = nodeId,
                    FailureType = ExecutionFailureType.EvaluationMismatch,
                    Reason = detail ?? stepKind
                }
                : null
        });
    }

    public void MarkSuccess()
    {
        IsSuccess = true;
        TerminalFailure = null;
    }

    public void MarkFailure(ExecutionFailure failure)
    {
        IsSuccess = false;
        TerminalFailure = failure;
    }

    /// <summary>
    /// True when the trace contains educational execution content worth watching.
    /// Validation-only failures (blocked with no machine work) are not watchable.
    /// </summary>
    public static bool IsWatchable(ExecutionTrace? trace)
    {
        if (trace is null || trace.Count == 0)
        {
            return false;
        }

        return trace.Steps.Any(step => step.StepType is
            ExecutionStepType.NodeStarted
            or ExecutionStepType.Compare
            or ExecutionStepType.Swap
            or ExecutionStepType.ConnectionTraversed
            or ExecutionStepType.DataStreamProduced
            or ExecutionStepType.OutputReceived
            or ExecutionStepType.NodeCompleted);
    }

    public ExecutionExplanationContext BuildExplanationContext(LevelData level, int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (stepIndex < 0 || stepIndex >= _steps.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stepIndex));
        }

        ExecutionStep step = _steps[stepIndex];
        return new ExecutionExplanationContext
        {
            LevelId = level.LevelId,
            LevelTitle = level.Title,
            LevelDescription = level.Description,
            LevelInput = level.InputStream.ToArray(),
            LevelExpectedOutput = level.ExpectedOutput.ToArray(),
            MaxCpuCycles = level.Budgets.MaxCPUCycles,
            MaxRamSlots = level.Budgets.MaxRAMSlots,
            Step = step,
            PreviousStep = stepIndex > 0 ? _steps[stepIndex - 1] : null,
            NextStep = stepIndex + 1 < _steps.Count ? _steps[stepIndex + 1] : null,
            StepIndex = stepIndex,
            TotalSteps = _steps.Count,
            FailureContext = step.Failure ?? TerminalFailure
        };
    }
}

public sealed class ExecutionTraceEntry
{
    public required int Sequence { get; init; }
    public required string NodeId { get; init; }
    public required string StepKind { get; init; }
    public string? Detail { get; init; }
    public int CpuCycles { get; init; }
    public int RamSlots { get; init; }
}
