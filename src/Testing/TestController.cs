using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation;
using PipelineArchitect.UI;

namespace PipelineArchitect.Testing;

/// <summary>
/// High-production visual smoke test: industrial StyleBoxFlat cargo panels, Tween-driven
/// palette morphs, and a framed CPU top-bar — all timed by <see cref="SimulationSpeed"/>.
/// </summary>
public partial class TestController : Control
{
    private const string SmokeTestLevelPath = "res://Data/Levels/intern_01_bubblesort.json";
    private const string StyleBoxMetaKey = "CargoStyleBox";
    private const string ValueLabelMetaKey = "ValueLabel";

    private const float CpuDangerRatio = 0.75f;
    private const float ComparePulseSpeedRatio = 0.75f;
    private const float SwapArcHeightPixels = 48f;

    // Industrial palette
    private static readonly Color ColorPanelFill = new("1F242C");
    private static readonly Color ColorCyanBorder = new("219EBC");
    private static readonly Color ColorLaserGold = new("FFB703");
    private static readonly Color ColorNeonBlue = new("00D4FF");
    private static readonly Color ColorSystemGreen = new("2A9D8F");
    private static readonly Color ColorTextWhite = new("F8F9FA");
    private static readonly Color ColorHeaderFill = new("161A20");
    private static readonly Color ColorHeaderBorder = new("2D3640");
    private static readonly Color ColorDanger = new("E63946");
    private static readonly Color ColorIdleModulate = Colors.White;
    private static readonly Color ColorGoldGlow = new(1.0f, 0.72f, 0.01f, 0.45f);
    private static readonly Color ColorFlightShadow = new(0f, 0f, 0f, 0.55f);
    private static readonly Color ColorRestShadow = new(0f, 0f, 0f, 0.35f);

    /// <summary>Global animation / step timing (seconds).</summary>
    [Export]
    public float SimulationSpeed { get; set; } = 0.2f;

    [Export]
    public ProgressBar? CpuMeter { get; set; }

    [Export]
    public HBoxContainer? ConveyorBelt { get; set; }

    /// <summary>Optional GraphEdit used when validating pipeline wiring before run.</summary>
    [Export]
    public GraphEdit? PipelineCanvas { get; set; }

    /// <summary>Interactive bottom-bar Run Pipeline action button.</summary>
    [Export]
    public Button? RunButton { get; set; }

    [Export]
    public PanelContainer? TopBar { get; set; }

    [Export]
    public Label? HeaderTitleLabel { get; set; }

    [Export]
    public Label? HeaderSubtitleLabel { get; set; }

    private LevelData? _level;
    private SimulationEngine? _engine;
    private bool _eventsSubscribed;
    private bool _swapAnimating;
    private bool _simulationRunning;
    private AcceptDialog? _creatorDialog;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ResolveUiReferences();
        EnsureUiScaffold();
        ApplyIndustrialWorkspaceChrome();

        GD.Print("[TestController] Starting industrial StyleBoxFlat visual smoke test…");

        try
        {
            string absolutePath = ProjectSettings.GlobalizePath(SmokeTestLevelPath);
            GD.Print($"[TestController] LevelDataLoader reading '{SmokeTestLevelPath}'");
            _level = LevelDataLoader.FromFile(absolutePath);
            _engine = new SimulationEngine(_level);

            GD.Print(
                $"[TestController] Loaded '{_level.LevelId}' | " +
                $"input=[{string.Join(", ", _level.InputStream)}] | " +
                $"maxCPU={_level.Budgets.MaxCPUCycles} | simulationSpeed={SimulationSpeed:0.00}s");

            if (HeaderTitleLabel is not null)
            {
                HeaderTitleLabel.Text = $"[{_level.Tier}] {_level.Title}";
            }

            if (HeaderSubtitleLabel is not null)
            {
                HeaderSubtitleLabel.Text = "CPU Cycles  ·  Time Complexity Trace";
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"[TestController] Level load failed: {ex.Message}");
            return;
        }

        ConfigureCpuMeter(_level);
        InitializeVisualCargo();
        EnsureRunButton();
        SubscribeSimulationEvents();
        GD.Print("[TestController] Ready — tap ▶ RUN PIPELINE to start Bubble Sort visuals.");
    }

    public override void _ExitTree()
    {
        UnsubscribeSimulationEvents();
        GD.Print("[TestController] Unsubscribed from SimulationEvents (_ExitTree).");
    }

    /// <summary>
    /// Spawns styled cargo panels for each input integer using industrial StyleBoxFlat themes.
    /// </summary>
    public void InitializeVisualCargo()
    {
        if (ConveyorBelt is null || _level is null)
        {
            GD.PushError("[TestController] InitializeVisualCargo requires ConveyorBelt and a loaded level.");
            return;
        }

        foreach (Node child in ConveyorBelt.GetChildren())
        {
            child.QueueFree();
        }

        for (int index = 0; index < _level.InputStream.Count; index++)
        {
            int value = _level.InputStream[index];
            PanelContainer cargo = CreateStyledCargoPanel(index, value);
            ConveyorBelt.AddChild(cargo);
        }

        GD.Print($"[TestController] Spawned {ConveyorBelt.GetChildCount()} StyleBoxFlat cargo panels.");
    }

    private PanelContainer CreateStyledCargoPanel(int index, int value)
    {
        StyleBoxFlat style = CreateIdleCargoStyleBox();

        var cargo = new PanelContainer
        {
            Name = $"Cargo_{index}",
            CustomMinimumSize = new Vector2(84, 84),
            PivotOffset = new Vector2(42, 42),
            MouseFilter = MouseFilterEnum.Ignore
        };
        cargo.AddThemeStyleboxOverride("panel", style);
        cargo.SetMeta(StyleBoxMetaKey, style);

        var valueLabel = new Label
        {
            Name = "ValueLabel",
            Text = value.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        valueLabel.AddThemeColorOverride("font_color", ColorTextWhite);
        valueLabel.AddThemeFontSizeOverride("font_size", 28);
        // Bold via theme default; outline keeps digits readable on dark panels.
        valueLabel.AddThemeConstantOverride("outline_size", 2);
        valueLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.65f));
        cargo.AddChild(valueLabel);
        cargo.SetMeta(ValueLabelMetaKey, valueLabel);

        return cargo;
    }

    private static StyleBoxFlat CreateIdleCargoStyleBox()
    {
        return new StyleBoxFlat
        {
            BgColor = ColorPanelFill,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusBottomLeft = 8,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            BorderColor = ColorCyanBorder,
            ShadowSize = 4,
            ShadowColor = ColorRestShadow,
            ShadowOffset = new Vector2(0, 3),
            ContentMarginLeft = 8,
            ContentMarginTop = 8,
            ContentMarginRight = 8,
            ContentMarginBottom = 8
        };
    }

    #region Event wiring

    private void SubscribeSimulationEvents()
    {
        if (_eventsSubscribed)
        {
            return;
        }

        SimulationEvents.OnElementCompared += HandleElementCompared;
        SimulationEvents.OnElementsSwapped += HandleElementsSwapped;
        SimulationEvents.OnMetricsUpdated += HandleMetricsUpdated;
        SimulationEvents.OnSimulationEnded += HandleSimulationEnded;
        _eventsSubscribed = true;
        GD.Print("[TestController] SimulationEvents subscriptions locked in.");
    }

    private void UnsubscribeSimulationEvents()
    {
        if (!_eventsSubscribed)
        {
            return;
        }

        SimulationEvents.OnElementCompared -= HandleElementCompared;
        SimulationEvents.OnElementsSwapped -= HandleElementsSwapped;
        SimulationEvents.OnMetricsUpdated -= HandleMetricsUpdated;
        SimulationEvents.OnSimulationEnded -= HandleSimulationEnded;
        _eventsSubscribed = false;
    }

    #endregion

    /// <summary>
    /// Ensures the bottom-bar run button exists, is styled, and wires <see cref="OnRunButtonPressed"/>.
    /// </summary>
    private void EnsureRunButton()
    {
        if (RunButton is null)
        {
            HBoxContainer? bottomBar = GetNodeOrNull<HBoxContainer>("SmokeTestLayout/BottomBar");
            if (bottomBar is null)
            {
                var layout = GetNodeOrNull<VBoxContainer>("SmokeTestLayout");
                bottomBar = new HBoxContainer
                {
                    Name = "BottomBar",
                    Alignment = BoxContainer.AlignmentMode.Center
                };
                bottomBar.AddThemeConstantOverride("separation", 12);
                layout?.AddChild(bottomBar);
            }

            RunButton = bottomBar?.GetNodeOrNull<Button>("RunButton");
            if (RunButton is null && bottomBar is not null)
            {
                RunButton = new Button { Name = "RunButton" };
                bottomBar.AddChild(RunButton);
            }
        }

        if (RunButton is null)
        {
            return;
        }

        RunPipelineButtonStyle.Apply(RunButton);
        if (!RunButton.IsConnected(Button.SignalName.Pressed, Callable.From(OnRunButtonPressed)))
        {
            RunButton.Pressed += OnRunButtonPressed;
        }
    }

    /// <summary>
    /// Starts Bubble Sort when the canvas/conveyor is ready. Shows RUNNING… and blocks double-clicks.
    /// </summary>
    private async void OnRunButtonPressed()
    {
        if (_simulationRunning || _engine is null || _level is null || RunButton is null)
        {
            return;
        }

        if (!HasValidVisualPipeline())
        {
            GD.Print("[TestController] Run blocked — no valid pipeline/cargo setup yet.");
            return;
        }

        _simulationRunning = true;
        RunPipelineButtonStyle.SetRunning(RunButton);
        GD.Print("[TestController] ▶ RUN PIPELINE pressed — starting Bubble Sort + visual shuffle…");

        try
        {
            await RunBubbleSortSmokeTestAsync().ConfigureAwait(true);
        }
        finally
        {
            _simulationRunning = false;
            RunPipelineButtonStyle.SetIdle(RunButton);
            RunButton.Disabled = false;
        }
    }

    /// <summary>
    /// Prefer GraphEdit connection list when present; otherwise require spawned conveyor cargo.
    /// </summary>
    private bool HasValidVisualPipeline()
    {
        if (PipelineCanvas is not null)
        {
            var connections = PipelineCanvas.GetConnectionList();
            if (connections.Count > 0)
            {
                return true;
            }

            // GraphEdit present but unwired — block run (matches workspace rules).
            GD.Print("[TestController] GraphEdit has no connections yet.");
            return false;
        }

        return ConveyorBelt is not null && ConveyorBelt.GetChildCount() > 0;
    }

    private async Task RunBubbleSortSmokeTestAsync()
    {
        if (_engine is null || _level is null)
        {
            return;
        }

        float stepDelay = Math.Max(SimulationSpeed * 2f, GetComparePulseDuration() + GetSwapDuration() + 0.05f);
        GD.Print($"[TestController] ExecuteGraphAsync (Input→BubbleSort→Output) stepDelay={stepDelay:0.00}s");

        PipelineArchitect.Simulation.Pipeline.PipelineGraph graph =
            PipelineArchitect.Simulation.Pipeline.PipelineGraphBuilder.BuildLinearBubbleSort(_level);

        SimulationResult result = await _engine
            .ExecuteGraphAsync(graph, TimeSpan.FromSeconds(stepDelay))
            .ConfigureAwait(true);

        GD.Print(
            $"[TestController] Finished | success={result.IsSuccess} | CPU={result.CpuCyclesConsumed} | " +
            $"producer={result.ProducingNodeId} | output=[{string.Join(", ", result.FinalOutput)}]");
    }

    #region SimulationEvents

    private void HandleElementCompared(int indexA, int indexB)
    {
        if (_engine is not null)
        {
            IReadOnlyList<int> buffer = _engine.WorkingBuffer;
            GD.Print(
                $"[OnElementCompared] CPU={_engine.CurrentCPUCycles} | " +
                $"slot {indexA}={SafeValue(buffer, indexA)} vs slot {indexB}={SafeValue(buffer, indexB)}");
        }

        Callable.From(() => AnimateCompareStyleMorph(indexA, indexB)).CallDeferred();
    }

    private void HandleElementsSwapped(int fromIndex, int toIndex)
    {
        if (_engine is not null)
        {
            IReadOnlyList<int> buffer = _engine.WorkingBuffer;
            GD.Print(
                $"[OnElementsSwapped] CPU={_engine.CurrentCPUCycles} | " +
                $"swap {fromIndex}↔{toIndex} | now [{fromIndex}]={SafeValue(buffer, fromIndex)}, " +
                $"[{toIndex}]={SafeValue(buffer, toIndex)}");
        }

        Callable.From(() => _ = AnimateArcSwapAsync(fromIndex, toIndex)).CallDeferred();
    }

    private void HandleMetricsUpdated(float currentCpu, float currentRam)
    {
        GD.Print($"[OnMetricsUpdated] CPU={currentCpu:0} | RAM={currentRam:0}");
        Callable.From(() => AnimateCpuMeter(currentCpu)).CallDeferred();
    }

    private void HandleSimulationEnded(bool isSuccess, string evaluationMessage)
    {
        GD.Print($"[OnSimulationEnded] IsSuccess={isSuccess} | {evaluationMessage}");
        Callable.From(() =>
        {
            MorphAllCargoToVerifiedGreen(isSuccess);
            ShowCreatorCardDialog(isSuccess, evaluationMessage);
        }).CallDeferred();
    }

    #endregion

    #region Tween / StyleBox morphs

    private float GetComparePulseDuration() => Math.Max(0.05f, SimulationSpeed * ComparePulseSpeedRatio);

    private float GetSwapDuration() => Math.Max(0.05f, SimulationSpeed);

    /// <summary>
    /// Morphs cyan borders → Laser Gold with a faint yellow glow, plus a 1.1× scan pulse.
    /// </summary>
    private void AnimateCompareStyleMorph(int indexA, int indexB)
    {
        float duration = GetComparePulseDuration();
        AnimateSingleCompareMorph(GetConveyorChild(indexA), duration);
        AnimateSingleCompareMorph(GetConveyorChild(indexB), duration);
    }

    private void AnimateSingleCompareMorph(Control? block, float duration)
    {
        if (block is null || !GodotObject.IsInstanceValid(block))
        {
            return;
        }

        StyleBoxFlat? style = GetCargoStyle(block);
        if (style is null)
        {
            return;
        }

        block.PivotOffset = block.Size / 2f;
        block.Scale = Vector2.One;

        float half = duration * 0.5f;
        Tween tween = CreateTween().SetParallel(true);

        // Border: Cyan → Laser Gold, glow via enlarged gold-tinted shadow.
        tween.TweenProperty(style, "border_color", ColorLaserGold, half)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(style, "shadow_color", ColorGoldGlow, half);
        tween.TweenProperty(style, "shadow_size", 10, half);
        tween.TweenProperty(block, "scale", Vector2.One * 1.1f, half)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);

        tween.Chain().SetParallel(true);
        tween.TweenProperty(style, "border_color", ColorCyanBorder, half)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(style, "shadow_color", ColorRestShadow, half);
        tween.TweenProperty(style, "shadow_size", 4, half);
        tween.TweenProperty(block, "scale", Vector2.One, half)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
    }

    /// <summary>
    /// Neon-blue flight style + opposite arc paths + temporary deep blur shadow, then MoveChild.
    /// </summary>
    private async Task AnimateArcSwapAsync(int indexA, int indexB)
    {
        if (ConveyorBelt is null || indexA == indexB)
        {
            return;
        }

        while (_swapAnimating)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Control? blockA = GetConveyorChild(indexA);
        Control? blockB = GetConveyorChild(indexB);
        if (blockA is null || blockB is null)
        {
            return;
        }

        StyleBoxFlat? styleA = GetCargoStyle(blockA);
        StyleBoxFlat? styleB = GetCargoStyle(blockB);
        if (styleA is null || styleB is null)
        {
            return;
        }

        _swapAnimating = true;

        try
        {
            Vector2 globalA = blockA.GlobalPosition;
            Vector2 globalB = blockB.GlobalPosition;

            blockA.TopLevel = true;
            blockB.TopLevel = true;
            blockA.GlobalPosition = globalA;
            blockB.GlobalPosition = globalB;
            blockA.PivotOffset = blockA.Size / 2f;
            blockB.PivotOffset = blockB.Size / 2f;

            float duration = GetSwapDuration();
            float half = duration * 0.5f;

            // Snap into Neon Blue flight theme with a deeper soft shadow (blur stand-in).
            ApplyFlightStyle(styleA);
            ApplyFlightStyle(styleB);

            Vector2 midA = new(
                Mathf.Lerp(globalA.X, globalB.X, 0.5f),
                Mathf.Lerp(globalA.Y, globalB.Y, 0.5f) - SwapArcHeightPixels);
            Vector2 midB = new(
                Mathf.Lerp(globalB.X, globalA.X, 0.5f),
                Mathf.Lerp(globalB.Y, globalA.Y, 0.5f) + SwapArcHeightPixels);

            Tween tween = CreateTween().SetParallel(true);

            tween.TweenProperty(blockA, "global_position", midA, half)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(blockB, "global_position", midB, half)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(styleA, "shadow_size", 16, half);
            tween.TweenProperty(styleB, "shadow_size", 16, half);
            tween.TweenProperty(styleA, "shadow_offset", new Vector2(0, 10), half);
            tween.TweenProperty(styleB, "shadow_offset", new Vector2(0, 10), half);

            tween.Chain().SetParallel(true);
            tween.TweenProperty(blockA, "global_position", globalB, half)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.In);
            tween.TweenProperty(blockB, "global_position", globalA, half)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.In);
            tween.TweenProperty(styleA, "shadow_size", 4, half);
            tween.TweenProperty(styleB, "shadow_size", 4, half);
            tween.TweenProperty(styleA, "shadow_offset", new Vector2(0, 3), half);
            tween.TweenProperty(styleB, "shadow_offset", new Vector2(0, 3), half);

            await ToSignal(tween, Tween.SignalName.Finished);

            RestoreIdleStyle(styleA);
            RestoreIdleStyle(styleB);

            blockA.TopLevel = false;
            blockB.TopLevel = false;

            PermanentlyRearrangeConveyorChildren(blockA, blockB, indexA, indexB);
        }
        finally
        {
            _swapAnimating = false;
        }
    }

    private static void ApplyFlightStyle(StyleBoxFlat style)
    {
        style.BorderColor = ColorNeonBlue;
        style.ShadowColor = ColorFlightShadow;
        style.ShadowSize = 8;
        style.ShadowOffset = new Vector2(0, 6);
    }

    private static void RestoreIdleStyle(StyleBoxFlat style)
    {
        style.BorderColor = ColorCyanBorder;
        style.BgColor = ColorPanelFill;
        style.ShadowColor = ColorRestShadow;
        style.ShadowSize = 4;
        style.ShadowOffset = new Vector2(0, 3);
    }

    private void PermanentlyRearrangeConveyorChildren(Control blockA, Control blockB, int indexA, int indexB)
    {
        if (ConveyorBelt is null)
        {
            return;
        }

        ConveyorBelt.MoveChild(blockA, indexB);
        ConveyorBelt.MoveChild(blockB, indexA);

        blockA.Scale = Vector2.One;
        blockB.Scale = Vector2.One;
        blockA.Modulate = ColorIdleModulate;
        blockB.Modulate = ColorIdleModulate;

        GD.Print($"[TestController] MoveChild locked hierarchy: former {indexA}↔{indexB}");
    }

    /// <summary>
    /// Morphs every conveyor panel border/fill toward System Green on a successful run.
    /// </summary>
    private void MorphAllCargoToVerifiedGreen(bool isSuccess)
    {
        if (ConveyorBelt is null || !isSuccess)
        {
            return;
        }

        float duration = GetSwapDuration();
        foreach (Node child in ConveyorBelt.GetChildren())
        {
            if (child is not Control block)
            {
                continue;
            }

            StyleBoxFlat? style = GetCargoStyle(block);
            if (style is null)
            {
                continue;
            }

            Tween tween = CreateTween().SetParallel(true);
            tween.TweenProperty(style, "border_color", ColorSystemGreen, duration)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(style, "bg_color", ColorSystemGreen.Darkened(0.45f), duration);
            tween.TweenProperty(style, "shadow_color", new Color(0.16f, 0.62f, 0.56f, 0.4f), duration);
            tween.TweenProperty(style, "shadow_size", 8, duration);
            tween.TweenProperty(block, "scale", Vector2.One * 1.05f, duration * 0.5f)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            tween.Chain().TweenProperty(block, "scale", Vector2.One, duration * 0.5f);
        }
    }

    private void AnimateCpuMeter(float currentCpu)
    {
        if (CpuMeter is null || _level is null)
        {
            return;
        }

        float target = Mathf.Clamp(currentCpu, 0f, (float)CpuMeter.MaxValue);
        Tween tween = CreateTween();
        tween.TweenProperty(CpuMeter, "value", target, GetComparePulseDuration())
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);

        float ratio = target / Math.Max(1f, _level.Budgets.MaxCPUCycles);
        CpuMeter.Modulate = ratio >= CpuDangerRatio ? ColorDanger : ColorIdleModulate;
    }

    private void ShowCreatorCardDialog(bool isSuccess, string evaluationMessage)
    {
        if (_creatorDialog is not null && GodotObject.IsInstanceValid(_creatorDialog))
        {
            _creatorDialog.QueueFree();
            _creatorDialog = null;
        }

        CreatorMetadata? creator = _level?.OptimalCreator;
        string title = isSuccess ? "Algorithm Creator Unlocked!" : "Simulation Failed";
        string body = creator is not null && isSuccess
            ? $"{creator.InventorName}  ·  {creator.AlgorithmName} ({creator.InventionYear})\n\n" +
              $"{creator.KidFriendlyStory}\n\n{creator.HistoricalContext}\n\n{evaluationMessage}"
            : evaluationMessage;

        _creatorDialog = new AcceptDialog
        {
            Name = "CreatorCardDialog",
            Title = title,
            DialogText = body,
            Exclusive = true,
            Size = new Vector2I(520, 280)
        };
        AddChild(_creatorDialog);
        _creatorDialog.PopupCentered();
    }

    #endregion

    #region Industrial chrome

    private void ApplyIndustrialWorkspaceChrome()
    {
        if (GetNodeOrNull<ColorRect>("Backdrop") is null)
        {
            var backdrop = new ColorRect
            {
                Name = "Backdrop",
                Color = new Color("0E1116"),
                MouseFilter = MouseFilterEnum.Ignore
            };
            backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(backdrop);
            MoveChild(backdrop, 0);
        }

        if (TopBar is not null)
        {
            var headerStyle = new StyleBoxFlat
            {
                BgColor = ColorHeaderFill,
                BorderColor = ColorHeaderBorder,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = 10,
                CornerRadiusTopRight = 10,
                CornerRadiusBottomRight = 10,
                CornerRadiusBottomLeft = 10,
                ShadowSize = 6,
                ShadowColor = new Color(0f, 0f, 0f, 0.4f),
                ShadowOffset = new Vector2(0, 3),
                ContentMarginLeft = 18,
                ContentMarginTop = 14,
                ContentMarginRight = 18,
                ContentMarginBottom = 14
            };
            TopBar.AddThemeStyleboxOverride("panel", headerStyle);
        }

        StyleHeaderLabel(HeaderTitleLabel, 22, ColorTextWhite);
        StyleHeaderLabel(HeaderSubtitleLabel, 13, ColorCyanBorder);
        StyleCpuMeter();
    }

    private static void StyleHeaderLabel(Label? label, int fontSize, Color color)
    {
        if (label is null)
        {
            return;
        }

        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeConstantOverride("outline_size", 1);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.5f));
    }

    private void StyleCpuMeter()
    {
        if (CpuMeter is null)
        {
            return;
        }

        var background = new StyleBoxFlat
        {
            BgColor = new Color("12161C"),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusBottomLeft = 6,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            BorderColor = ColorHeaderBorder,
            ContentMarginLeft = 2,
            ContentMarginTop = 2,
            ContentMarginRight = 2,
            ContentMarginBottom = 2
        };

        var fill = new StyleBoxFlat
        {
            BgColor = ColorCyanBorder,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusBottomLeft = 5
        };

        CpuMeter.AddThemeStyleboxOverride("background", background);
        CpuMeter.AddThemeStyleboxOverride("fill", fill);
        CpuMeter.CustomMinimumSize = new Vector2(0, 22);
    }

    #endregion

    #region UI helpers

    private void ResolveUiReferences()
    {
        TopBar ??= GetNodeOrNull<PanelContainer>("%TopBar")
            ?? GetNodeOrNull<PanelContainer>("SmokeTestLayout/TopBar");
        HeaderTitleLabel ??= GetNodeOrNull<Label>("%HeaderTitle")
            ?? GetNodeOrNull<Label>("SmokeTestLayout/TopBar/TopBarBody/HeaderTitle");
        HeaderSubtitleLabel ??= GetNodeOrNull<Label>("%HeaderSubtitle")
            ?? GetNodeOrNull<Label>("SmokeTestLayout/TopBar/TopBarBody/HeaderSubtitle");
        CpuMeter ??= GetNodeOrNull<ProgressBar>("%CpuMeter")
            ?? GetNodeOrNull<ProgressBar>("SmokeTestLayout/TopBar/TopBarBody/CpuMeter");
        ConveyorBelt ??= GetNodeOrNull<HBoxContainer>("%ConveyorBelt")
            ?? GetNodeOrNull<HBoxContainer>("SmokeTestLayout/ConveyorSection/ConveyorBelt");
        PipelineCanvas ??= GetNodeOrNull<GraphEdit>("%PipelineCanvas")
            ?? GetNodeOrNull<GraphEdit>("SmokeTestLayout/PipelineCanvas");
        RunButton ??= GetNodeOrNull<Button>("%RunButton")
            ?? GetNodeOrNull<Button>("SmokeTestLayout/BottomBar/RunButton");
    }

    private void EnsureUiScaffold()
    {
        if (CpuMeter is not null && ConveyorBelt is not null && TopBar is not null)
        {
            return;
        }

        // Rebuild a complete industrial layout if scene exports are incomplete.
        foreach (Node child in GetChildren())
        {
            if (child.Name == "Backdrop")
            {
                continue;
            }

            child.QueueFree();
        }

        var root = new VBoxContainer
        {
            Name = "SmokeTestLayout"
        };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 20;
        root.OffsetTop = 20;
        root.OffsetRight = -20;
        root.OffsetBottom = -20;
        root.AddThemeConstantOverride("separation", 20);
        AddChild(root);

        TopBar = new PanelContainer
        {
            Name = "TopBar",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        root.AddChild(TopBar);

        var topBody = new VBoxContainer
        {
            Name = "TopBarBody"
        };
        topBody.AddThemeConstantOverride("separation", 8);
        TopBar.AddChild(topBody);

        HeaderTitleLabel = new Label
        {
            Name = "HeaderTitle",
            Text = "Pipeline Architect",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        topBody.AddChild(HeaderTitleLabel);

        HeaderSubtitleLabel = new Label
        {
            Name = "HeaderSubtitle",
            Text = "CPU Cycles  ·  Time Complexity Trace"
        };
        topBody.AddChild(HeaderSubtitleLabel);

        CpuMeter = new ProgressBar
        {
            Name = "CpuMeter",
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 22)
        };
        topBody.AddChild(CpuMeter);

        var conveyorSection = new VBoxContainer
        {
            Name = "ConveyorSection",
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        conveyorSection.AddThemeConstantOverride("separation", 10);
        root.AddChild(conveyorSection);

        conveyorSection.AddChild(new Label
        {
            Name = "ConveyorLabel",
            Text = "CONVEYOR BELT  ·  DATA STREAM",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        ConveyorBelt = new HBoxContainer
        {
            Name = "ConveyorBelt",
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        ConveyorBelt.AddThemeConstantOverride("separation", 14);
        conveyorSection.AddChild(ConveyorBelt);

        var bottomBar = new HBoxContainer
        {
            Name = "BottomBar",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        bottomBar.AddThemeConstantOverride("separation", 12);
        root.AddChild(bottomBar);

        RunButton = new Button { Name = "RunButton" };
        bottomBar.AddChild(RunButton);
    }

    private void ConfigureCpuMeter(LevelData level)
    {
        if (CpuMeter is null)
        {
            return;
        }

        CpuMeter.MaxValue = level.Budgets.MaxCPUCycles;
        CpuMeter.Value = 0;
        CpuMeter.ShowPercentage = false;
        CpuMeter.Modulate = ColorIdleModulate;
    }

    private Control? GetConveyorChild(int index)
    {
        if (ConveyorBelt is null || index < 0 || index >= ConveyorBelt.GetChildCount())
        {
            return null;
        }

        return ConveyorBelt.GetChild(index) as Control;
    }

    private static StyleBoxFlat? GetCargoStyle(Control block)
    {
        if (!block.HasMeta(StyleBoxMetaKey))
        {
            return null;
        }

        return block.GetMeta(StyleBoxMetaKey).AsGodotObject() as StyleBoxFlat;
    }

    private static int SafeValue(IReadOnlyList<int> buffer, int index)
    {
        return index >= 0 && index < buffer.Count ? buffer[index] : -1;
    }

    #endregion
}
