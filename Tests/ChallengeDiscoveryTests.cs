using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Challenges;
using PipelineArchitect.Challenges.BubbleSort;
using PipelineArchitect.Challenges.Core;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation;
using PipelineArchitect.Simulation.Pipeline;
using Xunit;

namespace PipelineArchitect.Tests;

/// <summary>
/// Discovery-first interactive challenge framework + adjacent-sort (Bubble Sort) rules.
/// </summary>
public sealed class ChallengeDiscoveryTests : IDisposable
{
    public ChallengeDiscoveryTests()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    public void Dispose()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    [Fact]
    public void CorrectSwap_AdvancesState()
    {
        ChallengeSession session = CreateSession(new[] { 5, 4, 3 });
        ChallengePresentation before = session.GetPresentation();
        Assert.True(before.ConceptHidden);
        Assert.DoesNotContain("Bubble", before.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bubble", before.Prompt, StringComparison.OrdinalIgnoreCase);

        ChallengeActionResult result = session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        Assert.True(result.Accepted);
        Assert.Equal(new[] { 4, 5, 3 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);
        Assert.Equal(1, ((AdjacentSortDiscoveryState)session.CurrentState).ComparisonIndex);
    }

    [Fact]
    public void IncorrectKeep_DoesNotMutateOrAdvance()
    {
        ChallengeSession session = CreateSession(new[] { 5, 4, 3 });
        var stateBefore = (AdjacentSortDiscoveryState)session.CurrentState;

        ChallengeActionResult result = session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Keep });
        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Feedback));
        Assert.Contains("larger", result.Feedback!, StringComparison.OrdinalIgnoreCase);

        var stateAfter = (AdjacentSortDiscoveryState)session.CurrentState;
        Assert.Equal(stateBefore.Values, stateAfter.Values);
        Assert.Equal(stateBefore.ComparisonIndex, stateAfter.ComparisonIndex);
        Assert.False(session.IsComplete);
        Assert.True(session.ConceptHidden);
    }

    [Fact]
    public void IncorrectSwap_WhenAlreadyOrdered_Rejected()
    {
        ChallengeSession session = CreateSession(new[] { 1, 2, 3 });
        // Already sorted initial — CreateInitialState marks fully sorted.
        // Use [1, 3, 2] where first pair should KEEP.
        session = CreateSession(new[] { 1, 3, 2 });
        ChallengeActionResult result = session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        Assert.False(result.Accepted);
        Assert.Equal(new[] { 1, 3, 2 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);
    }

    [Fact]
    public void FullDiscovery_CompletesAndRevealsConcept()
    {
        ChallengeSession session = CreateSession(new[] { 3, 2, 1 });
        Assert.True(session.ConceptHidden);
        Assert.Null(session.GetPresentation().Reveal);

        PlayOptimalBubbleSort(session);

        Assert.True(session.IsComplete);
        Assert.False(session.ConceptHidden);
        Assert.NotNull(session.Reveal);
        Assert.Equal("Bubble Sort", session.Reveal!.ConceptName);
        Assert.Contains("neighbor", session.Reveal.Explanation, StringComparison.OrdinalIgnoreCase);

        ChallengePresentation after = session.GetPresentation();
        Assert.False(after.ConceptHidden);
        Assert.NotNull(after.Reveal);
        Assert.Equal(new[] { 1, 2, 3 }, after.Values);
    }

    [Fact]
    public void Restart_ResetsCompletelyAndHidesConcept()
    {
        ChallengeSession session = CreateSession(new[] { 2, 1 });
        session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        Assert.True(session.IsComplete);
        Assert.False(session.ConceptHidden);

        session.Restart();
        Assert.False(session.IsComplete);
        Assert.True(session.ConceptHidden);
        Assert.Null(session.Reveal);
        Assert.Equal(new[] { 2, 1 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);
        Assert.Equal(0, ((AdjacentSortDiscoveryState)session.CurrentState).ComparisonIndex);
        Assert.DoesNotContain("Bubble", session.GetPresentation().Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Isolation_DoesNotMutateLevelDataOrEngine()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath("intern_01_bubblesort.json"));
        int[] originalInput = level.InputStream.ToArray();
        var engine = new SimulationEngine(level);
        int cpuBefore = engine.CurrentCPUCycles;

        ChallengeSession? session = ChallengeFactory.TryCreate(level);
        Assert.NotNull(session);
        PlayOptimalBubbleSort(session!);

        Assert.Equal(originalInput, level.InputStream);
        Assert.Equal(cpuBefore, engine.CurrentCPUCycles);
        Assert.Null(engine.LastTrace);
        Assert.Empty(engine.PipelineRoutes);
    }

    [Fact]
    public void AlgorithmConsistency_MatchesBubbleSortMachineOrdering()
    {
        int[] input = { 5, 4, 3, 2, 1 };
        ChallengeSession session = CreateSession(input);
        PlayOptimalBubbleSort(session);
        int[] discoveryResult = session.GetPresentation().Values.ToArray();

        LevelData level = new()
        {
            LevelId = "consistency",
            Tier = LevelTier.Intern,
            Title = "Consistency",
            Description = "Consistency",
            InputStream = input.ToList(),
            ExpectedOutput = new[] { 1, 2, 3, 4, 5 },
            Budgets = new LevelBudgets { MaxCPUCycles = 100, MaxRAMSlots = 2 },
            AllowedToolbox = new List<string> { "Bubble_Sort_Machine" }
        };
        var engine = new SimulationEngine(level);
        SimulationResult machine = engine.RunLinearBubbleSortPipeline();
        Assert.True(machine.IsSuccess);
        Assert.Equal(machine.FinalOutput, discoveryResult);
    }

    [Fact]
    public void LevelJson_LoadsInteractiveChallengeWithoutHardcodedLevelIdBranch()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath("intern_01_bubblesort.json"));
        Assert.NotNull(level.InteractiveChallenge);
        Assert.True(level.InteractiveChallenge!.Enabled);
        Assert.Equal(ChallengeIds.AdjacentSortDiscovery, level.InteractiveChallenge.ChallengeId);
        Assert.Equal(new[] { 5, 1, 4, 2, 8 }, level.InputStream);
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, level.InteractiveChallenge.InitialValues);

        ChallengeSession? session = ChallengeFactory.TryCreate(level);
        Assert.NotNull(session);
        Assert.Equal("Sort the Shipment", session!.Definition.PlayerTitle);
        Assert.DoesNotContain("Bubble", session.GetPresentation().Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Presentation_HidesConceptNameUntilComplete()
    {
        ChallengeSession session = CreateSession(new[] { 2, 1 });
        ChallengePresentation mid = session.GetPresentation();
        Assert.True(mid.ConceptHidden);
        Assert.Null(mid.Reveal);
        Assert.DoesNotContain("Bubble Sort", mid.Title);
        Assert.DoesNotContain("Bubble Sort", mid.Prompt);
        Assert.DoesNotContain("O(n", mid.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pass", mid.Prompt, StringComparison.OrdinalIgnoreCase);

        session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        Assert.False(session.ConceptHidden);
        Assert.Contains("Bubble Sort", session.Reveal!.ConceptName);
    }

    [Fact]
    public void Evaluate_DoesNotMutateChallengeState()
    {
        ChallengeSession session = CreateSession(new[] { 5, 4, 3 });
        var before = (AdjacentSortDiscoveryState)session.CurrentState;
        int[] snapshot = before.Values.ToArray();
        int index = before.ComparisonIndex;

        // Preview-style evaluation of both actions — no commit.
        ChallengeActionResult keepEval = session.Evaluate(new ChallengeAction { ActionId = ChallengeActionIds.Keep });
        ChallengeActionResult swapEval = session.Evaluate(new ChallengeAction { ActionId = ChallengeActionIds.Swap });

        Assert.False(keepEval.Accepted);
        Assert.True(swapEval.Accepted);
        Assert.Equal(snapshot, ((AdjacentSortDiscoveryState)session.CurrentState).Values);
        Assert.Equal(index, ((AdjacentSortDiscoveryState)session.CurrentState).ComparisonIndex);
        Assert.False(session.IsComplete);
        Assert.True(session.ConceptHidden);
        Assert.False(session.IsInteractionLocked);
    }

    [Fact]
    public void InteractionLock_BlocksTryAction_UntilCommitOrEnd()
    {
        ChallengeSession session = CreateSession(new[] { 5, 4, 3 });
        Assert.True(session.BeginAuthoritativeTransition());
        Assert.True(session.IsInteractionLocked);

        ChallengeActionResult blocked = session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        Assert.False(blocked.Accepted);
        Assert.Equal(new[] { 5, 4, 3 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);

        // Evaluate still works while locked (preview / pre-commit validation).
        ChallengeActionResult evaluated = session.Evaluate(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        Assert.True(evaluated.Accepted);
        Assert.Equal(new[] { 5, 4, 3 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);

        ChallengeActionResult committed = session.CommitWhileLocked(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        Assert.True(committed.Accepted);
        Assert.False(session.IsInteractionLocked);
        Assert.Equal(new[] { 4, 5, 3 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);
    }

    [Fact]
    public void IncorrectAction_WhileLocked_EndsWithoutAdvancing()
    {
        ChallengeSession session = CreateSession(new[] { 5, 4, 3 });
        Assert.True(session.BeginAuthoritativeTransition());

        ChallengeActionResult evaluated = session.Evaluate(new ChallengeAction { ActionId = ChallengeActionIds.Keep });
        Assert.False(evaluated.Accepted);

        session.PublishRejectionFeedback(evaluated.Feedback);
        session.EndAuthoritativeTransition();

        Assert.False(session.IsInteractionLocked);
        Assert.Equal(new[] { 5, 4, 3 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);
        Assert.Equal(0, ((AdjacentSortDiscoveryState)session.CurrentState).ComparisonIndex);
        Assert.Contains("larger", session.GetPresentation().Feedback ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keep_PreservesValues_AdvancesComparison()
    {
        ChallengeSession session = CreateSession(new[] { 1, 3, 2 });
        Assert.True(session.BeginAuthoritativeTransition());
        ChallengeActionResult evaluated = session.Evaluate(new ChallengeAction { ActionId = ChallengeActionIds.Keep });
        Assert.True(evaluated.Accepted);
        Assert.Equal(new[] { 1, 3, 2 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);

        ChallengeActionResult committed = session.CommitWhileLocked(new ChallengeAction { ActionId = ChallengeActionIds.Keep });
        Assert.True(committed.Accepted);
        Assert.Equal(new[] { 1, 3, 2 }, ((AdjacentSortDiscoveryState)session.CurrentState).Values);
        Assert.Equal(1, ((AdjacentSortDiscoveryState)session.CurrentState).ComparisonIndex);
    }

    private static ChallengeSession CreateSession(int[] values)
    {
        var config = new InteractiveChallengeConfig
        {
            Enabled = true,
            ChallengeId = ChallengeIds.AdjacentSortDiscovery,
            PlayerTitle = "Sort the Shipment",
            PlayerDescription = "The packages are out of order.\nCan you figure out how to move them into the correct order?",
            InitialValues = values,
            RevealConceptAfterCompletion = true,
            ConceptName = "Bubble Sort",
            ConceptExplanation =
                "Bubble Sort repeatedly compares neighboring values.\n" +
                "When two values are in the wrong order, they are swapped.\n" +
                "Each pass moves the largest remaining value toward the end.",
            MachineBlockId = "Bubble_Sort_Machine"
        };

        ChallengeSession? session = ChallengeFactory.TryCreate(config);
        Assert.NotNull(session);
        return session!;
    }

    /// <summary>Drives optimal Bubble Sort decisions (swap iff left &gt; right).</summary>
    private static void PlayOptimalBubbleSort(ChallengeSession session)
    {
        int guard = 0;
        while (!session.IsComplete && guard++ < 256)
        {
            var state = (AdjacentSortDiscoveryState)session.CurrentState;
            if (state.IsFullySorted)
            {
                break;
            }

            int left = state.Values[state.ComparisonIndex];
            int right = state.Values[state.ComparisonIndex + 1];
            string action = left > right ? ChallengeActionIds.Swap : ChallengeActionIds.Keep;
            ChallengeActionResult result = session.TryAction(new ChallengeAction { ActionId = action });
            Assert.True(result.Accepted, result.Feedback);
        }

        Assert.True(session.IsComplete);
    }

    private static string ResolveLevelPath(string fileName)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = System.IO.Path.Combine(dir, "levels", fileName);
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            candidate = System.IO.Path.Combine(dir, fileName);
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException($"Could not resolve level file '{fileName}'.");
    }
}
