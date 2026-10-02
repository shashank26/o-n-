using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Challenges.Core;

namespace PipelineArchitect.Challenges.BubbleSort;

/// <summary>
/// Bubble-Sort-specific discovery state. Generic framework never references these fields.
/// SortedBoundary = count of packages fixed at the end (same semantics as machine passes).
/// </summary>
public sealed class AdjacentSortDiscoveryState : IChallengeState
{
    public required IReadOnlyList<int> Values { get; init; }
    public required int ComparisonIndex { get; init; }
    public required int CurrentRound { get; init; }
    public required int SortedBoundary { get; init; }
    public required int PatternHintsShown { get; init; }
    public required bool IsFullySorted { get; init; }
}

/// <summary>
/// Manual adjacent compare/swap discovery. Ordering rules match
/// <c>BubbleSortMachineNode</c>: swap when left &gt; right; rounds shrink the unsorted prefix.
/// Player-facing copy never names the algorithm while concept is hidden.
/// </summary>
public sealed class AdjacentSortDiscoveryRules : IChallengeRules
{
    public IChallengeState CreateInitialState(ChallengeDefinition definition)
    {
        int[] values = definition.InitialValues.ToArray();
        return new AdjacentSortDiscoveryState
        {
            Values = values,
            ComparisonIndex = 0,
            CurrentRound = 0,
            SortedBoundary = 0,
            PatternHintsShown = 0,
            IsFullySorted = IsSorted(values)
        };
    }

    public ChallengeActionResult Apply(IChallengeState state, ChallengeAction action)
    {
        if (state is not AdjacentSortDiscoveryState bubble)
        {
            throw new InvalidOperationException("Adjacent sort rules received incompatible state.");
        }

        if (bubble.IsFullySorted)
        {
            return new ChallengeActionResult
            {
                Accepted = false,
                State = bubble,
                Feedback = "The shipment is already in order.",
                AvailableActionIds = Array.Empty<string>(),
                ChallengeCompleted = true
            };
        }

        string actionId = action.ActionId ?? string.Empty;
        if (actionId is not (ChallengeActionIds.Swap or ChallengeActionIds.Keep))
        {
            return new ChallengeActionResult
            {
                Accepted = false,
                State = bubble,
                Feedback = "Choose SWAP or KEEP for these two packages.",
                AvailableActionIds = AvailableActions()
            };
        }

        int index = bubble.ComparisonIndex;
        int left = bubble.Values[index];
        int right = bubble.Values[index + 1];
        bool shouldSwap = left > right;
        bool playerWantsSwap = actionId == ChallengeActionIds.Swap;

        if (playerWantsSwap != shouldSwap)
        {
            string feedback = shouldSwap
                ? $"{left} is larger than {right}.\nTo move larger values toward the end, swap them."
                : $"{left} is already smaller than or equal to {right}.\nKeep them and move on.";

            return new ChallengeActionResult
            {
                Accepted = false,
                State = bubble,
                Feedback = feedback,
                AvailableActionIds = AvailableActions(),
                VisualHint = new ChallengeVisualHint
                {
                    CompareIndexA = index,
                    CompareIndexB = index + 1,
                    DidSwap = false,
                    SortedFromEndCount = bubble.SortedBoundary,
                    ValuesAfter = bubble.Values.ToArray()
                }
            };
        }

        int[] nextValues = bubble.Values.ToArray();
        if (shouldSwap)
        {
            nextValues[index] = right;
            nextValues[index + 1] = left;
        }

        int n = nextValues.Length;
        int nextIndex = index + 1;
        int nextRound = bubble.CurrentRound;
        int nextBoundary = bubble.SortedBoundary;
        int patternHints = bubble.PatternHintsShown;
        string? narrative = null;
        bool roundFinished = nextIndex >= n - 1 - nextRound;

        if (roundFinished)
        {
            nextBoundary = nextRound + 1;
            nextRound++;
            nextIndex = 0;

            if (nextBoundary == 1)
            {
                narrative = "Something changed.\nThe largest package has reached the end.";
            }
            else if (nextBoundary >= 2 && patternHints == 0)
            {
                patternHints = 1;
                narrative =
                    "Again, the largest remaining package reached the end.\n\n" +
                    "Notice the pattern?\n" +
                    "Compare neighboring packages.\n" +
                    "Swap them when they are out of order.\n" +
                    "Move forward.\n" +
                    "Repeat for the remaining packages.";
            }
            else
            {
                narrative = "Again, the largest remaining package reached the end.\nKeep moving through the shipment.";
            }
        }
        else
        {
            narrative = "Keep moving through the shipment.";
        }

        bool fullySorted = nextRound >= n - 1 || IsSorted(nextValues);
        if (fullySorted)
        {
            nextBoundary = n;
            nextIndex = 0;
            narrative =
                "The shipment is in order.\n\n" +
                "These repeated rounds are called passes.";
        }

        var nextState = new AdjacentSortDiscoveryState
        {
            Values = nextValues,
            ComparisonIndex = fullySorted ? 0 : nextIndex,
            CurrentRound = fullySorted ? Math.Max(0, n - 1) : nextRound,
            SortedBoundary = nextBoundary,
            PatternHintsShown = patternHints,
            IsFullySorted = fullySorted
        };

        return new ChallengeActionResult
        {
            Accepted = true,
            State = nextState,
            NarrativeCue = narrative,
            AvailableActionIds = fullySorted ? Array.Empty<string>() : AvailableActions(),
            ChallengeCompleted = fullySorted,
            VisualHint = new ChallengeVisualHint
            {
                CompareIndexA = index,
                CompareIndexB = index + 1,
                DidSwap = shouldSwap,
                SortedFromEndCount = nextBoundary,
                ValuesAfter = nextValues
            }
        };
    }

    public ChallengePresentation BuildPresentation(
        ChallengeDefinition definition,
        IChallengeState state,
        bool conceptHidden,
        string? feedback,
        string? narrativeCue,
        ConceptReveal? reveal)
    {
        var bubble = (AdjacentSortDiscoveryState)state;
        string prompt;
        if (bubble.IsFullySorted)
        {
            prompt = narrativeCue ?? "The shipment is in order.";
        }
        else
        {
            int a = bubble.Values[bubble.ComparisonIndex];
            int b = bubble.Values[bubble.ComparisonIndex + 1];
            prompt =
                $"{definition.PlayerDescription}\n\n" +
                $"Current pair:\n{a}    {b}\n\n" +
                "What should happen?";
            if (!string.IsNullOrWhiteSpace(narrativeCue) && string.IsNullOrWhiteSpace(feedback))
            {
                prompt = narrativeCue + "\n\n" + $"Current pair:\n{a}    {b}\n\nWhat should happen?";
            }
        }

        return new ChallengePresentation
        {
            // Never surface concept name while hidden — title stays player-facing only.
            Title = definition.PlayerTitle,
            Prompt = prompt,
            Values = bubble.Values.ToArray(),
            HighlightIndexA = bubble.IsFullySorted ? null : bubble.ComparisonIndex,
            HighlightIndexB = bubble.IsFullySorted ? null : bubble.ComparisonIndex + 1,
            SortedFromEndCount = bubble.SortedBoundary,
            AvailableActionIds = bubble.IsFullySorted ? Array.Empty<string>() : AvailableActions(),
            Feedback = feedback,
            NarrativeCue = narrativeCue,
            ConceptHidden = conceptHidden,
            IsComplete = bubble.IsFullySorted,
            Reveal = conceptHidden ? null : reveal,
            PassNumber = PassDisplayNumber(bubble, definition.InitialValues.Count),
            TotalPasses = Math.Max(1, definition.InitialValues.Count - 1),
            ShowGuidance = true
        };
    }

    private static int PassDisplayNumber(AdjacentSortDiscoveryState bubble, int n)
    {
        if (bubble.IsFullySorted)
        {
            return Math.Max(1, n - 1);
        }

        // CurrentRound is 0-based; player-facing pass is 1-based.
        return bubble.CurrentRound + 1;
    }

    public ConceptReveal BuildConceptReveal(ChallengeDefinition definition) =>
        new()
        {
            ConceptName = definition.ConceptName,
            Explanation = definition.ConceptExplanation,
            MachineBlockId = definition.MachineBlockId,
            MachineRevealTitle = definition.MachineRevealTitle,
            MachineRevealBody = definition.MachineRevealBody
        };

    private static IReadOnlyList<string> AvailableActions() =>
        new[] { ChallengeActionIds.Swap, ChallengeActionIds.Keep };

    private static bool IsSorted(IReadOnlyList<int> values)
    {
        for (int i = 0; i < values.Count - 1; i++)
        {
            if (values[i] > values[i + 1])
            {
                return false;
            }
        }

        return true;
    }
}
