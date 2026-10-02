using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Challenges;
using PipelineArchitect.Challenges.BubbleSort;
using PipelineArchitect.Challenges.Core;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation;
using Xunit;

namespace PipelineArchitect.Tests;

/// <summary>
/// Challenge 1 pass display + Challenge 2 unguided practice / validation / recovery.
/// </summary>
public sealed class ChallengePracticeTests : IDisposable
{
    public ChallengePracticeTests()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    public void Dispose()
    {
        SimulationEvents.ClearAllSubscribers();
    }

    [Fact]
    public void Guided_PassCounter_StartsAtOne_AndUpdatesOnPassBoundary()
    {
        ChallengeSession session = CreateGuided(new[] { 3, 2, 1 });
        ChallengePresentation start = session.GetPresentation();
        Assert.Equal(1, start.PassNumber);
        Assert.Equal(2, start.TotalPasses);
        Assert.Equal("PASS 1 / 2", start.PassLabel);

        // First pass for n=3 has 2 compares.
        Assert.True(session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap }).Accepted);
        Assert.Equal(1, session.GetPresentation().PassNumber);
        Assert.True(session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap }).Accepted);

        // After first pass completes, guided auto-advances to pass 2.
        Assert.Equal(2, session.GetPresentation().PassNumber);
        Assert.Equal("PASS 2 / 2", session.GetPresentation().PassLabel);
    }

    [Fact]
    public void Practice_HidesGuidance_AndExposesNextPassComplete()
    {
        ChallengeSession practice = CreatePractice(new[] { 3, 2, 1 });
        ChallengePresentation p = practice.GetPresentation();
        Assert.Equal("Sort It Yourself", p.Title);
        Assert.False(p.ShowGuidance);
        Assert.DoesNotContain("Bubble", p.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("compare these", p.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ChallengeActionIds.Swap, p.AvailableActionIds);
        Assert.Contains(ChallengeActionIds.NextPass, p.AvailableActionIds);
        Assert.Contains(ChallengeActionIds.Complete, p.AvailableActionIds);
        Assert.Equal(1, p.PassNumber);
    }

    [Fact]
    public void Practice_NextPass_RecordsPrematureDecision_WithoutImmediateFailure()
    {
        ChallengeSession practice = CreatePractice(new[] { 3, 2, 1 });
        ChallengeActionResult next = practice.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.NextPass });
        Assert.True(next.Accepted);

        var state = (AdjacentSortPracticeState)practice.CurrentState;
        Assert.Equal(2, state.PassNumber);
        ChallengeActionRecord boundary = Assert.Single(state.History);
        Assert.Equal(ChallengeActionIds.NextPass, boundary.ActionId);
        Assert.True(boundary.WasPrematurePassBoundary);
        Assert.Equal(0, boundary.ActualComparisonsInPass);
        Assert.Equal(2, boundary.ExpectedComparisonsInPass);
    }

    [Fact]
    public void Practice_CorrectProcess_SucceedsValidation()
    {
        ChallengeSession practice = CreatePractice(new[] { 3, 2, 1 });
        PlayOptimalPractice(practice);
        var state = (AdjacentSortPracticeState)practice.CurrentState;
        ChallengeValidationResult result = AdjacentSortPracticeValidator.Validate(state);
        Assert.True(result.IsSuccess, string.Join("; ", result.DetailMessages));
        Assert.Equal(new[] { 1, 2, 3 }, state.Values);
    }

    [Fact]
    public void Practice_IncompleteSorting_FailsValidation()
    {
        ChallengeSession practice = CreatePractice(new[] { 3, 2, 1 });
        practice.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Swap });
        practice.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Complete });

        var state = (AdjacentSortPracticeState)practice.CurrentState;
        ChallengeValidationResult result = AdjacentSortPracticeValidator.Validate(state);
        Assert.False(result.IsSuccess);
        Assert.Contains("not fully ordered", result.DetailMessages[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Practice_PrematurePass_FailsAtComplete()
    {
        ChallengeSession practice = CreatePractice(new[] { 3, 2, 1 });
        practice.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.NextPass });
        // Even if later somehow sorted incorrectly — premature alone fails.
        ChallengeValidationResult result = AdjacentSortPracticeValidator.Validate(
            (AdjacentSortPracticeState)practice.CurrentState);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.DetailMessages, d => d.Contains("pass ended", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Progression_RetryResetsPractice_ReplayReturnsToGuided()
    {
        InteractiveChallengeConfig config = CreateConfig(new[] { 3, 2, 1 }, withPractice: true);
        var progression = new ChallengeProgressionController(config);
        Assert.Equal(ChallengeFlowPhase.Guided, progression.Phase);
        Assert.Equal(1, progression.CurrentExerciseNumber);

        PlayOptimalGuided(progression.GuidedSession);
        progression.NotifyGuidedCompleted();
        Assert.Equal(ChallengeFlowPhase.ConceptReveal, progression.Phase);
        progression.ContinueFromConceptReveal();
        Assert.Equal(ChallengeFlowPhase.MachineReveal, progression.Phase);
        progression.ContinueFromMachineReveal();
        Assert.Equal(ChallengeFlowPhase.ExerciseComplete, progression.Phase);
        Assert.True(progression.IsExerciseCompleted(1));
        Assert.False(progression.IsProblemComplete);

        progression.ContinueAfterExerciseComplete();
        Assert.Equal(ChallengeFlowPhase.Unguided, progression.Phase);
        Assert.Equal(2, progression.CurrentExerciseNumber);
        Assert.NotNull(progression.PracticeSession);

        progression.PracticeSession!.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.NextPass });
        Assert.Equal(2, ((AdjacentSortPracticeState)progression.PracticeSession.CurrentState).PassNumber);

        progression.RetryPractice();
        Assert.Equal(ChallengeFlowPhase.Unguided, progression.Phase);
        Assert.Equal(1, ((AdjacentSortPracticeState)progression.PracticeSession!.CurrentState).PassNumber);
        Assert.Empty(((AdjacentSortPracticeState)progression.PracticeSession.CurrentState).History);

        progression.ReplayGuided();
        Assert.Equal(ChallengeFlowPhase.Guided, progression.Phase);
        Assert.Null(progression.PracticeSession);
        Assert.False(progression.GuidedSession.IsComplete);
        Assert.Equal(1, progression.GuidedSession.GetPresentation().PassNumber);
    }

    [Fact]
    public void Practice_DoesNotMutateLevelDataOrEngine()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath("intern_01_bubblesort.json"));
        int[] original = level.InputStream.ToArray();
        var engine = new SimulationEngine(level);
        int cpu = engine.CurrentCPUCycles;

        ChallengeProgressionController? progression = ChallengeFactory.TryCreateProgression(level);
        Assert.NotNull(progression);
        PlayOptimalGuided(progression!.GuidedSession);
        progression.NotifyGuidedCompleted();
        progression.ContinueFromConceptReveal();
        progression.ContinueFromMachineReveal();
        progression.ContinueAfterExerciseComplete();
        PlayOptimalPractice(progression.PracticeSession!);
        ChallengeValidationResult result = progression.SubmitPracticeComplete();
        Assert.True(result.IsSuccess);
        Assert.Equal(ChallengeFlowPhase.ExerciseComplete, progression.Phase);
        Assert.False(progression.IsProblemComplete);

        Assert.Equal(original, level.InputStream);
        Assert.Equal(cpu, engine.CurrentCPUCycles);
        Assert.Null(engine.LastTrace);
    }

    private static void PlayOptimalGuided(ChallengeSession session)
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
            Assert.True(session.TryAction(new ChallengeAction { ActionId = action }).Accepted);
        }

        Assert.True(session.IsComplete);
    }

    private static void PlayOptimalPractice(ChallengeSession session)
    {
        var state = (AdjacentSortPracticeState)session.CurrentState;
        int n = state.ArrayLength;
        int totalPasses = n - 1;

        for (int pass = 1; pass <= totalPasses; pass++)
        {
            int expected = n - pass;
            for (int i = 0; i < expected; i++)
            {
                state = (AdjacentSortPracticeState)session.CurrentState;
                int left = state.Values[state.ComparisonIndex];
                int right = state.Values[state.ComparisonIndex + 1];
                string action = left > right ? ChallengeActionIds.Swap : ChallengeActionIds.Keep;
                Assert.True(session.TryAction(new ChallengeAction { ActionId = action }).Accepted, $"pass {pass} compare {i}");
            }

            if (pass < totalPasses)
            {
                Assert.True(session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.NextPass }).Accepted);
            }
        }

        Assert.True(session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Complete }).Accepted);
    }

    private static ChallengeSession CreateGuided(int[] values)
    {
        ChallengeSession? session = ChallengeFactory.TryCreate(CreateConfig(values, withPractice: false));
        Assert.NotNull(session);
        return session!;
    }

    private static ChallengeSession CreatePractice(int[] values) =>
        ChallengeFactory.CreatePracticeSession(CreateConfig(values, withPractice: true));

    private static InteractiveChallengeConfig CreateConfig(int[] values, bool withPractice) =>
        new()
        {
            Enabled = true,
            ChallengeId = ChallengeIds.AdjacentSortDiscovery,
            PlayerTitle = "Sort the Shipment",
            PlayerDescription = "The packages are out of order.",
            InitialValues = values,
            RevealConceptAfterCompletion = true,
            ConceptName = "Bubble Sort",
            ConceptExplanation = "Neighbor compares and swaps.",
            MachineBlockId = "Bubble_Sort_Machine",
            PracticeChallenge = withPractice
                ? new PracticeChallengeConfig
                {
                    Enabled = true,
                    PlayerTitle = "Sort It Yourself",
                    PlayerDescription = "You know how this works. Now sort the shipment yourself.",
                    InitialValues = values
                }
                : new PracticeChallengeConfig { Enabled = false },
            MasteryChallenge = withPractice
                ? new PracticeChallengeConfig
                {
                    Enabled = true,
                    PlayerTitle = "Final Challenge",
                    PlayerDescription = "Show that you can do it on a new shipment.",
                    InitialValues = ChallengeFactory.DefaultMasteryValues
                }
                : new PracticeChallengeConfig { Enabled = false }
        };

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

            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException($"Could not resolve '{fileName}'.");
    }
}
