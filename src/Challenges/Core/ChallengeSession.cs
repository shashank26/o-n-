using System;

namespace PipelineArchitect.Challenges.Core;

/// <summary>
/// Owns one interactive discovery session. Pure C# — no Godot, no SimulationEngine.
/// View-layer previews must use <see cref="Evaluate"/> only; commit via
/// <see cref="CommitWhileLocked"/> after an authoritative animation.
/// </summary>
public sealed class ChallengeSession
{
    private readonly ChallengeDefinition _definition;
    private readonly IChallengeRules _rules;
    private IChallengeState _state;
    private string? _lastFeedback;
    private string? _lastNarrative;
    private bool _completed;
    private ConceptReveal? _reveal;

    public ChallengeSession(ChallengeDefinition definition, IChallengeRules rules)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        ConceptHidden = true;
        _state = _rules.CreateInitialState(_definition);
    }

    public ChallengeDefinition Definition => _definition;

    /// <summary>True until the player finishes discovery and reveal is unlocked.</summary>
    public bool ConceptHidden { get; private set; }

    public bool IsComplete => _completed;

    public ConceptReveal? Reveal => _reveal;

    public IChallengeState CurrentState => _state;

    /// <summary>
    /// True while an authoritative operation animation is in progress.
    /// Preview loops may continue; player commits are gated.
    /// </summary>
    public bool IsInteractionLocked { get; private set; }

    public event Action<ChallengePresentation>? PresentationChanged;

    public ChallengePresentation GetPresentation() =>
        _rules.BuildPresentation(
            _definition,
            _state,
            ConceptHidden,
            _lastFeedback,
            _lastNarrative,
            ConceptHidden ? null : _reveal);

    /// <summary>
    /// Pure evaluation of an action against the current state.
    /// Does not mutate session state — for previews and pre-animation validation only.
    /// </summary>
    public ChallengeActionResult Evaluate(ChallengeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_completed)
        {
            return new ChallengeActionResult
            {
                Accepted = false,
                State = _state,
                Feedback = "This challenge is already complete.",
                AvailableActionIds = Array.Empty<string>()
            };
        }

        return _rules.Apply(_state, action);
    }

    public bool BeginAuthoritativeTransition()
    {
        if (IsInteractionLocked || _completed)
        {
            return false;
        }

        IsInteractionLocked = true;
        return true;
    }

    public void EndAuthoritativeTransition()
    {
        IsInteractionLocked = false;
    }

    /// <summary>
    /// Publishes rejection feedback without changing challenge state.
    /// </summary>
    public void PublishRejectionFeedback(string? feedback)
    {
        _lastFeedback = feedback;
        Publish();
    }

    public ChallengeActionResult TryAction(ChallengeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsInteractionLocked)
        {
            return new ChallengeActionResult
            {
                Accepted = false,
                State = _state,
                Feedback = "Wait for the current operation to finish.",
                AvailableActionIds = Array.Empty<string>()
            };
        }

        return CommitCore(action, unlockAfter: false);
    }

    /// <summary>
    /// Commits a validated action while interaction is locked (end of authoritative animation).
    /// Unlocks after the commit.
    /// </summary>
    public ChallengeActionResult CommitWhileLocked(ChallengeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!IsInteractionLocked)
        {
            return TryAction(action);
        }

        return CommitCore(action, unlockAfter: true);
    }

    private ChallengeActionResult CommitCore(ChallengeAction action, bool unlockAfter)
    {
        if (_completed)
        {
            if (unlockAfter)
            {
                IsInteractionLocked = false;
            }

            return new ChallengeActionResult
            {
                Accepted = false,
                State = _state,
                Feedback = "This challenge is already complete.",
                AvailableActionIds = Array.Empty<string>()
            };
        }

        ChallengeActionResult result = _rules.Apply(_state, action);
        if (!result.Accepted)
        {
            _lastFeedback = result.Feedback;
            if (unlockAfter)
            {
                IsInteractionLocked = false;
            }

            Publish();
            return result;
        }

        _state = result.State;
        _lastFeedback = null;
        _lastNarrative = result.NarrativeCue;

        if (result.ChallengeCompleted)
        {
            _completed = true;
            if (_definition.RevealConceptAfterCompletion)
            {
                ConceptHidden = false;
                _reveal = _rules.BuildConceptReveal(_definition);
            }
        }

        if (unlockAfter)
        {
            IsInteractionLocked = false;
        }

        Publish();
        return new ChallengeActionResult
        {
            Accepted = true,
            State = _state,
            NarrativeCue = result.NarrativeCue,
            AvailableActionIds = result.AvailableActionIds,
            ChallengeCompleted = _completed,
            VisualHint = result.VisualHint
        };
    }

    /// <summary>Resets working state; concept remains hidden.</summary>
    public void Restart()
    {
        IsInteractionLocked = false;
        ConceptHidden = true;
        _completed = false;
        _reveal = null;
        _lastFeedback = null;
        _lastNarrative = null;
        _state = _rules.CreateInitialState(_definition);
        Publish();
    }

    private void Publish() => PresentationChanged?.Invoke(GetPresentation());
}
