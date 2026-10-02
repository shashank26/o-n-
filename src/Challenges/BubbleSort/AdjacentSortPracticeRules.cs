using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Challenges.Core;

namespace PipelineArchitect.Challenges.BubbleSort;

/// <summary>
/// Unguided practice state. Player chooses when to end each pass.
/// History is authoritative for COMPLETE validation.
/// </summary>
public sealed class AdjacentSortPracticeState : IChallengeState
{
    public required IReadOnlyList<int> Values { get; init; }

    /// <summary>Active pair index within the current pass unsorted prefix.</summary>
    public required int ComparisonIndex { get; init; }

    /// <summary>1-based pass number shown to the player.</summary>
    public required int PassNumber { get; init; }

    public required int ComparisonsInCurrentPass { get; init; }

    /// <summary>True when the natural end of this pass has been reached (no more pairs).</summary>
    public required bool AtNaturalPassEnd { get; init; }

    public required IReadOnlyList<ChallengeActionRecord> History { get; init; }

    public required int ArrayLength { get; init; }
}

/// <summary>
/// Player-driven Bubble Sort practice. Same compare/swap semantics as the machine and guided challenge.
/// No algorithmic hints. NEXT PASS and COMPLETE are player decisions.
/// </summary>
public sealed class AdjacentSortPracticeRules : IChallengeRules
{
    public IChallengeState CreateInitialState(ChallengeDefinition definition)
    {
        int[] values = definition.InitialValues.ToArray();
        return new AdjacentSortPracticeState
        {
            Values = values,
            ComparisonIndex = 0,
            PassNumber = 1,
            ComparisonsInCurrentPass = 0,
            AtNaturalPassEnd = values.Length < 2,
            History = Array.Empty<ChallengeActionRecord>(),
            ArrayLength = values.Length
        };
    }

    public ChallengeActionResult Apply(IChallengeState state, ChallengeAction action)
    {
        if (state is not AdjacentSortPracticeState practice)
        {
            throw new InvalidOperationException("Practice rules received incompatible state.");
        }

        string actionId = action.ActionId ?? string.Empty;
        return actionId switch
        {
            ChallengeActionIds.Swap or ChallengeActionIds.Keep => ApplyCompareAction(practice, actionId),
            ChallengeActionIds.NextPass => ApplyNextPass(practice),
            ChallengeActionIds.Complete => ApplyComplete(practice),
            _ => new ChallengeActionResult
            {
                Accepted = false,
                State = practice,
                Feedback = null,
                AvailableActionIds = AvailableActions(practice)
            }
        };
    }

    private static ChallengeActionResult ApplyCompareAction(AdjacentSortPracticeState practice, string actionId)
    {
        if (practice.AtNaturalPassEnd || practice.Values.Count < 2)
        {
            return new ChallengeActionResult
            {
                Accepted = false,
                State = practice,
                AvailableActionIds = AvailableActions(practice)
            };
        }

        int n = practice.ArrayLength;
        int passIndex = practice.PassNumber - 1;
        int passLimit = n - 1 - passIndex;
        if (passLimit <= 0 || practice.ComparisonIndex >= passLimit)
        {
            var ended = Clone(practice, atNaturalPassEnd: true);
            return new ChallengeActionResult
            {
                Accepted = false,
                State = ended,
                AvailableActionIds = AvailableActions(ended)
            };
        }

        int index = practice.ComparisonIndex;
        int left = practice.Values[index];
        int right = practice.Values[index + 1];
        bool shouldSwap = left > right;
        bool wantsSwap = actionId == ChallengeActionIds.Swap;

        if (wantsSwap != shouldSwap)
        {
            return new ChallengeActionResult
            {
                Accepted = false,
                State = practice,
                Feedback = "Not that action.",
                AvailableActionIds = AvailableActions(practice),
                VisualHint = new ChallengeVisualHint
                {
                    CompareIndexA = index,
                    CompareIndexB = index + 1,
                    DidSwap = false,
                    SortedFromEndCount = passIndex,
                    ValuesAfter = practice.Values.ToArray()
                }
            };
        }

        int[] nextValues = practice.Values.ToArray();
        if (shouldSwap)
        {
            nextValues[index] = right;
            nextValues[index + 1] = left;
        }

        int comparisons = practice.ComparisonsInCurrentPass + 1;
        int nextIndex = index + 1;
        bool atEnd = nextIndex >= passLimit;

        var history = practice.History.ToList();
        history.Add(new ChallengeActionRecord
        {
            Sequence = history.Count,
            PassNumber = practice.PassNumber,
            ActionId = actionId,
            IndexA = index,
            IndexB = index + 1,
            ValueA = left,
            ValueB = right,
            StateAfter = nextValues.ToArray(),
            ExpectedComparisonsInPass = passLimit,
            ActualComparisonsInPass = comparisons
        });

        var next = new AdjacentSortPracticeState
        {
            Values = nextValues,
            ComparisonIndex = atEnd ? index : nextIndex,
            PassNumber = practice.PassNumber,
            ComparisonsInCurrentPass = comparisons,
            AtNaturalPassEnd = atEnd,
            History = history,
            ArrayLength = n
        };

        return new ChallengeActionResult
        {
            Accepted = true,
            State = next,
            AvailableActionIds = AvailableActions(next),
            VisualHint = new ChallengeVisualHint
            {
                CompareIndexA = index,
                CompareIndexB = index + 1,
                DidSwap = shouldSwap,
                SortedFromEndCount = passIndex,
                ValuesAfter = nextValues
            }
        };
    }

    private static ChallengeActionResult ApplyNextPass(AdjacentSortPracticeState practice)
    {
        int n = practice.ArrayLength;
        int passIndex = practice.PassNumber - 1;
        int expected = Math.Max(0, n - 1 - passIndex);
        bool premature = practice.ComparisonsInCurrentPass < expected;
        int maxPass = Math.Max(1, n - 1);

        if (practice.PassNumber >= maxPass && practice.AtNaturalPassEnd)
        {
            return new ChallengeActionResult
            {
                Accepted = false,
                State = practice,
                AvailableActionIds = AvailableActions(practice)
            };
        }

        var history = practice.History.ToList();
        history.Add(new ChallengeActionRecord
        {
            Sequence = history.Count,
            PassNumber = practice.PassNumber,
            ActionId = ChallengeActionIds.NextPass,
            StateAfter = practice.Values.ToArray(),
            WasPrematurePassBoundary = premature,
            ExpectedComparisonsInPass = expected,
            ActualComparisonsInPass = practice.ComparisonsInCurrentPass
        });

        int nextPass = Math.Min(maxPass, practice.PassNumber + 1);
        bool noMorePairs = n - 1 - (nextPass - 1) <= 0;
        var next = new AdjacentSortPracticeState
        {
            Values = practice.Values.ToArray(),
            ComparisonIndex = 0,
            PassNumber = nextPass,
            ComparisonsInCurrentPass = 0,
            AtNaturalPassEnd = noMorePairs,
            History = history,
            ArrayLength = n
        };

        return new ChallengeActionResult
        {
            Accepted = true,
            State = next,
            AvailableActionIds = AvailableActions(next),
            VisualHint = new ChallengeVisualHint
            {
                SortedFromEndCount = nextPass - 1,
                ValuesAfter = next.Values.ToArray()
            }
        };
    }

    private static ChallengeActionResult ApplyComplete(AdjacentSortPracticeState practice)
    {
        var history = practice.History.ToList();
        history.Add(new ChallengeActionRecord
        {
            Sequence = history.Count,
            PassNumber = practice.PassNumber,
            ActionId = ChallengeActionIds.Complete,
            StateAfter = practice.Values.ToArray(),
            ActualComparisonsInPass = practice.ComparisonsInCurrentPass
        });

        var next = Clone(practice, history: history);
        return new ChallengeActionResult
        {
            Accepted = true,
            State = next,
            ChallengeCompleted = true,
            AvailableActionIds = Array.Empty<string>(),
            VisualHint = new ChallengeVisualHint
            {
                ValuesAfter = next.Values.ToArray(),
                SortedFromEndCount = IsSorted(next.Values) ? next.ArrayLength : next.PassNumber - 1
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
        var practice = (AdjacentSortPracticeState)state;
        int totalPasses = Math.Max(1, practice.ArrayLength - 1);
        bool highlight = !practice.AtNaturalPassEnd && practice.Values.Count >= 2;

        return new ChallengePresentation
        {
            Title = definition.PlayerTitle,
            Prompt = definition.PlayerDescription,
            Values = practice.Values.ToArray(),
            HighlightIndexA = highlight ? practice.ComparisonIndex : null,
            HighlightIndexB = highlight ? practice.ComparisonIndex + 1 : null,
            SortedFromEndCount = Math.Max(0, practice.PassNumber - 1),
            AvailableActionIds = AvailableActions(practice),
            Feedback = feedback,
            NarrativeCue = narrativeCue,
            ConceptHidden = false,
            IsComplete = false,
            Reveal = null,
            PassNumber = practice.PassNumber,
            TotalPasses = totalPasses,
            ShowGuidance = false,
            IsPassTransition = false
        };
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

    private static AdjacentSortPracticeState Clone(
        AdjacentSortPracticeState source,
        bool? atNaturalPassEnd = null,
        IReadOnlyList<ChallengeActionRecord>? history = null) =>
        new()
        {
            Values = source.Values.ToArray(),
            ComparisonIndex = source.ComparisonIndex,
            PassNumber = source.PassNumber,
            ComparisonsInCurrentPass = source.ComparisonsInCurrentPass,
            AtNaturalPassEnd = atNaturalPassEnd ?? source.AtNaturalPassEnd,
            History = history ?? source.History.ToArray(),
            ArrayLength = source.ArrayLength
        };

    private static IReadOnlyList<string> AvailableActions(AdjacentSortPracticeState practice)
    {
        var actions = new List<string>();
        if (!practice.AtNaturalPassEnd && practice.Values.Count >= 2)
        {
            actions.Add(ChallengeActionIds.Swap);
            actions.Add(ChallengeActionIds.Keep);
        }

        int maxPass = Math.Max(1, practice.ArrayLength - 1);
        if (!(practice.PassNumber >= maxPass && practice.AtNaturalPassEnd))
        {
            actions.Add(ChallengeActionIds.NextPass);
        }

        actions.Add(ChallengeActionIds.Complete);
        return actions;
    }

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

/// <summary>Validates unguided practice history against Bubble Sort process expectations.</summary>
public static class AdjacentSortPracticeValidator
{
    public static ChallengeValidationResult Validate(AdjacentSortPracticeState state)
    {
        var details = new List<string>();
        int n = state.ArrayLength;
        int expectedPasses = Math.Max(1, n - 1);
        bool sorted = IsSorted(state.Values);
        if (!sorted)
        {
            details.Add("The shipment is not fully ordered.");
        }

        IReadOnlyList<ChallengeActionRecord> compareActions = state.History
            .Where(h => h.ActionId is ChallengeActionIds.Swap or ChallengeActionIds.Keep)
            .ToArray();

        int expectedCompares = expectedPasses * (expectedPasses + 1) / 2; // sum 1..n-1 = (n-1)n/2 wait
        // Passes 0..n-2 have (n-1), (n-2), ... 1 compares → sum = (n-1)*n/2
        expectedCompares = n * (n - 1) / 2;
        if (compareActions.Count < expectedCompares)
        {
            details.Add("Some comparisons were skipped.");
        }

        bool anyPremature = state.History.Any(h =>
            h.ActionId == ChallengeActionIds.NextPass && h.WasPrematurePassBoundary == true);
        if (anyPremature)
        {
            details.Add("A pass ended before all neighboring comparisons were finished.");
        }

        // Require correct number of NEXT PASS decisions between passes (n-2 for n>=2),
        // or allow finishing on last pass without an extra NEXT PASS after the final compares.
        int nextPassCount = state.History.Count(h => h.ActionId == ChallengeActionIds.NextPass);
        int minNextPasses = Math.Max(0, expectedPasses - 1);
        if (nextPassCount < minNextPasses)
        {
            details.Add("Not enough passes were completed.");
        }

        // Every recorded compare must match Bubble Sort ordering for its pass/index.
        if (!ComparesFollowBubbleOrder(state.History, n))
        {
            details.Add("The comparison sequence did not follow the sorting process.");
        }

        bool success = sorted
            && compareActions.Count >= expectedCompares
            && !anyPremature
            && nextPassCount >= minNextPasses
            && ComparesFollowBubbleOrder(state.History, n);

        return new ChallengeValidationResult
        {
            IsSuccess = success,
            SummaryMessage = success
                ? "You figured it out."
                : "Your sorting process wasn't complete.",
            DetailMessages = details
        };
    }

    private static bool ComparesFollowBubbleOrder(IReadOnlyList<ChallengeActionRecord> history, int n)
    {
        // Track expected index per pass from compare actions only.
        var comparesByPass = history
            .Where(h => h.ActionId is ChallengeActionIds.Swap or ChallengeActionIds.Keep)
            .GroupBy(h => h.PassNumber)
            .OrderBy(g => g.Key);

        foreach (IGrouping<int, ChallengeActionRecord> group in comparesByPass)
        {
            int passNumber = group.Key;
            int passIndex = passNumber - 1;
            int expectedCount = n - 1 - passIndex;
            ChallengeActionRecord[] compares = group.ToArray();
            if (compares.Length > expectedCount)
            {
                return false;
            }

            for (int i = 0; i < compares.Length; i++)
            {
                if (compares[i].IndexA != i || compares[i].IndexB != i + 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

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
