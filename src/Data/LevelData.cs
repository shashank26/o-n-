using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PipelineArchitect.Challenges.Core;

namespace PipelineArchitect.Data;

/// <summary>
/// Career-tier classification for Pipeline Architect levels.
/// Serialized as the string name (e.g. "Intern") in level JSON profiles.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LevelTier
{
    Intern,
    Junior,
    Senior,
    Lead
}

/// <summary>
/// Well-known kid-friendly visual skin identifiers for level JSON <c>visualSkin</c> values.
/// Simulation logic never branches on these — they affect View-layer presentation only.
/// </summary>
public static class VisualSkinIds
{
    /// <summary>Raw numeric labels (default when omitted).</summary>
    public const string None = "";

    /// <summary>Values rendered as sized toy-brick sprites.</summary>
    public const string ToyBricks = "Toy_Bricks";

    /// <summary>Values rendered as solid colored item sprites.</summary>
    public const string Colors = "Colors";
}

/// <summary>
/// Operational resource ceilings enforced by the SimulationEngine.
/// Maps to the nested "budgets" object inside a level JSON profile.
/// </summary>
public sealed class LevelBudgets
{
    /// <summary>
    /// Maximum CPU cycles permitted before the simulation fails with an overheat state.
    /// </summary>
    [JsonPropertyName("maxCPUCycles")]
    public required int MaxCPUCycles { get; init; }

    /// <summary>
    /// Maximum concurrent RAM slot allocations permitted during execution.
    /// </summary>
    [JsonPropertyName("maxRAMSlots")]
    public required int MaxRAMSlots { get; init; }
}

/// <summary>
/// Kid-friendly Algorithm Creator Card metadata shown after a successful evaluation.
/// Maps to the nested "optimalCreator" object inside a level JSON profile.
/// </summary>
public sealed class CreatorMetadata
{
    /// <summary>
    /// Canonical algorithm name associated with this creator (e.g. "Merge Sort").
    /// </summary>
    [JsonPropertyName("algorithmName")]
    public required string AlgorithmName { get; init; }

    /// <summary>
    /// Display name of the historical inventor / scientist.
    /// </summary>
    [JsonPropertyName("inventorName")]
    public required string InventorName { get; init; }

    /// <summary>
    /// Year the algorithm was invented or published.
    /// </summary>
    [JsonPropertyName("inventionYear")]
    public required int InventionYear { get; init; }

    /// <summary>
    /// Godot resource path to a vector/raster avatar texture for the scientist's likeness
    /// (e.g. <c>res://Assets/Textures/Avatars/von_neumann.png</c>).
    /// </summary>
    [JsonPropertyName("avatarTexturePath")]
    public string? AvatarTexturePath { get; init; }

    /// <summary>
    /// Short historical context for older players / parents.
    /// </summary>
    [JsonPropertyName("historicalContext")]
    public required string HistoricalContext { get; init; }

    /// <summary>
    /// Simplified narrative used on the kid-facing creator card.
    /// </summary>
    [JsonPropertyName("kidFriendlyStory")]
    public required string KidFriendlyStory { get; init; }
}

/// <summary>
/// One onboarding coach beat for a level. Targets a named UI element for contextual guidance.
/// </summary>
public readonly struct TutorialStep
{
    /// <summary>Zero-based ordering index within the level's tutorial sequence.</summary>
    [JsonPropertyName("stepIndex")]
    public int StepIndex { get; init; }

    /// <summary>Player-facing coach message shown in the TutorialCoach overlay.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; }

    /// <summary>
    /// Logical UI target id (e.g. ConveyorBelt, GraphEdit_Ports, RunButton)
    /// used by the View layer for highlighting / focus.
    /// </summary>
    [JsonPropertyName("targetElement")]
    public string TargetElement { get; init; }
}

/// <summary>
/// Serializable schema representing a complete, data-driven level configuration.
/// Deserialized from external JSON profiles via <see cref="LevelDataLoader"/>.
/// Contains no Godot dependencies — pure C# data model only.
/// </summary>
public sealed class LevelData
{
    /// <summary>
    /// Unique level identifier used for lookups and progression tracking (e.g. "intern_01").
    /// </summary>
    [JsonPropertyName("levelId")]
    public required string LevelId { get; init; }

    /// <summary>
    /// Difficulty / career-tier classification for this level.
    /// </summary>
    [JsonPropertyName("tier")]
    public required LevelTier Tier { get; init; }

    /// <summary>
    /// Player-facing display title shown in the level select and workspace header.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Narrative briefing describing the logistical puzzle for the player.
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Ordered input data stream fed into the pipeline at simulation start.
    /// </summary>
    [JsonPropertyName("inputStream")]
    public required IReadOnlyList<int> InputStream { get; init; }

    /// <summary>
    /// Exact expected output collection used for deep-equality evaluation at simulation end.
    /// </summary>
    [JsonPropertyName("expectedOutput")]
    public required IReadOnlyList<int> ExpectedOutput { get; init; }

    /// <summary>
    /// CPU and RAM operational ceilings for this level.
    /// </summary>
    [JsonPropertyName("budgets")]
    public required LevelBudgets Budgets { get; init; }

    /// <summary>
    /// Toolbox inventory of structural building-block identifiers the player may place.
    /// </summary>
    [JsonPropertyName("allowedToolbox")]
    public required IReadOnlyList<string> AllowedToolbox { get; init; }

    /// <summary>
    /// Optional kid-friendly presentation skin (e.g. <see cref="VisualSkinIds.ToyBricks"/>,
    /// <see cref="VisualSkinIds.Colors"/>). Empty or null keeps raw numeric labels.
    /// Does not alter simulation inputs, outputs, or CPU/RAM accounting.
    /// </summary>
    [JsonPropertyName("visualSkin")]
    public string? VisualSkin { get; init; }

    /// <summary>
    /// Optional Algorithm Creator Card content unlocked on a successful win evaluation.
    /// </summary>
    [JsonPropertyName("optimalCreator")]
    public CreatorMetadata? OptimalCreator { get; init; }

    /// <summary>
    /// Optional context-aware onboarding sequence. Empty or null skips the TutorialCoach overlay.
    /// </summary>
    [JsonPropertyName("tutorialSteps")]
    public List<TutorialStep>? TutorialSteps { get; init; }

    /// <summary>
    /// Optional discovery-first interactive challenge. When enabled, the View presents it
    /// before unlocking the associated machine in the toolbox.
    /// </summary>
    [JsonPropertyName("interactiveChallenge")]
    public InteractiveChallengeConfig? InteractiveChallenge { get; init; }
}

/// <summary>
/// Loads and validates <see cref="LevelData"/> instances from JSON text or files.
/// Uses System.Text.Json with strict naming and enum conversion — no Godot coupling.
/// </summary>
public static class LevelDataLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // PascalCase enum names match level JSON ("Intern", "Junior", "Senior", "Lead").
        // Integer enum values are rejected to keep profiles human-readable and strict.
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    /// <summary>
    /// Deserializes a level profile from a raw JSON string.
    /// </summary>
    /// <param name="json">Complete level JSON document.</param>
    /// <returns>A fully populated, validated <see cref="LevelData"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is null or whitespace.</exception>
    /// <exception cref="JsonException">Thrown when the JSON is malformed or fails schema validation.</exception>
    public static LevelData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Level JSON content must not be null or empty.", nameof(json));
        }

        LevelData? level;
        try
        {
            level = JsonSerializer.Deserialize<LevelData>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Failed to deserialize level JSON: {ex.Message}", ex);
        }

        if (level is null)
        {
            throw new JsonException("Level JSON deserialized to null. Ensure the document is a valid object.");
        }

        Validate(level);
        return level;
    }

    /// <summary>
    /// Reads a level profile from a filesystem path and deserializes it.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to a .json level profile.</param>
    /// <returns>A fully populated, validated <see cref="LevelData"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="JsonException">Thrown when the file contents are malformed or invalid.</exception>
    public static LevelData FromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Level file path must not be null or empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Level profile not found at path: {filePath}", filePath);
        }

        string json = File.ReadAllText(filePath);
        return FromJson(json);
    }

    /// <summary>
    /// Attempts to deserialize a level profile without throwing on failure.
    /// </summary>
    /// <param name="json">Complete level JSON document.</param>
    /// <param name="level">Populated level on success; null on failure.</param>
    /// <param name="errorMessage">Human-readable failure reason when returning false.</param>
    /// <returns>True when deserialization and validation succeed; otherwise false.</returns>
    public static bool TryFromJson(string json, out LevelData? level, out string errorMessage)
    {
        level = null;
        errorMessage = string.Empty;

        try
        {
            level = FromJson(json);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Enforces invariants required by the simulation engine before a level may run.
    /// </summary>
    private static void Validate(LevelData level)
    {
        if (string.IsNullOrWhiteSpace(level.LevelId))
        {
            throw new JsonException("LevelData.LevelId must be a non-empty string.");
        }

        if (string.IsNullOrWhiteSpace(level.Title))
        {
            throw new JsonException("LevelData.Title must be a non-empty string.");
        }

        if (string.IsNullOrWhiteSpace(level.Description))
        {
            throw new JsonException("LevelData.Description must be a non-empty string.");
        }

        if (level.InputStream is null || level.InputStream.Count == 0)
        {
            throw new JsonException("LevelData.InputStream must contain at least one element.");
        }

        if (level.ExpectedOutput is null || level.ExpectedOutput.Count == 0)
        {
            throw new JsonException("LevelData.ExpectedOutput must contain at least one element.");
        }

        if (level.Budgets is null)
        {
            throw new JsonException("LevelData.Budgets is required.");
        }

        if (level.Budgets.MaxCPUCycles <= 0)
        {
            throw new JsonException("LevelData.Budgets.MaxCPUCycles must be greater than zero.");
        }

        if (level.Budgets.MaxRAMSlots <= 0)
        {
            throw new JsonException("LevelData.Budgets.MaxRAMSlots must be greater than zero.");
        }

        if (level.AllowedToolbox is null || level.AllowedToolbox.Count == 0)
        {
            throw new JsonException("LevelData.AllowedToolbox must list at least one building block.");
        }

        foreach (string blockId in level.AllowedToolbox)
        {
            if (string.IsNullOrWhiteSpace(blockId))
            {
                throw new JsonException("LevelData.AllowedToolbox entries must be non-empty strings.");
            }
        }

        if (level.OptimalCreator is not null)
        {
            ValidateCreator(level.OptimalCreator);
        }

        if (level.InteractiveChallenge is not null)
        {
            ValidateInteractiveChallenge(level.InteractiveChallenge);
        }
    }

    private static void ValidateInteractiveChallenge(InteractiveChallengeConfig challenge)
    {
        if (!challenge.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(challenge.ChallengeId))
        {
            throw new JsonException("InteractiveChallenge.challengeId must be non-empty when enabled.");
        }

        if (string.IsNullOrWhiteSpace(challenge.PlayerTitle))
        {
            throw new JsonException("InteractiveChallenge.playerTitle is required when enabled.");
        }

        if (string.IsNullOrWhiteSpace(challenge.PlayerDescription))
        {
            throw new JsonException("InteractiveChallenge.playerDescription is required when enabled.");
        }

        if (challenge.InitialValues is null || challenge.InitialValues.Count < 2)
        {
            throw new JsonException("InteractiveChallenge.initialValues must contain at least two values.");
        }

        if (string.IsNullOrWhiteSpace(challenge.ConceptName))
        {
            throw new JsonException("InteractiveChallenge.conceptName is required when enabled.");
        }

        if (string.IsNullOrWhiteSpace(challenge.ConceptExplanation))
        {
            throw new JsonException("InteractiveChallenge.conceptExplanation is required when enabled.");
        }

        if (string.IsNullOrWhiteSpace(challenge.MachineBlockId))
        {
            throw new JsonException("InteractiveChallenge.machineBlockId is required when enabled.");
        }
    }

    private static void ValidateCreator(CreatorMetadata creator)
    {
        if (string.IsNullOrWhiteSpace(creator.AlgorithmName))
        {
            throw new JsonException("CreatorMetadata.AlgorithmName must be a non-empty string.");
        }

        if (string.IsNullOrWhiteSpace(creator.InventorName))
        {
            throw new JsonException("CreatorMetadata.InventorName must be a non-empty string.");
        }

        if (creator.InventionYear <= 0)
        {
            throw new JsonException("CreatorMetadata.InventionYear must be a positive year.");
        }

        if (string.IsNullOrWhiteSpace(creator.HistoricalContext))
        {
            throw new JsonException("CreatorMetadata.HistoricalContext must be a non-empty string.");
        }

        if (string.IsNullOrWhiteSpace(creator.KidFriendlyStory))
        {
            throw new JsonException("CreatorMetadata.KidFriendlyStory must be a non-empty string.");
        }

        // avatarTexturePath is optional — the View layer falls back to a default avatar.
    }
}
