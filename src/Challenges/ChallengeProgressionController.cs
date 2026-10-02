using System;
using PipelineArchitect.Challenges.BubbleSort;
using PipelineArchitect.Challenges.Core;

namespace PipelineArchitect.Challenges;

/// <summary>
/// Authoritative three-exercise problem progression:
/// Guided → Practice → Mastery → Problem Complete.
/// Pure C# — no Godot, LevelData mutation, or SimulationEngine calls.
/// </summary>
public sealed class ChallengeProgressionController
{
    public const int TotalExercises = 3;

    private readonly InteractiveChallengeConfig _config;
    private ChallengeSession _guided;
    private ChallengeSession? _practice;
    private ChallengeSession? _mastery;
    private ChallengeValidationResult? _lastValidation;
    private readonly bool[] _exerciseCompleted = new bool[TotalExercises];

    public ChallengeProgressionController(InteractiveChallengeConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _guided = ChallengeFactory.CreateGuidedSession(config);
        CurrentExercise = ProblemExerciseSlot.GuidedDiscovery;
        Phase = ChallengeFlowPhase.Guided;
        ActiveSession = _guided;
    }

    public ChallengeFlowPhase Phase { get; private set; }

    public ProblemExerciseSlot CurrentExercise { get; private set; }

    public int CurrentExerciseNumber => (int)CurrentExercise;

    public ChallengeSession ActiveSession { get; private set; }

    public ChallengeSession GuidedSession => _guided;

    public ChallengeSession? PracticeSession => _practice;

    public ChallengeSession? MasterySession => _mastery;

    public ChallengeValidationResult? LastValidation => _lastValidation;

    public bool IsProblemComplete { get; private set; }

    public bool HasPracticeChallenge =>
        _config.PracticeChallenge is { Enabled: true };

    public bool HasMasteryChallenge =>
        _config.MasteryChallenge is { Enabled: true }
        || HasPracticeChallenge; // default mastery from practice template + distinct values

    public event Action<ChallengeFlowPhase>? PhaseChanged;

    public bool IsExerciseCompleted(ProblemExerciseSlot slot) =>
        _exerciseCompleted[(int)slot - 1];

    public bool IsExerciseCompleted(int exerciseNumber) =>
        exerciseNumber is >= 1 and <= TotalExercises && _exerciseCompleted[exerciseNumber - 1];

    public void NotifyGuidedCompleted()
    {
        if (Phase != ChallengeFlowPhase.Guided || !_guided.IsComplete)
        {
            return;
        }

        SetPhase(ChallengeFlowPhase.ConceptReveal);
    }

    public void ContinueFromConceptReveal()
    {
        if (Phase != ChallengeFlowPhase.ConceptReveal)
        {
            return;
        }

        SetPhase(ChallengeFlowPhase.MachineReveal);
    }

    /// <summary>
    /// After machine reveal: mark Exercise 1 complete, then interstitial (or skip to factory if no further exercises).
    /// </summary>
    public void ContinueFromMachineReveal()
    {
        if (Phase != ChallengeFlowPhase.MachineReveal)
        {
            return;
        }

        MarkExerciseComplete(ProblemExerciseSlot.GuidedDiscovery);
        if (!HasPracticeChallenge && !HasMasteryChallenge)
        {
            CompleteProblem();
            return;
        }

        SetPhase(ChallengeFlowPhase.ExerciseComplete);
    }

    /// <summary>
    /// Advances from an exercise-complete interstitial to the next exercise or problem complete screen.
    /// </summary>
    public void ContinueAfterExerciseComplete()
    {
        if (Phase != ChallengeFlowPhase.ExerciseComplete)
        {
            return;
        }

        if (CurrentExercise == ProblemExerciseSlot.GuidedDiscovery)
        {
            StartPractice();
            return;
        }

        if (CurrentExercise == ProblemExerciseSlot.SortItYourself)
        {
            if (HasMasteryChallenge)
            {
                StartMastery();
            }
            else
            {
                CompleteProblem();
            }

            return;
        }

        if (CurrentExercise == ProblemExerciseSlot.FinalChallenge)
        {
            CompleteProblem();
        }
    }

    public ChallengeValidationResult SubmitActiveUnguidedComplete()
    {
        ChallengeSession? session = Phase switch
        {
            ChallengeFlowPhase.Unguided => _practice,
            ChallengeFlowPhase.Mastery => _mastery,
            _ => null
        };

        if (session is null)
        {
            return new ChallengeValidationResult
            {
                IsSuccess = false,
                SummaryMessage = "Exercise is not active."
            };
        }

        if (!session.IsInteractionLocked)
        {
            session.BeginAuthoritativeTransition();
        }

        ChallengeActionResult complete = session.Evaluate(
            new ChallengeAction { ActionId = ChallengeActionIds.Complete });
        if (complete.Accepted)
        {
            session.CommitWhileLocked(new ChallengeAction { ActionId = ChallengeActionIds.Complete });
        }
        else if (session.IsInteractionLocked)
        {
            session.EndAuthoritativeTransition();
        }

        var state = (AdjacentSortPracticeState)session.CurrentState;
        _lastValidation = AdjacentSortPracticeValidator.Validate(state);

        if (_lastValidation.IsSuccess)
        {
            if (Phase == ChallengeFlowPhase.Unguided)
            {
                MarkExerciseComplete(ProblemExerciseSlot.SortItYourself);
            }
            else if (Phase == ChallengeFlowPhase.Mastery)
            {
                MarkExerciseComplete(ProblemExerciseSlot.FinalChallenge);
            }

            SetPhase(ChallengeFlowPhase.ExerciseComplete);
        }
        else
        {
            SetPhase(ChallengeFlowPhase.Failure);
        }

        return _lastValidation;
    }

    /// <summary>Legacy alias used by older callers / tests.</summary>
    public ChallengeValidationResult SubmitPracticeComplete() => SubmitActiveUnguidedComplete();

    public void RetryCurrentExercise()
    {
        _lastValidation = null;
        switch (CurrentExercise)
        {
            case ProblemExerciseSlot.SortItYourself:
                StartPractice(resetCompletion: false);
                break;
            case ProblemExerciseSlot.FinalChallenge:
                StartMastery(resetCompletion: false);
                break;
            default:
                ReplayGuided();
                break;
        }
    }

    public void RetryPractice() => RetryCurrentExercise();

    public void ReplayExercise(ProblemExerciseSlot slot)
    {
        _lastValidation = null;
        switch (slot)
        {
            case ProblemExerciseSlot.GuidedDiscovery:
                ReplayGuided();
                break;
            case ProblemExerciseSlot.SortItYourself:
                // Clear later completions when replaying earlier work.
                _exerciseCompleted[1] = false;
                _exerciseCompleted[2] = false;
                IsProblemComplete = false;
                StartPractice(resetCompletion: false);
                break;
            case ProblemExerciseSlot.FinalChallenge:
                _exerciseCompleted[2] = false;
                IsProblemComplete = false;
                StartMastery(resetCompletion: false);
                break;
        }
    }

    public void ReplayGuided()
    {
        _guided = ChallengeFactory.CreateGuidedSession(_config);
        _practice = null;
        _mastery = null;
        _exerciseCompleted[0] = false;
        _exerciseCompleted[1] = false;
        _exerciseCompleted[2] = false;
        IsProblemComplete = false;
        CurrentExercise = ProblemExerciseSlot.GuidedDiscovery;
        ActiveSession = _guided;
        _lastValidation = null;
        SetPhase(ChallengeFlowPhase.Guided);
    }

    public void ReplayPractice() => ReplayExercise(ProblemExerciseSlot.SortItYourself);

    private void StartPractice(bool resetCompletion = false)
    {
        if (resetCompletion)
        {
            _exerciseCompleted[1] = false;
            _exerciseCompleted[2] = false;
            IsProblemComplete = false;
        }

        _practice = ChallengeFactory.CreatePracticeSession(_config);
        _mastery = null;
        CurrentExercise = ProblemExerciseSlot.SortItYourself;
        ActiveSession = _practice;
        _lastValidation = null;
        SetPhase(ChallengeFlowPhase.Unguided);
    }

    private void StartMastery(bool resetCompletion = false)
    {
        if (resetCompletion)
        {
            _exerciseCompleted[2] = false;
            IsProblemComplete = false;
        }

        _mastery = ChallengeFactory.CreateMasterySession(_config);
        CurrentExercise = ProblemExerciseSlot.FinalChallenge;
        ActiveSession = _mastery;
        _lastValidation = null;
        SetPhase(ChallengeFlowPhase.Mastery);
    }

    private void MarkExerciseComplete(ProblemExerciseSlot slot)
    {
        _exerciseCompleted[(int)slot - 1] = true;
        CurrentExercise = slot;
    }

    private void CompleteProblem()
    {
        if (!_exerciseCompleted[0]
            || (HasPracticeChallenge && !_exerciseCompleted[1])
            || (HasMasteryChallenge && !_exerciseCompleted[2]))
        {
            // Never mark problem complete without required exercises.
            return;
        }

        IsProblemComplete = true;
        SetPhase(ChallengeFlowPhase.ProblemComplete);
    }

    private void SetPhase(ChallengeFlowPhase phase)
    {
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
