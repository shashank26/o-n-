using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Data;

namespace PipelineArchitect.Simulation.Pipeline;

/// <summary>
/// Authoritative cursor over a recorded <see cref="ExecutionTrace"/>.
/// Forward/backward restores checkpoint state — presentation only visualizes.
/// </summary>
public sealed class TracePlaybackController
{
    private readonly LevelData _level;
    private readonly ExecutionTrace _trace;
    private int _currentIndex = -1;

    public TracePlaybackController(LevelData level, ExecutionTrace trace)
    {
        _level = level ?? throw new ArgumentNullException(nameof(level));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
    }

    public ExecutionTrace Trace => _trace;
    public int CurrentIndex => _currentIndex;
    public int TotalSteps => _trace.Count;
    public bool CanStepForward => _currentIndex < _trace.Count - 1;
    public bool CanStepBackward => _currentIndex >= 0;
    public bool IsAtStart => _currentIndex < 0;
    public bool IsAtEnd => _trace.Count == 0 || _currentIndex >= _trace.Count - 1;

    public ExecutionStep? CurrentStep =>
        _currentIndex >= 0 && _currentIndex < _trace.Count ? _trace.Steps[_currentIndex] : null;

    public IReadOnlyList<int> CurrentState
    {
        get
        {
            if (_currentIndex < 0)
            {
                return Array.Empty<int>();
            }

            return _trace.Steps[_currentIndex].StateAfter;
        }
    }

    public int CurrentCpu => CurrentStep?.CpuCycles ?? 0;
    public int CurrentRam => CurrentStep?.RamSlots ?? 0;
    public int CurrentPeakRam => CurrentStep?.PeakRamSlots ?? 0;

    /// <summary>Resets to before the first step without clearing the recorded trace.</summary>
    public void ResetToStart()
    {
        _currentIndex = -1;
    }

    public ExecutionStep? StepForward()
    {
        if (!CanStepForward)
        {
            return null;
        }

        _currentIndex++;
        return _trace.Steps[_currentIndex];
    }

    public ExecutionStep? StepBackward()
    {
        if (!CanStepBackward)
        {
            return null;
        }

        ExecutionStep leaving = _trace.Steps[_currentIndex];
        _currentIndex--;
        return leaving;
    }

    /// <summary>Seek to a specific applied step index (-1 = before start).</summary>
    public void Seek(int index)
    {
        if (index < -1 || index >= _trace.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _currentIndex = index;
    }

    public ExecutionExplanationContext? TryBuildExplanationContext()
    {
        if (_currentIndex < 0 || _currentIndex >= _trace.Count)
        {
            return null;
        }

        return _trace.BuildExplanationContext(_level, _currentIndex);
    }

    public string PositionLabel =>
        TotalSteps == 0
            ? "Step 0 / 0"
            : $"Step {Math.Max(0, _currentIndex + 1)} / {TotalSteps}";
}
