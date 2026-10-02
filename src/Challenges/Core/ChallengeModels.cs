using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PipelineArchitect.Challenges.Core;

/// <summary>Well-known challenge type ids used in level JSON (not player-facing).</summary>
public static class ChallengeIds
{
    /// <summary>Adjacent-compare discovery that later reveals Bubble Sort.</summary>
    public const string AdjacentSortDiscovery = "adjacent_sort_discovery";

    /// <summary>Unguided adjacent-sort practice (player-driven pass boundaries).</summary>
    public const string AdjacentSortPractice = "adjacent_sort_practice";
}

/// <summary>Player action identifiers for discovery challenges.</summary>
public static class ChallengeActionIds
{
    public const string Swap = "swap";
    public const string Keep = "keep";
    public const string Continue = "continue";
    public const string NextPass = "next_pass";
    public const string Complete = "complete";
}

/// <summary>
/// Data-driven challenge attachment on a level. Concept strings are stored for reveal
/// but must never be shown while <see cref="ChallengeSession.ConceptHidden"/> is true.
/// </summary>
public sealed class InteractiveChallengeConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>Internal challenge type id (e.g. <see cref="ChallengeIds.AdjacentSortDiscovery"/>).</summary>
    [JsonPropertyName("challengeId")]
    public required string ChallengeId { get; init; }

    [JsonPropertyName("playerTitle")]
    public required string PlayerTitle { get; init; }

    [JsonPropertyName("playerDescription")]
    public required string PlayerDescription { get; init; }

    /// <summary>Working values for the discovery session. Must not alias LevelData.InputStream.</summary>
    [JsonPropertyName("initialValues")]
    public required IReadOnlyList<int> InitialValues { get; init; }

    [JsonPropertyName("revealConceptAfterCompletion")]
    public bool RevealConceptAfterCompletion { get; init; } = true;

    /// <summary>Revealed only after successful completion.</summary>
    [JsonPropertyName("conceptName")]
    public required string ConceptName { get; init; }

    [JsonPropertyName("conceptExplanation")]
    public required string ConceptExplanation { get; init; }

    /// <summary>Toolbox block unlocked after machine reveal (e.g. Bubble_Sort_Machine).</summary>
    [JsonPropertyName("machineBlockId")]
    public required string MachineBlockId { get; init; }

    [JsonPropertyName("machineRevealTitle")]
    public string MachineRevealTitle { get; init; } = "Meet Your New Machine";

    [JsonPropertyName("machineRevealBody")]
    public string MachineRevealBody { get; init; } =
        "You just performed the process yourself.\nNow let's make the machine do the repetitive work.";

    /// <summary>
    /// Optional unguided practice after concept/machine reveal (Exercise 2).
    /// </summary>
    [JsonPropertyName("practiceChallenge")]
    public PracticeChallengeConfig? PracticeChallenge { get; init; }

    /// <summary>
    /// Optional mastery check with a fresh dataset (Exercise 3).
    /// </summary>
    [JsonPropertyName("masteryChallenge")]
    public PracticeChallengeConfig? MasteryChallenge { get; init; }
}

/// <summary>Data-driven unguided practice / mastery attachment (Exercises 2–3).</summary>
public sealed class PracticeChallengeConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; init; } = ChallengeIds.AdjacentSortPractice;

    [JsonPropertyName("playerTitle")]
    public string PlayerTitle { get; init; } = "Sort It Yourself";

    [JsonPropertyName("playerDescription")]
    public string PlayerDescription { get; init; } =
        "You know how this works. Now sort the shipment yourself.";

    /// <summary>Working values for this exercise. Falls back to guided initialValues when omitted.</summary>
    [JsonPropertyName("initialValues")]
    public IReadOnlyList<int>? InitialValues { get; init; }
}

/// <summary>Immutable definition resolved for a session (from config + defaults).</summary>
public sealed class ChallengeDefinition
{
    public required string ChallengeId { get; init; }
    public required string PlayerTitle { get; init; }
    public required string PlayerDescription { get; init; }
    public required IReadOnlyList<int> InitialValues { get; init; }
    public required string ConceptName { get; init; }
    public required string ConceptExplanation { get; init; }
    public required string MachineBlockId { get; init; }
    public required string MachineRevealTitle { get; init; }
    public required string MachineRevealBody { get; init; }
    public bool RevealConceptAfterCompletion { get; init; }

    /// <summary>When true, presentation may show guided compare prompts.</summary>
    public bool IsGuided { get; init; } = true;

    public PracticeChallengeConfig? PracticeChallenge { get; init; }

    public PracticeChallengeConfig? MasteryChallenge { get; init; }
}

/// <summary>Opaque algorithm-specific state. Generic framework never inspects fields.</summary>
public interface IChallengeState
{
}

/// <summary>Deterministic history entry for process validation (not UI state).</summary>
public sealed class ChallengeActionRecord
{
    public required int Sequence { get; init; }
    public required int PassNumber { get; init; }
    public required string ActionId { get; init; }
    public int? IndexA { get; init; }
    public int? IndexB { get; init; }
    public int? ValueA { get; init; }
    public int? ValueB { get; init; }
    public required IReadOnlyList<int> StateAfter { get; init; }
    public bool? WasPrematurePassBoundary { get; init; }
    public int? ExpectedComparisonsInPass { get; init; }
    public int? ActualComparisonsInPass { get; init; }
}

/// <summary>Outcome of COMPLETE validation for unguided practice.</summary>
public sealed class ChallengeValidationResult
{
    public required bool IsSuccess { get; init; }
    public required string SummaryMessage { get; init; }
    public IReadOnlyList<string> DetailMessages { get; init; } = Array.Empty<string>();
}

/// <summary>High-level learning flow across a three-exercise problem.</summary>
public enum ChallengeFlowPhase
{
    Guided,
    ConceptReveal,
    MachineReveal,
    /// <summary>Brief interstitial after an exercise succeeds (not problem complete).</summary>
    ExerciseComplete,
    Unguided,
    Mastery,
    /// <summary>All three exercises succeeded.</summary>
    ProblemComplete,
    Failure
}

/// <summary>Authoritative exercise slot within a problem (1–3).</summary>
public enum ProblemExerciseSlot
{
    GuidedDiscovery = 1,
    SortItYourself = 2,
    FinalChallenge = 3
}

/// <summary>Player intent submitted to the session.</summary>
public sealed class ChallengeAction
{
    public required string ActionId { get; init; }
}

/// <summary>Result of validating and optionally applying a player action.</summary>
public sealed class ChallengeActionResult
{
    public required bool Accepted { get; init; }
    public required IChallengeState State { get; init; }
    public string? Feedback { get; init; }
    public string? NarrativeCue { get; init; }
    public IReadOnlyList<string> AvailableActionIds { get; init; } = Array.Empty<string>();
    public bool ChallengeCompleted { get; init; }
    public ChallengeVisualHint? VisualHint { get; init; }
}

/// <summary>View-layer animation hints derived from an accepted transition.</summary>
public sealed class ChallengeVisualHint
{
    public int? CompareIndexA { get; init; }
    public int? CompareIndexB { get; init; }
    public bool DidSwap { get; init; }
    public int? SortedFromEndCount { get; init; }
    public IReadOnlyList<int>? ValuesAfter { get; init; }
}

/// <summary>Post-completion concept reveal payload.</summary>
public sealed class ConceptReveal
{
    public required string ConceptName { get; init; }
    public required string Explanation { get; init; }
    public required string MachineBlockId { get; init; }
    public required string MachineRevealTitle { get; init; }
    public required string MachineRevealBody { get; init; }
}

/// <summary>Presentation snapshot for the View (safe strings only while concept hidden).</summary>
public sealed class ChallengePresentation
{
    public required string Title { get; init; }
    public required string Prompt { get; init; }
    public required IReadOnlyList<int> Values { get; init; }
    public int? HighlightIndexA { get; init; }
    public int? HighlightIndexB { get; init; }
    public int SortedFromEndCount { get; init; }
    public IReadOnlyList<string> AvailableActionIds { get; init; } = Array.Empty<string>();
    public string? Feedback { get; init; }
    public string? NarrativeCue { get; init; }
    public bool ConceptHidden { get; init; }
    public bool IsComplete { get; init; }
    public ConceptReveal? Reveal { get; init; }

    /// <summary>1-based pass index for display, or null when not applicable.</summary>
    public int? PassNumber { get; init; }

    /// <summary>Total passes for this shipment (typically N-1), or null when unknown.</summary>
    public int? TotalPasses { get; init; }

    /// <summary>True when guided compare prompts should appear.</summary>
    public bool ShowGuidance { get; init; }

    /// <summary>True when NEXT PASS should use a pass-transition animation.</summary>
    public bool IsPassTransition { get; init; }

    public string? PassLabel =>
        PassNumber is int pass && TotalPasses is int total && total > 0
            ? $"PASS {pass} / {total}"
            : PassNumber is int p
                ? $"PASS {p}"
                : null;
}

/// <summary>Algorithm-specific rules plugged into the generic session.</summary>
public interface IChallengeRules
{
    IChallengeState CreateInitialState(ChallengeDefinition definition);

    ChallengeActionResult Apply(IChallengeState state, ChallengeAction action);

    ChallengePresentation BuildPresentation(
        ChallengeDefinition definition,
        IChallengeState state,
        bool conceptHidden,
        string? feedback,
        string? narrativeCue,
        ConceptReveal? reveal);

    ConceptReveal BuildConceptReveal(ChallengeDefinition definition);
}
