using System;
using System.Linq;
using PipelineArchitect.Challenges;
using PipelineArchitect.Challenges.BubbleSort;
using PipelineArchitect.Challenges.Core;
using PipelineArchitect.Data;
using Xunit;

namespace PipelineArchitect.Tests;

/// <summary>
/// Authoritative three-exercise problem progression (Learn → Practice → Prove).
/// </summary>
public sealed class ChallengeProblemProgressionTests
{
    [Fact]
    public void Problem_StartsAtExercise1_CannotCompleteEarly()
    {
        var progression = new ChallengeProgressionController(CreateConfig());
        Assert.Equal(1, progression.CurrentExerciseNumber);
        Assert.Equal(ChallengeFlowPhase.Guided, progression.Phase);
        Assert.False(progression.IsProblemComplete);
        Assert.False(progression.IsExerciseCompleted(1));
        Assert.False(progression.IsExerciseCompleted(2));
        Assert.False(progression.IsExerciseCompleted(3));
    }

    [Fact]
    public void FullProblem_ThreeExercises_ThenProblemComplete()
    {
        var progression = new ChallengeProgressionController(CreateConfig());

        CompleteExercise1(progression);
        Assert.True(progression.IsExerciseCompleted(1));
        Assert.False(progression.IsProblemComplete);
        Assert.Equal(ChallengeFlowPhase.ExerciseComplete, progression.Phase);

        progression.ContinueAfterExerciseComplete();
        Assert.Equal(ChallengeFlowPhase.Unguided, progression.Phase);
        Assert.Equal(2, progression.CurrentExerciseNumber);

        PlayOptimal(progression.PracticeSession!);
        ChallengeValidationResult ex2 = progression.SubmitActiveUnguidedComplete();
        Assert.True(ex2.IsSuccess);
        Assert.True(progression.IsExerciseCompleted(2));
        Assert.False(progression.IsProblemComplete);
        Assert.Equal(ChallengeFlowPhase.ExerciseComplete, progression.Phase);

        progression.ContinueAfterExerciseComplete();
        Assert.Equal(ChallengeFlowPhase.Mastery, progression.Phase);
        Assert.Equal(3, progression.CurrentExerciseNumber);
        Assert.NotNull(progression.MasterySession);
        Assert.Equal(
            ChallengeFactory.DefaultMasteryValues,
            progression.MasterySession!.Definition.InitialValues);

        PlayOptimal(progression.MasterySession);
        ChallengeValidationResult ex3 = progression.SubmitActiveUnguidedComplete();
        Assert.True(ex3.IsSuccess);
        Assert.True(progression.IsExerciseCompleted(3));
        Assert.Equal(ChallengeFlowPhase.ExerciseComplete, progression.Phase);
        Assert.False(progression.IsProblemComplete);

        progression.ContinueAfterExerciseComplete();
        Assert.True(progression.IsProblemComplete);
        Assert.Equal(ChallengeFlowPhase.ProblemComplete, progression.Phase);
    }

    [Fact]
    public void Exercise2Failure_DoesNotCompleteProblem_OrUnlockExercise3()
    {
        var progression = new ChallengeProgressionController(CreateConfig());
        CompleteExercise1(progression);
        progression.ContinueAfterExerciseComplete();

        progression.PracticeSession!.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Complete });
        // Force validation fail path via Submit without finishing.
        // Prefer: next-pass premature then complete.
        progression.RetryCurrentExercise();
        progression.PracticeSession!.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.NextPass });
        ChallengeValidationResult result = progression.SubmitActiveUnguidedComplete();
        Assert.False(result.IsSuccess);
        Assert.Equal(ChallengeFlowPhase.Failure, progression.Phase);
        Assert.False(progression.IsExerciseCompleted(2));
        Assert.False(progression.IsProblemComplete);
        Assert.Null(progression.MasterySession);
    }

    [Fact]
    public void CannotSkipToExercise3_WithoutExercise2()
    {
        var progression = new ChallengeProgressionController(CreateConfig());
        CompleteExercise1(progression);
        // Trying to jump by calling StartMastery indirectly is not public.
        // ContinueAfterExerciseComplete from exercise 1 must land on exercise 2.
        progression.ContinueAfterExerciseComplete();
        Assert.Equal(ProblemExerciseSlot.SortItYourself, progression.CurrentExercise);
        Assert.Equal(ChallengeFlowPhase.Unguided, progression.Phase);
        Assert.Null(progression.MasterySession);
    }

    [Fact]
    public void MasteryDataset_DiffersFromGuidedAndPractice()
    {
        LevelData level = LevelDataLoader.FromFile(ResolveLevelPath("intern_01_bubblesort.json"));
        Assert.NotNull(level.InteractiveChallenge?.MasteryChallenge);
        int[] guided = level.InteractiveChallenge!.InitialValues.ToArray();
        int[] practice = level.InteractiveChallenge.PracticeChallenge!.InitialValues!.ToArray();
        int[] mastery = level.InteractiveChallenge.MasteryChallenge!.InitialValues!.ToArray();

        Assert.NotEqual(guided, mastery);
        Assert.NotEqual(practice, mastery);

        ChallengeSession session = ChallengeFactory.CreateMasterySession(level.InteractiveChallenge);
        Assert.Equal(mastery, session.Definition.InitialValues);
        Assert.Equal("Final Challenge", session.Definition.PlayerTitle);
        Assert.False(session.Definition.IsGuided);
    }

    [Fact]
    public void ReplayExercise2_ClearsLaterCompletions()
    {
        var progression = new ChallengeProgressionController(CreateConfig());
        CompleteExercise1(progression);
        progression.ContinueAfterExerciseComplete();
        PlayOptimal(progression.PracticeSession!);
        Assert.True(progression.SubmitActiveUnguidedComplete().IsSuccess);
        progression.ContinueAfterExerciseComplete();
        Assert.Equal(3, progression.CurrentExerciseNumber);

        progression.ReplayExercise(ProblemExerciseSlot.SortItYourself);
        Assert.Equal(2, progression.CurrentExerciseNumber);
        Assert.True(progression.IsExerciseCompleted(1));
        Assert.False(progression.IsExerciseCompleted(2));
        Assert.False(progression.IsExerciseCompleted(3));
        Assert.False(progression.IsProblemComplete);
        Assert.Equal(ChallengeFlowPhase.Unguided, progression.Phase);
    }

    private static void CompleteExercise1(ChallengeProgressionController progression)
    {
        PlayOptimalGuided(progression.GuidedSession);
        progression.NotifyGuidedCompleted();
        progression.ContinueFromConceptReveal();
        progression.ContinueFromMachineReveal();
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

    private static void PlayOptimal(ChallengeSession session)
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
                Assert.True(session.TryAction(new ChallengeAction { ActionId = action }).Accepted);
            }

            if (pass < totalPasses)
            {
                Assert.True(session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.NextPass }).Accepted);
            }
        }

        Assert.True(session.TryAction(new ChallengeAction { ActionId = ChallengeActionIds.Complete }).Accepted);
    }

    private static InteractiveChallengeConfig CreateConfig() =>
        new()
        {
            Enabled = true,
            ChallengeId = ChallengeIds.AdjacentSortDiscovery,
            PlayerTitle = "Sort the Shipment",
            PlayerDescription = "Packages are out of order.",
            InitialValues = new[] { 3, 2, 1 },
            RevealConceptAfterCompletion = true,
            ConceptName = "Bubble Sort",
            ConceptExplanation = "Neighbor compares and swaps.",
            MachineBlockId = "Bubble_Sort_Machine",
            PracticeChallenge = new PracticeChallengeConfig
            {
                Enabled = true,
                PlayerTitle = "Sort It Yourself",
                PlayerDescription = "Do it yourself.",
                InitialValues = new[] { 3, 2, 1 }
            },
            MasteryChallenge = new PracticeChallengeConfig
            {
                Enabled = true,
                PlayerTitle = "Final Challenge",
                PlayerDescription = "New shipment.",
                InitialValues = ChallengeFactory.DefaultMasteryValues
            }
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
