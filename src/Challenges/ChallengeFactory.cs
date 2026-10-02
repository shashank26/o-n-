using System;
using System.Collections.Generic;
using System.Linq;
using PipelineArchitect.Challenges.Core;
using PipelineArchitect.Data;

namespace PipelineArchitect.Challenges;

/// <summary>
/// Builds challenge sessions from level JSON config without branching on levelId.
/// </summary>
public static class ChallengeFactory
{
    /// <summary>Default mastery dataset — must differ from guided/practice reverse-sorted shipment.</summary>
    public static readonly int[] DefaultMasteryValues = { 4, 1, 5, 2, 3 };

    public static ChallengeSession? TryCreate(InteractiveChallengeConfig? config)
    {
        if (config is null || !config.Enabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(config.ChallengeId))
        {
            throw new InvalidOperationException("InteractiveChallenge.challengeId is required when enabled.");
        }

        return CreateGuidedSession(config);
    }

    public static ChallengeSession? TryCreate(LevelData level) =>
        TryCreate(level.InteractiveChallenge);

    public static ChallengeProgressionController? TryCreateProgression(LevelData level)
    {
        if (level.InteractiveChallenge is null || !level.InteractiveChallenge.Enabled)
        {
            return null;
        }

        return new ChallengeProgressionController(level.InteractiveChallenge);
    }

    public static ChallengeSession CreateGuidedSession(InteractiveChallengeConfig config)
    {
        ChallengeDefinition definition = ToGuidedDefinition(config);
        return new ChallengeSession(definition, new BubbleSort.AdjacentSortDiscoveryRules());
    }

    public static ChallengeSession CreatePracticeSession(InteractiveChallengeConfig config)
    {
        ChallengeDefinition definition = ToPracticeDefinition(config, config.PracticeChallenge, isMastery: false);
        return new ChallengeSession(definition, new BubbleSort.AdjacentSortPracticeRules());
    }

    public static ChallengeSession CreateMasterySession(InteractiveChallengeConfig config)
    {
        PracticeChallengeConfig mastery = config.MasteryChallenge
            ?? new PracticeChallengeConfig
            {
                Enabled = true,
                PlayerTitle = "Final Challenge",
                PlayerDescription = "Show that you can do it on a new shipment.",
                InitialValues = DefaultMasteryValues
            };

        if (mastery.InitialValues is null || mastery.InitialValues.Count < 2)
        {
            mastery = new PracticeChallengeConfig
            {
                Enabled = mastery.Enabled,
                ChallengeId = mastery.ChallengeId,
                PlayerTitle = string.IsNullOrWhiteSpace(mastery.PlayerTitle)
                    ? "Final Challenge"
                    : mastery.PlayerTitle,
                PlayerDescription = string.IsNullOrWhiteSpace(mastery.PlayerDescription)
                    ? "Show that you can do it on a new shipment."
                    : mastery.PlayerDescription,
                InitialValues = DefaultMasteryValues
            };
        }

        ChallengeDefinition definition = ToPracticeDefinition(config, mastery, isMastery: true);
        EnsureDistinctMasteryDataset(config, definition.InitialValues);
        return new ChallengeSession(definition, new BubbleSort.AdjacentSortPracticeRules());
    }

    private static void EnsureDistinctMasteryDataset(
        InteractiveChallengeConfig config,
        IReadOnlyList<int> masteryValues)
    {
        IReadOnlyList<int> guided = config.InitialValues;
        IReadOnlyList<int> practice = config.PracticeChallenge?.InitialValues ?? guided;
        if (SameSequence(masteryValues, guided) || SameSequence(masteryValues, practice))
        {
            throw new InvalidOperationException(
                "Mastery challenge initialValues must differ from guided and practice datasets.");
        }
    }

    private static bool SameSequence(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static ChallengeDefinition ToGuidedDefinition(InteractiveChallengeConfig config)
    {
        if (config.InitialValues is null || config.InitialValues.Count < 2)
        {
            throw new InvalidOperationException(
                "InteractiveChallenge.initialValues must contain at least two packages.");
        }

        return new ChallengeDefinition
        {
            ChallengeId = config.ChallengeId,
            PlayerTitle = config.PlayerTitle,
            PlayerDescription = config.PlayerDescription,
            InitialValues = config.InitialValues.ToArray(),
            ConceptName = config.ConceptName,
            ConceptExplanation = config.ConceptExplanation,
            MachineBlockId = config.MachineBlockId,
            MachineRevealTitle = config.MachineRevealTitle,
            MachineRevealBody = config.MachineRevealBody,
            RevealConceptAfterCompletion = config.RevealConceptAfterCompletion,
            IsGuided = true,
            PracticeChallenge = config.PracticeChallenge,
            MasteryChallenge = config.MasteryChallenge
        };
    }

    private static ChallengeDefinition ToPracticeDefinition(
        InteractiveChallengeConfig config,
        PracticeChallengeConfig? exercise,
        bool isMastery)
    {
        PracticeChallengeConfig resolved = exercise
            ?? new PracticeChallengeConfig { Enabled = true };

        IReadOnlyList<int> values = resolved.InitialValues is { Count: >= 2 }
            ? resolved.InitialValues
            : isMastery
                ? DefaultMasteryValues
                : config.InitialValues;

        if (values.Count < 2)
        {
            throw new InvalidOperationException(
                "Practice/mastery challenge requires at least two initial values.");
        }

        string defaultTitle = isMastery ? "Final Challenge" : "Sort It Yourself";
        string defaultBody = isMastery
            ? "Show that you can do it on a new shipment."
            : "You know how this works. Now sort the shipment yourself.";

        return new ChallengeDefinition
        {
            ChallengeId = string.IsNullOrWhiteSpace(resolved.ChallengeId)
                ? ChallengeIds.AdjacentSortPractice
                : resolved.ChallengeId,
            PlayerTitle = string.IsNullOrWhiteSpace(resolved.PlayerTitle)
                ? defaultTitle
                : resolved.PlayerTitle,
            PlayerDescription = string.IsNullOrWhiteSpace(resolved.PlayerDescription)
                ? defaultBody
                : resolved.PlayerDescription,
            InitialValues = values.ToArray(),
            ConceptName = config.ConceptName,
            ConceptExplanation = config.ConceptExplanation,
            MachineBlockId = config.MachineBlockId,
            MachineRevealTitle = config.MachineRevealTitle,
            MachineRevealBody = config.MachineRevealBody,
            RevealConceptAfterCompletion = false,
            IsGuided = false,
            PracticeChallenge = config.PracticeChallenge,
            MasteryChallenge = config.MasteryChallenge
        };
    }
}
