using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PipelineArchitect.Challenges;
using PipelineArchitect.Challenges.Core;
using PipelineArchitect.Data;
using PipelineArchitect.Simulation;
using PipelineArchitect.Simulation.Pipeline;

namespace PipelineArchitect.UI;

/// <summary>
/// Primary Godot View-layer container for Pipeline Architect.
/// Bridges <see cref="LevelDataLoader"/> / <see cref="SimulationEngine"/> to a
/// GraphEdit canvas: toolbox inventory, pipeline wiring, and event-driven highlights.
/// Attach this script to a root <see cref="Control"/> node (see scenes/PipelineWorkspace.tscn).
/// </summary>
public partial class PipelineWorkspace : Control
{
    /// <summary>Graph port type shared by data-stream and engine input sockets.</summary>
    public const int PortTypeDataStream = 0;

    /// <summary>CPU meter ratio at which the warning indicator begins flashing.</summary>
    private const float CpuDangerThresholdRatio = 0.80f;

    /// <summary>Touch-friendly port connector diameter (visible rim targets).</summary>
    private const int PortConnectorDiameter = 28;

    /// <summary>
    /// Generic fallback scientist likeness used when a level avatar path is missing or invalid.
    /// </summary>
    private const string DefaultAvatarTexturePath = "res://Assets/Textures/Avatars/default_architect.png";

    // Industrial palette (shared with smoke-test chrome)
    private static readonly Color ColorPanelFill = new("1F242C");
    private static readonly Color ColorCyanBorder = new("219EBC");
    private static readonly Color ColorNeonWire = new("00D4FF");
    private static readonly Color ColorWireGlow = new(0.0f, 0.83f, 1.0f, 0.35f);
    private static readonly Color ColorHeaderFill = new("161A20");
    private static readonly Color ColorTextWhite = new("F8F9FA");
    private static readonly Color ColorCompareHighlight = new(1.0f, 0.85f, 0.15f, 1.0f);
    private static readonly Color ColorSwapHighlight = new(0.25f, 0.95f, 0.55f, 1.0f);
    private static readonly Color ColorDangerFlash = new(1.0f, 0.35f, 0.25f, 1.0f);
    private static readonly Color ColorIdle = Colors.White;

    private static Texture2D? _sharedPortTexture;

    /// <summary>
    /// Canonical bootstrap level profile for the minimal runtime workspace scene.
    /// </summary>
    public const string BootstrapLevelJsonPath = "res://Data/Levels/intern_01_bubblesort.json";

    /// <summary>
    /// res:// path to the active level JSON profile.
    /// </summary>
    [Export]
    public string LevelJsonPath { get; set; } = BootstrapLevelJsonPath;

    /// <summary>
    /// Optional step delay (seconds) between simulation frames for animation gaps.
    /// </summary>
    [Export]
    public float SimulationStepDelaySeconds { get; set; } = 0.20f;

    /// <summary>
    /// When true, auto-runs Bubble Sort on ready (debug). Default false so players can wire GraphEdit first.
    /// </summary>
    [Export]
    public bool AutoRunBootstrapOnReady { get; set; }

    /// <summary>
    /// Testing shortcut: skip Learn→Practice→Prove quizzes and open the factory floor immediately.
    /// </summary>
    [Export]
    public bool SkipDiscoveryChallenge { get; set; } = true;

    private LevelData? _activeLevel;
    private SimulationEngine? _engine;
    private bool _cpuInDangerZone;
    private readonly PipelineLifecycle _lifecycle = new();
    private SimulationResult? _lastResult;
    private int[] _workingVisualValues = System.Array.Empty<int>();

    private Label _titleLabel = null!;
    private Label _descriptionLabel = null!;
    private Label _inputPreviewLabel = null!;
    private Control _inputPreviewChipsHost = null!;
    private Control _targetPreviewChipsHost = null!;
    private Label _cpuValueLabel = null!;
    private Label _ramValueLabel = null!;
    private Label _statusLabel = null!;
    private Label _execPhaseLabel = null!;
    private Label _execMachineLabel = null!;
    private Label _execOperationLabel = null!;
    private Label _tracePositionLabel = null!;
    private ProgressBar _cpuMeter = null!;
    private ProgressBar _ramMeter = null!;
    private VBoxContainer _toolboxList = null!;
    private GraphEdit _pipelineCanvas = null!;
    private Button _runButton = null!;
    private Control? _executeFooter;
    private Button _stepBackButton = null!;
    private Button _playPauseButton = null!;
    private Button _stepForwardButton = null!;
    private Button _replayButton = null!;
    private Button _speedButton = null!;
    private CanvasLayer? _creatorCardLayer;
    private TutorialCoach? _tutorialCoach;
    private DiscoveryChallengeView? _discoveryView;
    private ChallengeProgressionController? _challengeProgression;
    private bool _discoveryComplete;
    private CancellationTokenSource? _playbackCts;
    private float _presentationSpeed = 1f;
    private ExecutionStoryPresenter? _storyPresenter;

    private PipelineInputStreamGraphNode? _inputStreamNode;
    private PipelineOutputSinkGraphNode? _outputSinkNode;
    private PipelineEngineNode? _activeEngineNode;

    /// <summary>Authoritative player/editor lifecycle for this workspace.</summary>
    public PipelineState CurrentPipelineState => _lifecycle.Current;

    /// <summary>Exposes the lifecycle controller for focused unit tests.</summary>
    internal PipelineLifecycle Lifecycle => _lifecycle;

    /// <summary>Toolbox block id → placed computational GraphNode name (if any).</summary>
    private readonly Dictionary<string, string> _placedEngineNodes = new();

    /// <summary>Active pipeline edges assembled from GraphEdit connection lines.</summary>
    private readonly HashSet<PipelineConnection> _pipelineConnections = new();

    public override void _Ready()
    {
        BuildLayoutIfNeeded();
        ApplyGraphEditChrome();
        WireGraphEditSignals();
        SubscribeSimulationEvents();
        _lifecycle.StateChanged += OnLifecycleStateChanged;
        _storyPresenter = new ExecutionStoryPresenter(
            host: this,
            findNode: id => _pipelineCanvas?.GetNodeOrNull(id) as GraphNode,
            getActiveEngine: () => _activeEngineNode,
            getInput: () => _inputStreamNode,
            getOutput: () => _outputSinkNode,
            setStatus: (phase, machine, op) => SetExecutionStatus(phase, machine, op));

        LoadAndBindLevel(LevelJsonPath);
        RaiseExecuteFooter();

        if (AutoRunBootstrapOnReady)
        {
            BootstrapRuntimeSimulation();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Continue body-drags here so GraphEdit connection drags on the port rim stay free.
        if (PipelineGraphNodeTheme.TryHandleActiveDrag(@event))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Loads the bootstrap level and, when structurally ready, starts Run via the state machine.
    /// </summary>
    private void BootstrapRuntimeSimulation()
    {
        if (_activeLevel is null || _engine is null)
        {
            LevelJsonPath = BootstrapLevelJsonPath;
            GD.Print($"[Bootstrap] LevelDataLoader reading '{BootstrapLevelJsonPath}'");
            LoadAndBindLevel(BootstrapLevelJsonPath);
        }

        if (_activeLevel is null || _engine is null)
        {
            GD.Print("[Bootstrap] Aborting auto-run — level or engine failed to initialize.");
            return;
        }

        GD.Print(
            $"[Bootstrap] Level '{_activeLevel.LevelId}' loaded. " +
            $"Input stream = [{string.Join(", ", _activeLevel.InputStream)}].");

        ReevaluatePipelineReadiness();
        if (CurrentPipelineState != PipelineState.Ready)
        {
            GD.Print("[Bootstrap] Pipeline not Ready — wire Input → Bubble Sort → Output before auto-run.");
            return;
        }

        OnRunButtonPressed();
    }

    public override void _ExitTree()
    {
        _lifecycle.StateChanged -= OnLifecycleStateChanged;
        DismissTutorialCoach();
        DismissDiscoveryChallenge();
        DismissCreatorCardPopup();
        UnsubscribeSimulationEvents();
    }

    /// <summary>
    /// Loads a level JSON profile, optionally runs discovery-first challenge,
    /// then rebuilds the toolbox sidebar and seeds the canvas.
    /// </summary>
    public void LoadAndBindLevel(string levelResourcePath)
    {
        try
        {
            string absolutePath = ProjectSettings.GlobalizePath(levelResourcePath);
            _activeLevel = LevelDataLoader.FromFile(absolutePath);
        }
        catch (Exception ex)
        {
            GD.PushError($"PipelineWorkspace failed to load level '{levelResourcePath}': {ex.Message}");
            _statusLabel.Text = $"Level load failed: {ex.Message}";
            return;
        }

        _engine = new SimulationEngine(_activeLevel);
        _discoveryComplete = false;
        DismissDiscoveryChallenge();
        DismissTutorialCoach();

        ApplyLevelHeader(_activeLevel);
        ConfigureMeters(_activeLevel);

        ChallengeProgressionController? progression = ChallengeFactory.TryCreateProgression(_activeLevel);
        if (progression is not null && !SkipDiscoveryChallenge)
        {
            BeginDiscoveryChallenge(progression);
            return;
        }

        if (progression is not null && SkipDiscoveryChallenge)
        {
            GD.Print("[Pipeline] SkipDiscoveryChallenge=true — opening factory floor for drag/wire testing.");
        }

        EnterFactoryBuildPhase();
    }

    /// <summary>
    /// Unlocks toolbox/canvas/tutorial after discovery (or immediately when no challenge).
    /// </summary>
    private void EnterFactoryBuildPhase()
    {
        if (_activeLevel is null)
        {
            return;
        }

        _discoveryComplete = true;
        DismissDiscoveryChallenge();
        PopulateToolbox(_activeLevel);
        ResetPipelineCanvas(_activeLevel);
        SetupTutorialCoach(_activeLevel);
        GD.Print(
            $"[LevelDataLoader] Bound level '{_activeLevel.LevelId}' " +
            $"(tier={_activeLevel.Tier}, skin={_activeLevel.VisualSkin ?? "none"}, " +
            $"N={_activeLevel.InputStream.Count}).");
        SetExecutionStatus(
            phase: "READY",
            machine: "—",
            operation: "Drag machines from the center. Wire from the cyan dots on the edges.");
        // Discovery already left us in Editing; SetState is a no-op then — force unlock.
        SetPipelineState(PipelineState.Editing);
        ApplyGraphEditability(locked: false);
        ApplyToolboxAvailability(enabled: true);
        // GraphEdit may reset mouse filters when nodes enter the tree — re-apply next frame.
        CallDeferred(MethodName.DeferredUnlockFactoryFloor);
        RaiseExecuteFooter();
        ReevaluatePipelineReadiness();
    }

    private void DeferredUnlockFactoryFloor()
    {
        ApplyGraphEditability(locked: false);
        ApplyToolboxAvailability(enabled: true);
        ResyncAllGraphNodePositions();
        RaiseExecuteFooter();
    }

    private void BeginDiscoveryChallenge(ChallengeProgressionController progression)
    {
        _challengeProgression = progression;
        ClearToolboxForDiscovery();
        ClearPipelineCanvasForDiscovery();
        SetPipelineState(PipelineState.Editing);
        ApplyGraphEditability(locked: true);
        ApplyToolboxAvailability(enabled: false);

        InteractiveChallengeConfig? cfg = _activeLevel?.InteractiveChallenge;
        SetExecutionStatus(
            phase: "DISCOVER",
            machine: "—",
            operation: cfg?.PlayerTitle ?? "Figure out how to order the shipment.");

        if (_titleLabel is not null && cfg is not null)
        {
            _titleLabel.Text = cfg.PlayerTitle;
        }

        if (_descriptionLabel is not null && cfg is not null)
        {
            _descriptionLabel.Text = cfg.PlayerDescription;
        }

        _discoveryView = new DiscoveryChallengeView { Name = "DiscoveryChallengeView" };
        AddChild(_discoveryView);
        _discoveryView.FactoryHandoffRequested += OnDiscoveryFactoryHandoff;
        _discoveryView.Bind(progression);
    }

    private void OnDiscoveryFactoryHandoff()
    {
        if (_discoveryView is not null)
        {
            _discoveryView.FactoryHandoffRequested -= OnDiscoveryFactoryHandoff;
        }

        EnterFactoryBuildPhase();
    }

    private void DismissDiscoveryChallenge()
    {
        if (_discoveryView is not null && GodotObject.IsInstanceValid(_discoveryView))
        {
            _discoveryView.FactoryHandoffRequested -= OnDiscoveryFactoryHandoff;
            _discoveryView.Unbind();
            // Free immediately so the Layer-90 dimmer cannot keep eating factory-floor input.
            _discoveryView.Visible = false;
            _discoveryView.GetParent()?.RemoveChild(_discoveryView);
            _discoveryView.Free();
        }

        _discoveryView = null;
        _challengeProgression = null;
    }

    private void ClearToolboxForDiscovery()
    {
        if (_toolboxList is null)
        {
            return;
        }

        foreach (Node child in _toolboxList.GetChildren())
        {
            child.QueueFree();
        }

        var hint = new Label
        {
            Text = "Complete the shipment puzzle to unlock your first machine.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        hint.AddThemeColorOverride("font_color", new Color("F8F9FA"));
        _toolboxList.AddChild(hint);
    }

    private void ClearPipelineCanvasForDiscovery()
    {
        if (_pipelineCanvas is null)
        {
            return;
        }

        foreach (Node child in _pipelineCanvas.GetChildren())
        {
            child.QueueFree();
        }

        _pipelineConnections.Clear();
        _placedEngineNodes.Clear();
        _inputStreamNode = null;
        _outputSinkNode = null;
        _activeEngineNode = null;
    }

    /// <summary>
    /// Instantiates the spotlight TutorialCoach when the level ships tutorialSteps data.
    /// </summary>
    private void SetupTutorialCoach(LevelData level)
    {
        DismissTutorialCoach();

        if (level.TutorialSteps is null || level.TutorialSteps.Count == 0)
        {
            return;
        }

        _tutorialCoach = new TutorialCoach
        {
            Name = "TutorialCoach",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SafeBottomReserve = ExecuteFooterHeight
        };
        _tutorialCoach.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tutorialCoach.OffsetBottom = -ExecuteFooterHeight;
        _tutorialCoach.ResolveTarget = ResolveTutorialTarget;
        AddChild(_tutorialCoach);
        _tutorialCoach.MakeClickThrough();
        _tutorialCoach.BindSteps(level.TutorialSteps);
        _tutorialCoach.ShowStep(0);
        RaiseExecuteFooter();

        GD.Print($"[TutorialCoach] Spotlight coach bound with {level.TutorialSteps.Count} steps.");
    }

    /// <summary>
    /// Maps level JSON targetElement ids onto live workspace controls for the spotlight.
    /// </summary>
    private Control? ResolveTutorialTarget(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        return targetId.Trim() switch
        {
            "Toolbox" => GetNodeOrNull<Control>("RootLayout/BottomRow/ToolboxPanel")
                ?? _toolboxList,
            "InputPort" => (Control?)_inputStreamNode,
            "MachineOutPort" => (Control?)_activeEngineNode
                ?? _inputStreamNode,
            "RunButton" => _runButton,
            "ExecutionStatus" => GetNodeOrNull<Control>("RootLayout/BottomRow/ExecutionStatusPanel")
                ?? _statusLabel,
            "PipelineCanvas" => _pipelineCanvas,
            _ => FindChild(targetId, recursive: true, owned: false) as Control
        };
    }

    private void DismissTutorialCoach()
    {
        if (_tutorialCoach is not null && GodotObject.IsInstanceValid(_tutorialCoach))
        {
            _tutorialCoach.QueueFree();
        }

        _tutorialCoach = null;
    }

    #region Pipeline lifecycle

    private void SetPipelineState(PipelineState newState)
    {
        _lifecycle.SetState(newState);
    }

    private void OnLifecycleStateChanged(PipelineState previous, PipelineState next)
    {
        GD.Print($"[Pipeline] State: {previous} -> {next}");
        ApplyPipelineStateToUi(next);
    }

    private void ApplyPipelineStateToUi(PipelineState state)
    {
        bool graphLocked = state is PipelineState.Running
            or PipelineState.Completed
            or PipelineState.AwaitingReplayChoice
            or PipelineState.PlayingTrace;
        ApplyGraphEditability(locked: graphLocked);
        ApplyToolboxAvailability(enabled: state is PipelineState.Editing or PipelineState.Ready or PipelineState.Failed);

        if (_runButton is null)
        {
            return;
        }

        switch (state)
        {
            case PipelineState.Running:
                RunPipelineButtonStyle.SetRunning(_runButton);
                break;
            case PipelineState.AwaitingReplayChoice:
                RunPipelineButtonStyle.SetIdle(_runButton);
                _runButton.Disabled = true;
                _runButton.Text = "⏳ RESULT READY";
                break;
            case PipelineState.PlayingTrace:
                RunPipelineButtonStyle.SetRunning(_runButton);
                _runButton.Text = "▶ WATCHING…";
                break;
            case PipelineState.Success:
                RunPipelineButtonStyle.SetSuccess(_runButton);
                break;
            case PipelineState.Failed:
                RunPipelineButtonStyle.SetFailed(_runButton);
                _runButton.Disabled = true;
                break;
            case PipelineState.Ready:
                RunPipelineButtonStyle.SetIdle(_runButton);
                _runButton.Disabled = false;
                _runButton.Modulate = Colors.White;
                break;
            default:
                RunPipelineButtonStyle.SetIdle(_runButton);
                _runButton.Disabled = true;
                _runButton.Modulate = new Color(0.75f, 0.75f, 0.75f, 0.9f);
                break;
        }

        _runButton.TooltipText = state switch
        {
            PipelineState.Ready => "Execute your factory pipeline.",
            PipelineState.Editing => "Connect Input → Bubble Sort → Output to enable Execute.",
            PipelineState.Running => "Your factory is simulating…",
            PipelineState.AwaitingReplayChoice => "Simulation finished — choose Show Me or Skip.",
            PipelineState.PlayingTrace => "Watching the recorded execution…",
            PipelineState.Completed => "Checking the result…",
            PipelineState.Success => "Factory solved the shipment.",
            PipelineState.Failed => "Pipeline stopped — fix the machinery and try again.",
            _ => string.Empty
        };
    }

    private void ApplyGraphEditability(bool locked)
    {
        if (_pipelineCanvas is null)
        {
            return;
        }

        foreach (Node child in _pipelineCanvas.GetChildren())
        {
            if (child is GraphElement element)
            {
                ConfigureGraphElementInteraction(element, locked);
            }
        }
    }

    /// <summary>
    /// GraphEdit native drag/connect need Pass filter. We only make child chrome Ignore so
    /// body clicks reach the GraphNode, then bubble to GraphEdit for move vs port wiring.
    /// </summary>
    private void ConfigureGraphElementInteraction(GraphElement element, bool locked)
    {
        if (element is GraphNode graphNode)
        {
            PipelineGraphNodeTheme.ConfigureInteractiveNode(graphNode, locked);
            WirePositionOffsetSync(graphNode);
            return;
        }

        element.Draggable = !locked;
        element.Selectable = !locked;
    }

    private void WirePositionOffsetSync(GraphElement element)
    {
        if (element.HasMeta("pa_offset_sync"))
        {
            return;
        }

        element.SetMeta("pa_offset_sync", true);
        element.PositionOffsetChanged += () => SyncGraphNodeScreenPosition(element);
    }

    private void ApplyToolboxAvailability(bool enabled)
    {
        if (_toolboxList is null)
        {
            return;
        }

        foreach (Node child in _toolboxList.GetChildren())
        {
            if (child is Button button)
            {
                button.Disabled = !enabled;
            }
        }
    }

    /// <summary>
    /// Structural readiness only: Input Stream → Bubble Sort → Output.
    /// </summary>
    private bool IsPipelineReady()
    {
        SyncPipelineRoutesToEngine();
        return _engine is not null && PipelineLifecycle.IsPipelineReady(_engine.PipelineRoutes);
    }

    private void ReevaluatePipelineReadiness()
    {
        if (_lifecycle.IsGraphLocked || CurrentPipelineState == PipelineState.Success)
        {
            return;
        }

        bool ready = IsPipelineReady();
        _lifecycle.ReevaluateAfterGraphChange(ready);
        ApplyPipelineStateToUi(CurrentPipelineState);
    }

    private void AdvanceTutorialAfterBubbleSortLink()
    {
        if (_tutorialCoach is null)
        {
            return;
        }

        bool hasInputToBubble = _engine?.PipelineRoutes.Any(r =>
            PipelineNodeIds.IsInputStream(r.SourceNodeId)
            && r.TargetNodeId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase)) == true;
        bool hasBubbleToOutput = _engine?.PipelineRoutes.Any(r =>
            r.SourceNodeId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase)
            && PipelineNodeIds.IsOutputSink(r.TargetNodeId)) == true;

        if (CurrentPipelineState == PipelineState.Ready)
        {
            _tutorialCoach.ShowStep(3);
            SetExecutionStatus("READY", FriendlyMachineName(_activeEngineNode?.BlockId), "Press Execute Factory.");
            return;
        }

        if (hasInputToBubble && !hasBubbleToOutput)
        {
            _tutorialCoach.ShowStep(2);
            SetExecutionStatus("BUILDING", FriendlyMachineName(_activeEngineNode?.BlockId), "Connect Bubble Sort → Output.");
            return;
        }

        if (_placedEngineNodes.Count > 0 && !hasInputToBubble)
        {
            _tutorialCoach.ShowStep(1);
            SetExecutionStatus("BUILDING", FriendlyMachineName(_activeEngineNode?.BlockId), "Connect Input Shipment → Bubble Sort.");
        }
    }

    #endregion

    #region Layout construction

    /// <summary>
    /// Builds a mobile-safe objective / factory / machines+status layout.
    /// </summary>
    private void BuildLayoutIfNeeded()
    {
        if (GetNodeOrNull("RootLayout") is not null)
        {
            BindExistingLayoutNodes();
            return;
        }

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var rootLayout = new VBoxContainer
        {
            Name = "RootLayout"
        };
        rootLayout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        rootLayout.AddThemeConstantOverride("separation", 8);
        AddChild(rootLayout);

        // --- Objective header ------------------------------------------------
        var headerPanel = new PanelContainer { Name = "ObjectivePanel" };
        rootLayout.AddChild(headerPanel);
        var headerBody = new VBoxContainer { Name = "ObjectiveBody" };
        headerBody.AddThemeConstantOverride("separation", 6);
        headerPanel.AddChild(headerBody);

        _titleLabel = new Label
        {
            Name = "TitleLabel",
            Text = "SORT THE SHIPMENT",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 22);
        _titleLabel.AddThemeColorOverride("font_color", ColorTextWhite);
        headerBody.AddChild(_titleLabel);

        _descriptionLabel = new Label
        {
            Name = "DescriptionLabel",
            Text = string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _descriptionLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.88f, 0.92f));
        headerBody.AddChild(_descriptionLabel);

        var objectiveRow = new HBoxContainer { Name = "ObjectiveRow" };
        objectiveRow.AddThemeConstantOverride("separation", 16);
        headerBody.AddChild(objectiveRow);

        var inputCol = new VBoxContainer { Name = "InputPreviewCol", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        objectiveRow.AddChild(inputCol);
        _inputPreviewLabel = new Label { Name = "InputPreviewTitle", Text = "INPUT" };
        _inputPreviewLabel.AddThemeColorOverride("font_color", ColorCyanBorder);
        inputCol.AddChild(_inputPreviewLabel);
        _inputPreviewChipsHost = new MarginContainer { Name = "InputPreviewChipsHost" };
        inputCol.AddChild(_inputPreviewChipsHost);

        var targetCol = new VBoxContainer { Name = "TargetPreviewCol", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        objectiveRow.AddChild(targetCol);
        var targetTitle = new Label { Name = "TargetPreviewTitle", Text = "TARGET" };
        targetTitle.AddThemeColorOverride("font_color", new Color("E9C46A"));
        targetCol.AddChild(targetTitle);
        _targetPreviewChipsHost = new MarginContainer { Name = "TargetPreviewChipsHost" };
        targetCol.AddChild(_targetPreviewChipsHost);

        var metersCol = new VBoxContainer { Name = "MetersCol" };
        objectiveRow.AddChild(metersCol);
        _cpuValueLabel = new Label { Name = "CpuValueLabel", Text = "CPU: 0 / 0" };
        _cpuValueLabel.AddThemeColorOverride("font_color", ColorTextWhite);
        metersCol.AddChild(_cpuValueLabel);
        _cpuMeter = new ProgressBar
        {
            Name = "CpuMeter",
            CustomMinimumSize = new Vector2(120, 14),
            MaxValue = 100,
            ShowPercentage = false
        };
        metersCol.AddChild(_cpuMeter);
        _ramValueLabel = new Label { Name = "RamValueLabel", Text = "RAM: 0 / 0" };
        _ramValueLabel.AddThemeColorOverride("font_color", ColorTextWhite);
        metersCol.AddChild(_ramValueLabel);
        _ramMeter = new ProgressBar
        {
            Name = "RamMeter",
            CustomMinimumSize = new Vector2(120, 14),
            MaxValue = 100,
            ShowPercentage = false
        };
        metersCol.AddChild(_ramMeter);

        // --- Factory floor ---------------------------------------------------
        var factoryLabel = new Label
        {
            Name = "FactoryFloorLabel",
            Text = "FACTORY FLOOR"
        };
        factoryLabel.AddThemeColorOverride("font_color", ColorCyanBorder);
        rootLayout.AddChild(factoryLabel);

        _pipelineCanvas = new GraphEdit
        {
            Name = "PipelineCanvas",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            RightDisconnects = true,
            ShowGrid = true,
            SnappingEnabled = true,
            SnappingDistance = 16,
            CustomMinimumSize = new Vector2(0, 180),
            // Editor chrome steals space / touches on mobile factory floor.
            ShowMenu = false,
            ShowGridButtons = false,
            ShowMinimapButton = false,
            ShowArrangeButton = false,
            ShowZoomLabel = false,
            MinimapEnabled = false
        };
        rootLayout.AddChild(_pipelineCanvas);

        // --- Bottom: machines + execution status -----------------------------
        var bottomRow = new HBoxContainer
        {
            Name = "BottomRow",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        bottomRow.AddThemeConstantOverride("separation", 10);
        rootLayout.AddChild(bottomRow);

        var machinesPanel = new PanelContainer
        {
            Name = "ToolboxPanel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 120)
        };
        bottomRow.AddChild(machinesPanel);
        var machinesBody = new VBoxContainer { Name = "ToolboxBody" };
        machinesPanel.AddChild(machinesBody);
        var machinesHeader = new Label
        {
            Name = "ToolboxHeader",
            Text = "AVAILABLE MACHINES"
        };
        machinesHeader.AddThemeColorOverride("font_color", ColorCyanBorder);
        machinesBody.AddChild(machinesHeader);
        _toolboxList = new VBoxContainer
        {
            Name = "ToolboxList",
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        machinesBody.AddChild(_toolboxList);

        var statusPanel = new PanelContainer
        {
            Name = "ExecutionStatusPanel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 120)
        };
        bottomRow.AddChild(statusPanel);
        var statusBody = new VBoxContainer { Name = "ExecutionStatusBody" };
        statusBody.AddThemeConstantOverride("separation", 4);
        statusPanel.AddChild(statusBody);

        var statusHeader = new Label { Name = "ExecutionStatusHeader", Text = "EXECUTION STATUS" };
        statusHeader.AddThemeColorOverride("font_color", ColorCyanBorder);
        statusBody.AddChild(statusHeader);

        _execPhaseLabel = new Label { Name = "ExecPhaseLabel", Text = "Phase: READY" };
        _execPhaseLabel.AddThemeColorOverride("font_color", ColorTextWhite);
        statusBody.AddChild(_execPhaseLabel);
        _execMachineLabel = new Label { Name = "ExecMachineLabel", Text = "Machine: —" };
        _execMachineLabel.AddThemeColorOverride("font_color", ColorTextWhite);
        statusBody.AddChild(_execMachineLabel);
        _execOperationLabel = new Label
        {
            Name = "ExecOperationLabel",
            Text = "Operation: —",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _execOperationLabel.AddThemeColorOverride("font_color", ColorTextWhite);
        statusBody.AddChild(_execOperationLabel);

        _statusLabel = new Label
        {
            Name = "StatusLabel",
            Text = "Waiting for level…",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.84f, 0.9f));
        statusBody.AddChild(_statusLabel);

        _tracePositionLabel = new Label
        {
            Name = "TracePositionLabel",
            Text = "Step 0 / 0",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _tracePositionLabel.AddThemeColorOverride("font_color", ColorCyanBorder);
        statusBody.AddChild(_tracePositionLabel);

        var controlsRow = new HBoxContainer { Name = "PlaybackControlsRow" };
        controlsRow.AddThemeConstantOverride("separation", 6);
        statusBody.AddChild(controlsRow);

        _stepBackButton = CreatePlaybackButton("StepBackButton", "◀ Back");
        _stepBackButton.Pressed += OnStepBackPressed;
        controlsRow.AddChild(_stepBackButton);

        _playPauseButton = CreatePlaybackButton("PlayPauseButton", "▶ Play");
        _playPauseButton.Pressed += OnPlayPausePressed;
        controlsRow.AddChild(_playPauseButton);

        _stepForwardButton = CreatePlaybackButton("StepForwardButton", "Step ▶");
        _stepForwardButton.Pressed += OnStepForwardPressed;
        controlsRow.AddChild(_stepForwardButton);

        var utilRow = new HBoxContainer { Name = "PlaybackUtilRow" };
        utilRow.AddThemeConstantOverride("separation", 6);
        statusBody.AddChild(utilRow);

        _speedButton = CreatePlaybackButton("SpeedButton", "1×");
        _speedButton.Pressed += OnSpeedPressed;
        utilRow.AddChild(_speedButton);

        _replayButton = CreatePlaybackButton("ReplayButton", "Replay");
        _replayButton.Pressed += OnReplayPressed;
        utilRow.AddChild(_replayButton);

        // Leave room in the main column for the pinned execute bar below.
        rootLayout.OffsetBottom = -ExecuteFooterHeight;

        BuildPinnedExecuteFooter();
        UpdatePlaybackControlsUi();
    }

    private const float ExecuteFooterHeight = 72f;

    /// <summary>
    /// Pins Execute Factory to the workspace bottom, above the tutorial dimmer.
    /// </summary>
    private void BuildPinnedExecuteFooter()
    {
        if (_executeFooter is not null && GodotObject.IsInstanceValid(_executeFooter))
        {
            _executeFooter.QueueFree();
        }

        var footer = new PanelContainer
        {
            Name = "ExecuteFooter",
            MouseFilter = MouseFilterEnum.Stop
        };
        footer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        footer.OffsetTop = -ExecuteFooterHeight;
        footer.OffsetBottom = 0;
        footer.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("0E1218"),
            BorderColor = ColorCyanBorder,
            BorderWidthTop = 2,
            ContentMarginLeft = 12,
            ContentMarginTop = 8,
            ContentMarginRight = 12,
            ContentMarginBottom = 8
        });
        AddChild(footer);
        _executeFooter = footer;

        var executeBody = new VBoxContainer
        {
            Name = "ExecuteFooterBody",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        footer.AddChild(executeBody);

        _runButton = new Button
        {
            Name = "RunButton",
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 52)
        };
        RunPipelineButtonStyle.Apply(_runButton);
        _runButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _runButton.Pressed += OnRunButtonPressed;
        executeBody.AddChild(_runButton);
    }

    /// <summary>Keeps the execute bar above tutorial / overlays.</summary>
    private void RaiseExecuteFooter()
    {
        if (_executeFooter is not null && GodotObject.IsInstanceValid(_executeFooter))
        {
            _executeFooter.MoveToFront();
            _executeFooter.Visible = true;
            _executeFooter.Modulate = Colors.White;
        }

        if (_runButton is not null && GodotObject.IsInstanceValid(_runButton))
        {
            _runButton.Visible = true;
            _runButton.Modulate = Colors.White;
        }
    }

    private static Button CreatePlaybackButton(string name, string text)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40)
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        return button;
    }

    private void BindExistingLayoutNodes()
    {
        // Prefer the new layout paths; fall back for older scenes.
        _toolboxList = GetNodeOrNull<VBoxContainer>("RootLayout/BottomRow/ToolboxPanel/ToolboxBody/ToolboxList")
            ?? GetNode<VBoxContainer>("RootLayout/ToolboxPanel/ToolboxBody/ToolboxList");
        _titleLabel = GetNodeOrNull<Label>("RootLayout/ObjectivePanel/ObjectiveBody/TitleLabel")
            ?? GetNode<Label>("RootLayout/MainColumn/TitleLabel");
        _descriptionLabel = GetNodeOrNull<Label>("RootLayout/ObjectivePanel/ObjectiveBody/DescriptionLabel")
            ?? GetNode<Label>("RootLayout/MainColumn/DescriptionLabel");
        _cpuMeter = GetNodeOrNull<ProgressBar>("RootLayout/ObjectivePanel/ObjectiveBody/ObjectiveRow/MetersCol/CpuMeter")
            ?? GetNode<ProgressBar>("RootLayout/MainColumn/MetersRow/CpuMeter");
        _ramMeter = GetNodeOrNull<ProgressBar>("RootLayout/ObjectivePanel/ObjectiveBody/ObjectiveRow/MetersCol/RamMeter")
            ?? GetNode<ProgressBar>("RootLayout/MainColumn/MetersRow/RamMeter");
        _pipelineCanvas = GetNodeOrNull<GraphEdit>("RootLayout/PipelineCanvas")
            ?? GetNode<GraphEdit>("RootLayout/MainColumn/PipelineCanvas");
        _runButton = GetNodeOrNull<Button>("ExecuteFooter/ExecuteFooterBody/RunButton")
            ?? GetNodeOrNull<Button>("RootLayout/ExecuteFooter/ExecuteFooterBody/RunButton")
            ?? GetNodeOrNull<Button>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/RunButton")
            ?? GetNodeOrNull<Button>("RootLayout/MainColumn/Footer/RunButton");

        if (_runButton is null)
        {
            BuildPinnedExecuteFooter();
        }
        else
        {
            _executeFooter = GetNodeOrNull<Control>("ExecuteFooter")
                ?? _runButton.GetParent()?.GetParent() as Control;
            RaiseExecuteFooter();
        }
        _statusLabel = GetNodeOrNull<Label>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/StatusLabel")
            ?? GetNode<Label>("RootLayout/MainColumn/Footer/StatusLabel");

        _inputPreviewChipsHost = GetNodeOrNull<Control>("RootLayout/ObjectivePanel/ObjectiveBody/ObjectiveRow/InputPreviewCol/InputPreviewChipsHost")
            ?? new MarginContainer { Name = "InputPreviewChipsHost" };
        _targetPreviewChipsHost = GetNodeOrNull<Control>("RootLayout/ObjectivePanel/ObjectiveBody/ObjectiveRow/TargetPreviewCol/TargetPreviewChipsHost")
            ?? new MarginContainer { Name = "TargetPreviewChipsHost" };
        _cpuValueLabel = GetNodeOrNull<Label>("RootLayout/ObjectivePanel/ObjectiveBody/ObjectiveRow/MetersCol/CpuValueLabel")
            ?? new Label { Name = "CpuValueLabel" };
        _ramValueLabel = GetNodeOrNull<Label>("RootLayout/ObjectivePanel/ObjectiveBody/ObjectiveRow/MetersCol/RamValueLabel")
            ?? new Label { Name = "RamValueLabel" };
        _execPhaseLabel = GetNodeOrNull<Label>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/ExecPhaseLabel")
            ?? new Label { Name = "ExecPhaseLabel" };
        _execMachineLabel = GetNodeOrNull<Label>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/ExecMachineLabel")
            ?? new Label { Name = "ExecMachineLabel" };
        _execOperationLabel = GetNodeOrNull<Label>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/ExecOperationLabel")
            ?? new Label { Name = "ExecOperationLabel" };
        _inputPreviewLabel = GetNodeOrNull<Label>("RootLayout/ObjectivePanel/ObjectiveBody/ObjectiveRow/InputPreviewCol/InputPreviewTitle")
            ?? new Label { Name = "InputPreviewTitle", Text = "INPUT" };
        _tracePositionLabel = GetNodeOrNull<Label>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/TracePositionLabel")
            ?? new Label { Name = "TracePositionLabel", Text = "Step 0 / 0" };
        _stepBackButton = GetNodeOrNull<Button>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/PlaybackControlsRow/StepBackButton")
            ?? CreatePlaybackButton("StepBackButton", "◀ Back");
        _playPauseButton = GetNodeOrNull<Button>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/PlaybackControlsRow/PlayPauseButton")
            ?? CreatePlaybackButton("PlayPauseButton", "▶ Play");
        _stepForwardButton = GetNodeOrNull<Button>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/PlaybackControlsRow/StepForwardButton")
            ?? CreatePlaybackButton("StepForwardButton", "Step ▶");
        _speedButton = GetNodeOrNull<Button>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/PlaybackUtilRow/SpeedButton")
            ?? CreatePlaybackButton("SpeedButton", "1×");
        _replayButton = GetNodeOrNull<Button>("RootLayout/BottomRow/ExecutionStatusPanel/ExecutionStatusBody/PlaybackUtilRow/ReplayButton")
            ?? CreatePlaybackButton("ReplayButton", "Replay");

        if (_runButton is not null)
        {
            RunPipelineButtonStyle.Apply(_runButton);
            if (!_runButton.IsConnected(Button.SignalName.Pressed, Callable.From(OnRunButtonPressed)))
            {
                _runButton.Pressed += OnRunButtonPressed;
            }
        }

        WirePlaybackButtons();
        UpdatePlaybackControlsUi();
    }

    private void WirePlaybackButtons()
    {
        if (!_stepBackButton.IsConnected(Button.SignalName.Pressed, Callable.From(OnStepBackPressed)))
        {
            _stepBackButton.Pressed += OnStepBackPressed;
        }

        if (!_playPauseButton.IsConnected(Button.SignalName.Pressed, Callable.From(OnPlayPausePressed)))
        {
            _playPauseButton.Pressed += OnPlayPausePressed;
        }

        if (!_stepForwardButton.IsConnected(Button.SignalName.Pressed, Callable.From(OnStepForwardPressed)))
        {
            _stepForwardButton.Pressed += OnStepForwardPressed;
        }

        if (!_speedButton.IsConnected(Button.SignalName.Pressed, Callable.From(OnSpeedPressed)))
        {
            _speedButton.Pressed += OnSpeedPressed;
        }

        if (!_replayButton.IsConnected(Button.SignalName.Pressed, Callable.From(OnReplayPressed)))
        {
            _replayButton.Pressed += OnReplayPressed;
        }
    }

    #endregion

    #region Level → toolbox / canvas

    private void ApplyLevelHeader(LevelData level)
    {
        _titleLabel.Text = level.Title.ToUpperInvariant();
        _descriptionLabel.Text = level.Description;
        RefreshObjectiveChips(level);
        UpdateMeterLabels(0, 0);
    }

    private void RefreshObjectiveChips(LevelData level)
    {
        ReplaceChipsHost(_inputPreviewChipsHost, level.InputStream);
        ReplaceChipsHost(_targetPreviewChipsHost, level.ExpectedOutput);
    }

    private static void ReplaceChipsHost(Control host, IReadOnlyList<int> values)
    {
        foreach (Node child in host.GetChildren())
        {
            child.QueueFree();
        }

        var chips = new PipelineStreamChips { Name = "Chips" };
        chips.SetValues(values);
        host.AddChild(chips);
    }

    private void ConfigureMeters(LevelData level)
    {
        _cpuMeter.MaxValue = level.Budgets.MaxCPUCycles;
        _cpuMeter.Value = 0;
        _ramMeter.MaxValue = level.Budgets.MaxRAMSlots;
        _ramMeter.Value = 0;
        _cpuInDangerZone = false;
        _cpuMeter.Modulate = ColorIdle;
        UpdateMeterLabels(0, 0);
    }

    private void UpdateMeterLabels(float cpu, float ram)
    {
        if (_activeLevel is null)
        {
            return;
        }

        _cpuValueLabel.Text = $"CPU: {(int)cpu} / {_activeLevel.Budgets.MaxCPUCycles}";
        _ramValueLabel.Text = $"RAM: {(int)ram} / {_activeLevel.Budgets.MaxRAMSlots}";
        _cpuMeter.Value = cpu;
        _ramMeter.Value = ram;
    }

    private void SetExecutionStatus(string phase, string? machine, string operation)
    {
        if (_execPhaseLabel is not null)
        {
            _execPhaseLabel.Text = $"Phase: {phase}";
        }

        if (_execMachineLabel is not null)
        {
            _execMachineLabel.Text = $"Machine: {machine ?? "—"}";
        }

        if (_execOperationLabel is not null)
        {
            _execOperationLabel.Text = $"Operation: {operation}";
        }

        if (_statusLabel is not null)
        {
            _statusLabel.Text = operation;
        }
    }

    private static string FriendlyMachineName(string? blockId)
    {
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return "—";
        }

        if (blockId.Contains("Bubble_Sort", System.StringComparison.OrdinalIgnoreCase))
        {
            return "Bubble Sort";
        }

        return blockId.Replace('_', ' ');
    }

    private static string FriendlyNodeName(string nodeId)
    {
        if (PipelineNodeIds.IsInputStream(nodeId))
        {
            return "Input Shipment";
        }

        if (PipelineNodeIds.IsOutputSink(nodeId))
        {
            return "Output";
        }

        if (nodeId.StartsWith("Engine_", System.StringComparison.Ordinal))
        {
            return FriendlyMachineName(nodeId["Engine_".Length..]);
        }

        return nodeId;
    }

    /// <summary>
    /// Instantiates toolbox entries from allowed building blocks (machines only — storage is the Input Shipment).
    /// </summary>
    private void PopulateToolbox(LevelData level)
    {
        foreach (Node child in _toolboxList.GetChildren())
        {
            child.QueueFree();
        }

        foreach (string blockId in level.AllowedToolbox)
        {
            if (blockId.Contains("Storage", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string capturedId = blockId;
            var button = new Button
            {
                Name = $"Toolbox_{capturedId}",
                Text = $"⚙  {FriendlyMachineName(capturedId)}\nInteger Stream",
                TooltipText = $"Place {FriendlyMachineName(capturedId)} on the factory floor",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(0, 56)
            };
            button.Pressed += () => OnToolboxBlockPressed(capturedId);
            _toolboxList.AddChild(button);
        }
    }

    /// <summary>
    /// Seeds Input Shipment + Output on the factory floor. Stream values live inside those nodes —
    /// no independent Box graph machines.
    /// </summary>
    private void ResetPipelineCanvas(LevelData level)
    {
        _pipelineCanvas.ClearConnections();
        List<GraphElement> stale = new();
        foreach (Node child in _pipelineCanvas.GetChildren())
        {
            if (child is GraphElement element)
            {
                stale.Add(element);
            }
        }

        foreach (GraphElement element in stale)
        {
            element.Free();
        }

        _placedEngineNodes.Clear();
        _pipelineConnections.Clear();
        _inputStreamNode = null;
        _outputSinkNode = null;
        _activeEngineNode = null;
        _workingVisualValues = level.InputStream.ToArray();
        SyncPipelineRoutesToEngine();

        _pipelineCanvas.ScrollOffset = Vector2.Zero;
        _pipelineCanvas.Zoom = 1f;

        var inputNode = new PipelineInputStreamGraphNode();
        inputNode.Name = PipelineNodeIds.InputStream;
        inputNode.Initialize(level.InputStream);
        PlaceGraphNode(inputNode, new Vector2(40f, 60f), locked: false);
        _inputStreamNode = inputNode;

        var outputNode = new PipelineOutputSinkGraphNode();
        outputNode.Name = PipelineNodeIds.OutputSink;
        outputNode.Initialize(level.ExpectedOutput);
        PlaceGraphNode(outputNode, new Vector2(520f, 60f), locked: false);
        _outputSinkNode = outputNode;

        CallDeferred(MethodName.ResyncAllGraphNodePositions);
    }

    /// <summary>
    /// Adds a GraphNode to the factory floor and forces screen Position to match PositionOffset.
    /// GraphEdit only reapplies offsets during deferred scroll updates — without this, nodes
    /// stack on top of each other at the origin and cannot be dragged apart.
    /// </summary>
    private void PlaceGraphNode(GraphNode node, Vector2 graphOffset, bool locked)
    {
        _pipelineCanvas.AddChild(node);
        ConfigureGraphElementInteraction(node, locked);
        ApplyGraphNodePlacement(node, graphOffset);
    }

    private void ApplyGraphNodePlacement(GraphElement node, Vector2 graphOffset)
    {
        node.PositionOffset = graphOffset;
        SyncGraphNodeScreenPosition(node);
    }

    private void SyncGraphNodeScreenPosition(GraphElement node)
    {
        if (_pipelineCanvas is null || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        float zoom = Math.Max(_pipelineCanvas.Zoom, 0.01f);
        node.Scale = new Vector2(zoom, zoom);
        node.Position = node.PositionOffset * zoom - _pipelineCanvas.ScrollOffset;
    }

    private void ResyncAllGraphNodePositions()
    {
        if (_pipelineCanvas is null)
        {
            return;
        }

        // Nudge GraphEdit's internal scroll sync, then enforce our positions.
        _pipelineCanvas.ScrollOffset = _pipelineCanvas.ScrollOffset;
        foreach (Node child in _pipelineCanvas.GetChildren())
        {
            if (child is GraphElement element)
            {
                SyncGraphNodeScreenPosition(element);
                if (element is GraphNode graphNode)
                {
                    PipelineGraphNodeTheme.ConfigureInteractiveNode(graphNode, locked: _lifecycle.IsGraphLocked);
                }
            }
        }
    }

    private static string FormatToolboxLabel(string blockId) => FriendlyMachineName(blockId);

    #endregion

    #region GraphEdit wiring

    /// <summary>
    /// Thick glowing connection wires + touch-friendly hotzones on the GraphEdit canvas.
    /// </summary>
    private void ApplyGraphEditChrome()
    {
        _pipelineCanvas.ConnectionLinesThickness = 6f;
        _pipelineCanvas.ConnectionLinesAntialiased = true;
        _pipelineCanvas.AddThemeColorOverride("connection_valid", ColorNeonWire);
        _pipelineCanvas.AddThemeColorOverride("connection_rim", ColorWireGlow);
        _pipelineCanvas.AddThemeColorOverride("activity", ColorCyanBorder);
        _pipelineCanvas.AddThemeColorOverride("grid_major", new Color(0.18f, 0.22f, 0.28f));
        _pipelineCanvas.AddThemeColorOverride("grid_minor", new Color(0.12f, 0.15f, 0.19f));
        // Port hotzones extend outside the node so GraphEdit's connection layer can
        // receive the press (ports live on the rim, not under the node body).
        _pipelineCanvas.AddThemeConstantOverride("port_hotzone_inner_extent", PortConnectorDiameter);
        _pipelineCanvas.AddThemeConstantOverride("port_hotzone_outer_extent", PortConnectorDiameter * 2);
        _pipelineCanvas.AddValidConnectionType(PortTypeDataStream, PortTypeDataStream);
    }

    private void WireGraphEditSignals()
    {
        _pipelineCanvas.ConnectionRequest += OnConnectionRequest;
        _pipelineCanvas.DisconnectionRequest += OnDisconnectionRequest;
    }

    /// <summary>
    /// Handles GraphEdit connection_request for stream topology:
    /// Input Stream → Machine → Output (machines may chain later).
    /// </summary>
    private void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        if (_lifecycle.RejectGraphMutation())
        {
            _statusLabel.Text = "Cannot modify connections while the pipeline is running.";
            GD.Print("[Pipeline] ConnectionRequest rejected — graph locked.");
            return;
        }

        if (fromNode == toNode)
        {
            _statusLabel.Text = "Invalid link: a node cannot connect to itself.";
            return;
        }

        if (!TryResolveConnectionEndpoints(
                fromNode, (int)fromPort, toNode, (int)toPort,
                out string rejectionReason))
        {
            _statusLabel.Text = rejectionReason;
            GD.Print($"[GraphEdit] Rejected connection {fromNode}:{fromPort} → {toNode}:{toPort} — {rejectionReason}");
            return;
        }

        var connection = new PipelineConnection(fromNode, (int)fromPort, toNode, (int)toPort);
        if (!_pipelineConnections.Add(connection))
        {
            _statusLabel.Text = "That link already exists.";
            return;
        }

        _pipelineCanvas.ConnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
        SyncPipelineRoutesToEngine();
        ReevaluatePipelineReadiness();

        _statusLabel.Text = $"Linked {FriendlyNodeName(fromNode.ToString())} → {FriendlyNodeName(toNode.ToString())}";
        GD.Print(
            $"[GraphEdit] Connected {fromNode}:{fromPort} → {toNode}:{toPort} | " +
            $"routes={_engine?.PipelineRoutes.Count ?? 0}");

        AdvanceTutorialAfterBubbleSortLink();
    }

    private void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        if (_lifecycle.RejectGraphMutation())
        {
            _statusLabel.Text = "Cannot modify connections while the pipeline is running.";
            GD.Print("[Pipeline] DisconnectionRequest rejected — graph locked.");
            return;
        }

        var connection = new PipelineConnection(fromNode, (int)fromPort, toNode, (int)toPort);
        _pipelineConnections.Remove(connection);
        _pipelineCanvas.DisconnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
        SyncPipelineRoutesToEngine();
        ReevaluatePipelineReadiness();
        _statusLabel.Text = $"Unlinked {fromNode} → {toNode}";

        if (_tutorialCoach is not null && CurrentPipelineState == PipelineState.Editing)
        {
            _tutorialCoach.ShowStep(1);
        }
    }

    /// <summary>
    /// Validates stream-level wiring: sources emit Out, sinks receive In.
    /// </summary>
    private bool TryResolveConnectionEndpoints(
        StringName fromNode,
        int fromPort,
        StringName toNode,
        int toPort,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;

        Node? from = _pipelineCanvas.GetNodeOrNull(fromNode.ToString());
        Node? to = _pipelineCanvas.GetNodeOrNull(toNode.ToString());

        if (from is PipelineDataSlotNode || to is PipelineDataSlotNode)
        {
            rejectionReason = "Cargo boxes are stream content — wire Input Stream → Machine → Output.";
            return false;
        }

        bool fromIsSource = from is PipelineInputStreamGraphNode or PipelineEngineNode;
        bool toIsSink = to is PipelineEngineNode or PipelineOutputSinkGraphNode;

        if (!fromIsSource || !toIsSink)
        {
            rejectionReason = "Wire Input Stream → Bubble Sort → Output.";
            return false;
        }

        if (from is PipelineInputStreamGraphNode && fromPort != 0)
        {
            rejectionReason = "Use the Input Stream output port.";
            return false;
        }

        if (to is PipelineOutputSinkGraphNode && toPort != 0)
        {
            rejectionReason = "Connect into the Output input port.";
            return false;
        }

        if (from is PipelineEngineNode && to is PipelineInputStreamGraphNode)
        {
            rejectionReason = "Cannot feed data back into the Input Stream.";
            return false;
        }

        if (from is PipelineOutputSinkGraphNode)
        {
            rejectionReason = "Output is a sink — it has no outgoing stream.";
            return false;
        }

        if (from is PipelineInputStreamGraphNode && to is PipelineOutputSinkGraphNode)
        {
            // Allowed structurally (Input → Output). Evaluation will fail if unsorted.
            return true;
        }

        return true;
    }

    /// <summary>
    /// Pushes the current GraphEdit edge set into <see cref="SimulationEngine"/> as logical routes.
    /// </summary>
    private void SyncPipelineRoutesToEngine()
    {
        if (_engine is null)
        {
            return;
        }

        var routes = new List<PipelineRouteBinding>(_pipelineConnections.Count);
        foreach (PipelineConnection edge in _pipelineConnections)
        {
            string fromName = edge.FromNode.ToString();
            string toName = edge.ToNode.ToString();

            string machineId = string.Empty;
            if (toName.StartsWith("Engine_", StringComparison.Ordinal))
            {
                machineId = toName["Engine_".Length..];
            }
            else if (_pipelineCanvas.GetNodeOrNull(toName) is PipelineEngineNode engine)
            {
                machineId = engine.BlockId;
            }

            routes.Add(new PipelineRouteBinding
            {
                SourceNodeId = fromName,
                SourcePort = edge.FromPort,
                TargetNodeId = toName,
                TargetPort = edge.ToPort,
                TargetMachineId = machineId,
                SourceDataSlotIndex = null
            });
        }

        _engine.SetPipelineConnections(routes);
    }

    /// <summary>
    /// Places a computational engine GraphNode when the player taps a toolbox block.
    /// </summary>
    private void OnToolboxBlockPressed(string blockId)
    {
        if (_activeLevel is null)
        {
            return;
        }

        if (_lifecycle.RejectGraphMutation())
        {
            _statusLabel.Text = "Cannot add components while the pipeline is running.";
            GD.Print("[Pipeline] Toolbox placement rejected — graph locked.");
            return;
        }

        if (blockId.Contains("Storage", StringComparison.OrdinalIgnoreCase))
        {
            _statusLabel.Text = "Storage is shown as the Input Stream and cargo boxes on the canvas.";
            return;
        }

        if (_placedEngineNodes.ContainsKey(blockId))
        {
            _statusLabel.Text = $"{FormatToolboxLabel(blockId)} is already placed.";
            return;
        }

        var engineNode = new PipelineEngineNode();
        engineNode.Name = $"Engine_{blockId}";
        engineNode.Initialize(blockId, _activeLevel.InputStream);
        PlaceGraphNode(engineNode, new Vector2(280f, 60f), locked: _lifecycle.IsGraphLocked);
        _placedEngineNodes[blockId] = engineNode.Name;
        _activeEngineNode = engineNode;
        CallDeferred(MethodName.ResyncAllGraphNodePositions);
        ReevaluatePipelineReadiness();
        SetExecutionStatus("BUILDING", FriendlyMachineName(blockId), "Connect Input Shipment → this machine → Output.");
        _tutorialCoach?.ShowStep(1);
    }

    /// <summary>
    /// Shared oversized circular port icon for smartphone-friendly connector targets.
    /// </summary>
    internal static Texture2D GetTouchPortTexture()
    {
        if (_sharedPortTexture is not null && GodotObject.IsInstanceValid(_sharedPortTexture))
        {
            return _sharedPortTexture;
        }

        int size = PortConnectorDiameter;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));
        float radius = size * 0.5f - 1f;
        Vector2 center = new(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = center.DistanceTo(new Vector2(x + 0.5f, y + 0.5f));
                if (dist <= radius)
                {
                    bool rim = dist >= radius - 2.5f;
                    image.SetPixel(x, y, rim ? ColorCyanBorder.Lightened(0.25f) : ColorNeonWire);
                }
            }
        }

        _sharedPortTexture = ImageTexture.CreateFromImage(image);
        return _sharedPortTexture;
    }

    #endregion

    #region Simulation run + event visuals

    private void SubscribeSimulationEvents()
    {
        SimulationEvents.OnElementCompared += HandleElementCompared;
        SimulationEvents.OnElementsSwapped += HandleElementsSwapped;
        SimulationEvents.OnElementAllocated += HandleElementAllocated;
        SimulationEvents.OnMetricsUpdated += HandleMetricsUpdated;
        SimulationEvents.OnSimulationEnded += HandleSimulationEnded;
        SimulationEvents.OnNodeExecutionStarted += HandleNodeExecutionStarted;
        SimulationEvents.OnNodeExecutionCompleted += HandleNodeExecutionCompleted;
        SimulationEvents.OnDataStreamProduced += HandleDataStreamProduced;
        SimulationEvents.OnPipelineConnectionTraversed += HandlePipelineConnectionTraversed;
        SimulationEvents.OnExecutionBlocked += HandleExecutionBlocked;
        SimulationEvents.OnNodeExecutionStep += HandleNodeExecutionStep;
        SimulationEvents.OnTracePositionChanged += HandleTracePositionChanged;
        SimulationEvents.OnTraceStepApplied += HandleTraceStepApplied;
    }

    private void UnsubscribeSimulationEvents()
    {
        SimulationEvents.OnElementCompared -= HandleElementCompared;
        SimulationEvents.OnElementsSwapped -= HandleElementsSwapped;
        SimulationEvents.OnElementAllocated -= HandleElementAllocated;
        SimulationEvents.OnMetricsUpdated -= HandleMetricsUpdated;
        SimulationEvents.OnSimulationEnded -= HandleSimulationEnded;
        SimulationEvents.OnNodeExecutionStarted -= HandleNodeExecutionStarted;
        SimulationEvents.OnNodeExecutionCompleted -= HandleNodeExecutionCompleted;
        SimulationEvents.OnDataStreamProduced -= HandleDataStreamProduced;
        SimulationEvents.OnPipelineConnectionTraversed -= HandlePipelineConnectionTraversed;
        SimulationEvents.OnExecutionBlocked -= HandleExecutionBlocked;
        SimulationEvents.OnNodeExecutionStep -= HandleNodeExecutionStep;
        SimulationEvents.OnTracePositionChanged -= HandleTracePositionChanged;
        SimulationEvents.OnTraceStepApplied -= HandleTraceStepApplied;
        _storyPresenter?.Cancel();
    }

    private void HandleTraceStepApplied(ExecutionStep step, bool movingForward)
    {
        // Storytelling is presentation-only; simulation already advanced authoritatively.
        Callable.From(() =>
        {
            if (_storyPresenter is null)
            {
                return;
            }

            float speed = _engine?.PlaybackSpeed ?? _presentationSpeed;
            _ = _storyPresenter.PresentAsync(step, movingForward, speed);
        }).CallDeferred();
    }

    private async void OnRunButtonPressed()
    {
        if (_activeLevel is null || _engine is null)
        {
            return;
        }

        if (_lifecycle.BlocksNewExecution || CurrentPipelineState != PipelineState.Ready)
        {
            SetExecutionStatus(
                "NOT READY",
                FriendlyMachineName(_activeEngineNode?.BlockId),
                CurrentPipelineState == PipelineState.AwaitingReplayChoice
                    || CurrentPipelineState == PipelineState.PlayingTrace
                    ? "Simulation already finished — choose Show Me or Skip."
                    : "Connect Input → Bubble Sort → Output before executing.");
            return;
        }

        SyncPipelineRoutesToEngine();
        SetPipelineState(PipelineState.Running);
        _tutorialCoach?.ShowStep(4);
        SetExecutionStatus("EXECUTING", "—", "Simulating factory…");
        ClearAllHighlights();
        _workingVisualValues = _activeLevel.InputStream.ToArray();
        _activeEngineNode?.SetWorkingValues(_workingVisualValues);
        _lastResult = null;
        DismissCreatorCardPopup();
        UpdatePlaybackControlsUi();

        try
        {
            // Record + evaluate only. Educational playback is optional (SHOW ME).
            SimulationResult result = await _engine
                .RunPipelineAsync(TimeSpan.FromMilliseconds(1))
                .ConfigureAwait(true);
            _lastResult = result;

            if (!PipelineLifecycle.HasWatchableTrace(result.Trace))
            {
                ApplySkippedFinalPresentation(result, offerWatchLater: false);
                return;
            }

            _lifecycle.EnterAwaitingReplayChoice();
            ShowReplayChoiceDialog(result);
        }
        catch (Exception ex)
        {
            GD.PushError($"Pipeline simulation failed: {ex}");
            SetExecutionStatus("ERROR", "—", "Something went wrong running the factory.");
            if (CurrentPipelineState == PipelineState.Running)
            {
                _lifecycle.ApplySimulationOutcome(isSuccess: false);
            }
        }
        finally
        {
            UpdatePlaybackControlsUi();
        }
    }

    private void OnStepBackPressed()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.Pause();
        if (_engine.StepBackward())
        {
            UpdateMeterLabels(_engine.CurrentCPUCycles, _engine.CurrentRAMSlots);
            if (_tracePositionLabel is not null)
            {
                _tracePositionLabel.Text = _engine.PositionLabel;
            }

            SetExecutionStatus("PAUSED", FriendlyMachineName(_activeEngineNode?.BlockId), _engine.PositionLabel);
        }

        UpdatePlaybackControlsUi();
    }

    private void OnStepForwardPressed()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.Pause();
        if (_engine.StepForward())
        {
            UpdateMeterLabels(_engine.CurrentCPUCycles, _engine.CurrentRAMSlots);
            if (_tracePositionLabel is not null)
            {
                _tracePositionLabel.Text = _engine.PositionLabel;
            }

            SetExecutionStatus("PAUSED", FriendlyMachineName(_activeEngineNode?.BlockId), _engine.PositionLabel);
        }

        UpdatePlaybackControlsUi();
    }

    private void OnPlayPausePressed()
    {
        if (_engine is null)
        {
            return;
        }

        if (_engine.IsPaused || !_engine.IsPlaying)
        {
            _engine.Resume();
            if (_playPauseButton is not null)
            {
                _playPauseButton.Text = "⏸ Pause";
            }

            if (!_engine.IsPlaying && _engine.Playback is not null && !_engine.Playback.IsAtEnd)
            {
                _playbackCts?.Cancel();
                _playbackCts = new CancellationTokenSource();
                _ = ContinuePlaybackAsync(_playbackCts.Token);
            }
        }
        else
        {
            _engine.Pause();
            if (_playPauseButton is not null)
            {
                _playPauseButton.Text = "▶ Play";
            }
        }

        UpdatePlaybackControlsUi();
    }

    private async Task ContinuePlaybackAsync(CancellationToken token)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            await _engine.PlayRecordedTraceAsync(token, resetToStart: false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            GD.PushError($"Playback continue failed: {ex.Message}");
        }
        finally
        {
            UpdatePlaybackControlsUi();
        }
    }

    private void OnSpeedPressed()
    {
        _presentationSpeed = _presentationSpeed switch
        {
            0.5f => 1f,
            1f => 2f,
            _ => 0.5f
        };
        if (_speedButton is not null)
        {
            _speedButton.Text = _presentationSpeed switch
            {
                0.5f => "0.5×",
                2f => "2×",
                _ => "1×"
            };
        }

        _engine?.SetPlaybackSpeed(_presentationSpeed);
    }

    private async void OnReplayPressed()
    {
        if (_engine?.Playback is null || _lastResult is null)
        {
            return;
        }

        if (!ExecutionTrace.IsWatchable(_lastResult.Trace))
        {
            return;
        }

        DismissCreatorCardPopup();
        _lifecycle.BeginTracePlayback();
        ClearAllHighlights();
        _playbackCts?.Cancel();
        _playbackCts = new CancellationTokenSource();
        _engine.SetPlaybackSpeed(_presentationSpeed);
        try
        {
            await _engine.ReplayAsync(_playbackCts.Token).ConfigureAwait(true);
        }
        finally
        {
            UpdatePlaybackControlsUi();
        }
    }

    private void SyncVisualsFromEngineBuffer()
    {
        if (_engine is null)
        {
            return;
        }

        _workingVisualValues = _engine.WorkingBuffer.ToArray();
        _activeEngineNode?.SetWorkingValues(_workingVisualValues);
        UpdateMeterLabels(_engine.CurrentCPUCycles, _engine.CurrentRAMSlots);
        if (_tracePositionLabel is not null)
        {
            _tracePositionLabel.Text = _engine.PositionLabel;
        }
    }

    private void HandleTracePositionChanged(int currentIndex, int totalSteps)
    {
        Callable.From(() =>
        {
            if (_tracePositionLabel is not null)
            {
                _tracePositionLabel.Text = _engine?.PositionLabel
                    ?? (totalSteps == 0
                        ? "Step 0 / 0"
                        : $"Step {Math.Max(0, currentIndex + 1)} / {totalSteps}");
            }

            UpdatePlaybackControlsUi();
        }).CallDeferred();
    }

    private void UpdatePlaybackControlsUi()
    {
        bool hasTrace = _engine?.Playback is not null;
        bool canBack = _engine?.CanStepBackward == true;
        bool canForward = _engine?.CanStepForward == true;
        bool playing = _engine?.IsPlaying == true && _engine?.IsPaused == false;

        if (_stepBackButton is not null)
        {
            _stepBackButton.Disabled = !hasTrace || !canBack;
        }

        if (_stepForwardButton is not null)
        {
            _stepForwardButton.Disabled = !hasTrace || !canForward;
        }

        if (_playPauseButton is not null)
        {
            _playPauseButton.Disabled = !hasTrace;
            _playPauseButton.Text = playing ? "⏸ Pause" : "▶ Play";
        }

        if (_replayButton is not null)
        {
            _replayButton.Disabled = !hasTrace;
        }

        if (_speedButton is not null)
        {
            _speedButton.Disabled = false;
        }
    }

    private void ApplyFailureVisuals(SimulationResult result)
    {
        string machine = FriendlyNodeName(result.Failure?.NodeId ?? result.ProducingNodeId ?? "—");
        string reason = FormatFailureForUi(result);
        SetExecutionStatus("PIPELINE STOPPED", machine, reason);
        if (result.Failure is not null)
        {
            HighlightPipelineNode(result.Failure.NodeId, ColorDangerFlash);
        }
    }

    private static string FormatFailureForUi(SimulationResult result)
    {
        if (result.Failure is null)
        {
            return result.Message;
        }

        return result.Failure.Reason;
    }

    private bool HasValidPipelineWiring() => IsPipelineReady();

    private void HandleNodeExecutionStarted(string nodeId)
    {
        // Visual narrative is owned by ExecutionStoryPresenter via OnTraceStepApplied.
        _ = nodeId;
    }

    private void HandleNodeExecutionCompleted(string nodeId)
    {
        _ = nodeId;
    }

    private void HandleNodeExecutionStep(string nodeId, string stepKind, string? detail)
    {
        _ = nodeId;
        _ = stepKind;
        _ = detail;
    }

    private void HandleDataStreamProduced(string nodeId, string portId, string dataType, IReadOnlyList<int> values)
    {
        Callable.From(() =>
        {
            _ = portId;
            _ = dataType;
            if (nodeId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase)
                || PipelineNodeIds.IsInputStream(nodeId))
            {
                _workingVisualValues = values.ToArray();
            }
        }).CallDeferred();
    }

    private void HandlePipelineConnectionTraversed(string fromNodeId, string toNodeId, string edgeId)
    {
        _ = fromNodeId;
        _ = toNodeId;
        _ = edgeId;
    }

    private void HandleExecutionBlocked(ExecutionFailure failure)
    {
        // Story presenter also handles blocked emphasis; keep a status fallback.
        Callable.From(() =>
        {
            HighlightPipelineNode(failure.NodeId, ColorDangerFlash);
            SetExecutionStatus("PIPELINE STOPPED", FriendlyNodeName(failure.NodeId), failure.Reason);
        }).CallDeferred();
    }

    private void HighlightPipelineNode(string nodeId, Color color)
    {
        Node? node = _pipelineCanvas?.GetNodeOrNull(nodeId);
        if (node is GraphNode graphNode && GodotObject.IsInstanceValid(graphNode))
        {
            graphNode.Modulate = color;
        }
    }

    private void HandleElementCompared(int indexA, int indexB)
    {
        _ = indexA;
        _ = indexB;
    }

    private void HandleElementsSwapped(int fromIndex, int toIndex)
    {
        Callable.From(() =>
        {
            if (fromIndex >= 0
                && toIndex >= 0
                && fromIndex < _workingVisualValues.Length
                && toIndex < _workingVisualValues.Length)
            {
                int temp = _workingVisualValues[fromIndex];
                _workingVisualValues[fromIndex] = _workingVisualValues[toIndex];
                _workingVisualValues[toIndex] = temp;
            }
        }).CallDeferred();
    }

    private void HandleElementAllocated(int ramSlotNumber, string valueLabel)
    {
        Callable.From(() =>
        {
            _ = ramSlotNumber;
            _ = valueLabel;
            // RAM meter is updated via OnMetricsUpdated — avoid technical slot jargon.
        }).CallDeferred();
    }

    private void HandleMetricsUpdated(float currentCpu, float currentRam)
    {
        Callable.From(() =>
        {
            UpdateMeterLabels(currentCpu, currentRam);

            if (_activeLevel is null)
            {
                return;
            }

            float ratio = currentCpu / Math.Max(1f, _activeLevel.Budgets.MaxCPUCycles);
            bool nowDanger = ratio >= CpuDangerThresholdRatio;
            if (nowDanger && !_cpuInDangerZone)
            {
                FlashCpuWarning();
            }

            _cpuInDangerZone = nowDanger;
            _cpuMeter.Modulate = nowDanger ? ColorDangerFlash : ColorIdle;
        }).CallDeferred();
    }

    private void HandleSimulationEnded(bool isSuccess, string evaluationMessage)
    {
        GD.Print($"[OnSimulationEnded] IsSuccess={isSuccess}, Message={evaluationMessage}");
        Callable.From(() =>
        {
            // Terminal event fires when educational playback finishes — not when recording ends.
            if (CurrentPipelineState != PipelineState.PlayingTrace)
            {
                return;
            }

            _lifecycle.ApplySimulationOutcome(isSuccess);
            bool canWatchAgain = _lastResult is not null
                && ExecutionTrace.IsWatchable(_lastResult.Trace);

            if (isSuccess)
            {
                ApplyFinalWorkspaceVisuals(_lastResult);
                ShowFactoryCompleteSummary(offerWatchLater: canWatchAgain);
            }
            else
            {
                FlashCpuWarning();
                if (_lastResult is not null)
                {
                    ApplyFinalWorkspaceVisuals(_lastResult);
                    ShowPipelineStoppedSummary(_lastResult, offerWatchLater: canWatchAgain);
                }
                else
                {
                    SetExecutionStatus("PIPELINE STOPPED", "—", evaluationMessage);
                }
            }
        }).CallDeferred();
    }

    /// <summary>
    /// Post-simulation choice: watch the recorded trace or skip straight to the result.
    /// </summary>
    private void ShowReplayChoiceDialog(SimulationResult result)
    {
        if (_activeLevel is null)
        {
            return;
        }

        DismissCreatorCardPopup();
        _tutorialCoach?.ShowStep(4);

        int cpu = result.CpuCyclesConsumed;
        int ram = result.PeakRamSlotsUsed;
        int maxCpu = _activeLevel.Budgets.MaxCPUCycles;
        int maxRam = _activeLevel.Budgets.MaxRAMSlots;

        string titleText;
        string bodyText;
        string promptText;
        string showLabel;
        if (result.IsSuccess)
        {
            titleText = "FACTORY COMPLETE ✓";
            bodyText =
                "Your factory produced the correct result.\n\n" +
                $"Input:\n[{string.Join(", ", _activeLevel.InputStream)}]\n\n" +
                $"Output:\n[{string.Join(", ", result.FinalOutput)}]\n\n" +
                $"CPU: {cpu} / {maxCpu}\nRAM: {ram} / {maxRam}";
            promptText = "Want to see how it worked?";
            showLabel = "SHOW ME";
            SetExecutionStatus(
                "FACTORY COMPLETE",
                FriendlyMachineName(_activeEngineNode?.BlockId),
                "Choose Show Me or Skip.");
        }
        else
        {
            titleText = "PIPELINE STOPPED";
            string reason = FormatFailureForUi(result);
            bodyText = $"{reason}\n\nCPU: {cpu} / {maxCpu}\nRAM: {ram} / {maxRam}";
            promptText = "Want to see where it happened?";
            showLabel = "SHOW ME";
            SetExecutionStatus("PIPELINE STOPPED", FriendlyNodeName(result.Failure?.NodeId ?? "—"), reason);
        }

        _creatorCardLayer = CreateResultOverlayLayer("ReplayChoiceLayer");
        PanelContainer card = CreateResultCardHost(
            _creatorCardLayer,
            cardName: "ReplayChoiceCard",
            minSize: new Vector2(420, 360));
        var body = new VBoxContainer { Name = "ReplayChoiceBody" };
        body.AddThemeConstantOverride("separation", 12);
        card.AddChild(body);

        var title = new Label
        {
            Text = titleText,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", result.IsSuccess ? ColorSwapHighlight : ColorDangerFlash);
        body.AddChild(title);

        body.AddChild(new Label
        {
            Text = bodyText,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });

        body.AddChild(new Label
        {
            Text = promptText,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });

        var buttons = new HBoxContainer
        {
            Name = "ReplayChoiceButtons",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        buttons.AddThemeConstantOverride("separation", 12);
        body.AddChild(buttons);

        var showMe = new Button
        {
            Name = "ShowMeButton",
            Text = showLabel,
            CustomMinimumSize = new Vector2(160, 52)
        };
        RunPipelineButtonStyle.Apply(showMe);
        showMe.Text = showLabel;
        showMe.Disabled = false;
        showMe.Pressed += OnShowMePressed;
        buttons.AddChild(showMe);

        var skip = new Button
        {
            Name = "SkipReplayButton",
            Text = "SKIP",
            CustomMinimumSize = new Vector2(140, 52)
        };
        RunPipelineButtonStyle.Apply(skip);
        skip.Text = "SKIP";
        skip.Disabled = false;
        skip.Pressed += OnSkipReplayPressed;
        buttons.AddChild(skip);
    }

    private void OnShowMePressed()
    {
        if (_engine is null || _lastResult is null)
        {
            return;
        }

        if (!ExecutionTrace.IsWatchable(_lastResult.Trace))
        {
            ApplySkippedFinalPresentation(_lastResult, offerWatchLater: false);
            return;
        }

        DismissCreatorCardPopup();
        _ = StartEducationalPlaybackAsync(resetToStart: true);
    }

    private void OnSkipReplayPressed()
    {
        if (_lastResult is null)
        {
            return;
        }

        ApplySkippedFinalPresentation(_lastResult, offerWatchLater: true);
    }

    private async Task StartEducationalPlaybackAsync(bool resetToStart)
    {
        if (_engine is null || _lastResult is null)
        {
            return;
        }

        _lifecycle.BeginTracePlayback();
        ClearAllHighlights();
        SetExecutionStatus("WATCHING", FriendlyMachineName(_activeEngineNode?.BlockId), "Playing recorded execution…");
        _playbackCts?.Cancel();
        _playbackCts = new CancellationTokenSource();
        _engine.SetPlaybackSpeed(_presentationSpeed);
        UpdatePlaybackControlsUi();

        try
        {
            await _engine
                .PlayRecordedTraceAsync(_playbackCts.Token, resetToStart: resetToStart)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            GD.PushError($"Educational playback failed: {ex.Message}");
            ApplySkippedFinalPresentation(_lastResult, offerWatchLater: true);
        }
        finally
        {
            UpdatePlaybackControlsUi();
        }
    }

    /// <summary>
    /// Skip / validation path: snap workspace to the recorded final result without playback.
    /// </summary>
    private void ApplySkippedFinalPresentation(SimulationResult result, bool offerWatchLater)
    {
        DismissCreatorCardPopup();
        ApplyFinalWorkspaceVisuals(result);
        _lifecycle.ApplySimulationOutcome(result.IsSuccess);

        bool canWatch = offerWatchLater && ExecutionTrace.IsWatchable(result.Trace);
        if (result.IsSuccess)
        {
            ShowFactoryCompleteSummary(offerWatchLater: canWatch);
            return;
        }

        if (!ExecutionTrace.IsWatchable(result.Trace)
            && result.State == SimulationResultState.FailedInvalidPipeline)
        {
            ShowInvalidFactoryDialog(result);
            return;
        }

        ShowPipelineStoppedSummary(result, offerWatchLater: canWatch);
    }

    private void ApplyFinalWorkspaceVisuals(SimulationResult? result)
    {
        if (result is null)
        {
            return;
        }

        ClearAllHighlights();
        _engine?.SnapToFinalResultState();
        _workingVisualValues = result.FinalOutput.ToArray();
        _activeEngineNode?.SetWorkingValues(_workingVisualValues);
        _outputSinkNode?.SetReceivedValues(_workingVisualValues);
        UpdateMeterLabels(result.CpuCyclesConsumed, result.PeakRamSlotsUsed);

        if (_tracePositionLabel is not null && _engine is not null)
        {
            _tracePositionLabel.Text = _engine.PositionLabel;
        }

        if (result.IsSuccess)
        {
            PlaySuccessScalePop();
        }
        else if (result.Failure is not null)
        {
            HighlightPipelineNode(result.Failure.NodeId, ColorDangerFlash);
            ApplyFailureVisuals(result);
        }

        UpdatePlaybackControlsUi();
    }

    private CanvasLayer CreateResultOverlayLayer(string name)
    {
        var layer = new CanvasLayer
        {
            Name = name,
            Layer = 100
        };
        AddChild(layer);
        return layer;
    }

    /// <summary>
    /// Centers a result card above the lower HUD / tutorial reserved band.
    /// </summary>
    private PanelContainer CreateResultCardHost(CanvasLayer layer, string cardName, Vector2 minSize)
    {
        var dimmer = new ColorRect
        {
            Name = "Dimmer",
            Color = new Color(0f, 0f, 0f, 0.55f)
        };
        dimmer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Leave the lower HUD / TutorialCoach band clear (same reserved strip as coach OffsetBottom).
        dimmer.OffsetBottom = -88f;
        layer.AddChild(dimmer);

        var margin = new MarginContainer { Name = "ResultMargin" };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.OffsetBottom = -88f;
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        layer.AddChild(margin);

        var center = new CenterContainer { Name = "ResultCenter" };
        center.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        center.SizeFlagsVertical = SizeFlags.ExpandFill;
        margin.AddChild(center);

        var card = new PanelContainer
        {
            Name = cardName,
            CustomMinimumSize = minSize
        };
        center.AddChild(card);
        return card;
    }

    /// <summary>
    /// Player-facing success board: input, output, CPU/RAM, optional watch-again, then Continue.
    /// </summary>
    private void ShowFactoryCompleteSummary(bool offerWatchLater)
    {
        if (_activeLevel is null)
        {
            return;
        }

        DismissCreatorCardPopup();

        IReadOnlyList<int> output = _lastResult?.FinalOutput ?? _activeLevel.ExpectedOutput;
        int cpu = _lastResult?.CpuCyclesConsumed ?? (int)_cpuMeter.Value;
        int ram = _lastResult?.PeakRamSlotsUsed ?? (int)_ramMeter.Value;

        SetExecutionStatus(
            "FACTORY COMPLETE",
            FriendlyMachineName(_activeEngineNode?.BlockId),
            $"Output [{string.Join(", ", output)}]  ·  CPU {cpu}/{_activeLevel.Budgets.MaxCPUCycles}");

        _creatorCardLayer = CreateResultOverlayLayer("FactoryCompleteLayer");
        PanelContainer card = CreateResultCardHost(
            _creatorCardLayer,
            cardName: "CompleteCard",
            minSize: new Vector2(400, 320));
        var body = new VBoxContainer { Name = "CompleteBody" };
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        var title = new Label
        {
            Text = "FACTORY COMPLETE ✓",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", ColorSwapHighlight);
        body.AddChild(title);

        body.AddChild(new Label
        {
            Text = $"Input:\n[{string.Join(", ", _activeLevel.InputStream)}]",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        body.AddChild(new Label
        {
            Text = $"Output:\n[{string.Join(", ", output)}]",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        body.AddChild(new Label
        {
            Text = $"CPU: {cpu} / {_activeLevel.Budgets.MaxCPUCycles}\nRAM: {ram} / {_activeLevel.Budgets.MaxRAMSlots}",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        if (offerWatchLater && _lastResult is not null && ExecutionTrace.IsWatchable(_lastResult.Trace))
        {
            var watchAgain = new Button
            {
                Name = "WatchHowItWorkedButton",
                Text = "WATCH HOW IT WORKED",
                CustomMinimumSize = new Vector2(280, 48)
            };
            RunPipelineButtonStyle.Apply(watchAgain);
            watchAgain.Text = "WATCH HOW IT WORKED";
            watchAgain.Disabled = false;
            watchAgain.Pressed += () =>
            {
                DismissCreatorCardPopup();
                _ = StartEducationalPlaybackAsync(resetToStart: true);
            };
            body.AddChild(watchAgain);
        }

        var continueButton = new Button
        {
            Name = "ContinueButton",
            Text = "CONTINUE",
            CustomMinimumSize = new Vector2(200, 48)
        };
        RunPipelineButtonStyle.Apply(continueButton);
        continueButton.Text = "CONTINUE";
        continueButton.Disabled = false;
        continueButton.Pressed += () =>
        {
            DismissCreatorCardPopup();
            InstantiateWinEvaluationPopup("Factory complete");
        };
        body.AddChild(continueButton);
    }

    private void ShowPipelineStoppedSummary(SimulationResult result, bool offerWatchLater)
    {
        if (_activeLevel is null)
        {
            return;
        }

        DismissCreatorCardPopup();
        string reason = FormatFailureForUi(result);
        SetExecutionStatus("PIPELINE STOPPED", FriendlyNodeName(result.Failure?.NodeId ?? "—"), reason);

        _creatorCardLayer = CreateResultOverlayLayer("PipelineStoppedLayer");
        PanelContainer card = CreateResultCardHost(
            _creatorCardLayer,
            cardName: "StoppedCard",
            minSize: new Vector2(400, 280));
        var body = new VBoxContainer { Name = "StoppedBody" };
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        var title = new Label
        {
            Text = "PIPELINE STOPPED",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", ColorDangerFlash);
        body.AddChild(title);

        body.AddChild(new Label
        {
            Text = $"Reason:\n{reason}\n\nCPU: {result.CpuCyclesConsumed} / {_activeLevel.Budgets.MaxCPUCycles}",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });

        if (offerWatchLater && ExecutionTrace.IsWatchable(result.Trace))
        {
            var watchAgain = new Button
            {
                Name = "WatchHowItWorkedButton",
                Text = "WATCH HOW IT WORKED",
                CustomMinimumSize = new Vector2(280, 48)
            };
            RunPipelineButtonStyle.Apply(watchAgain);
            watchAgain.Text = "WATCH HOW IT WORKED";
            watchAgain.Disabled = false;
            watchAgain.Pressed += () =>
            {
                DismissCreatorCardPopup();
                _ = StartEducationalPlaybackAsync(resetToStart: true);
            };
            body.AddChild(watchAgain);
        }

        var fixButton = new Button
        {
            Name = "FixFactoryButton",
            Text = "FIX FACTORY",
            CustomMinimumSize = new Vector2(200, 48)
        };
        RunPipelineButtonStyle.Apply(fixButton);
        fixButton.Text = "FIX FACTORY";
        fixButton.Disabled = false;
        fixButton.Pressed += () =>
        {
            DismissCreatorCardPopup();
            SetExecutionStatus("BUILDING", FriendlyMachineName(_activeEngineNode?.BlockId), "Fix the factory and try again.");
            _tutorialCoach?.ShowStep(3);
        };
        body.AddChild(fixButton);
    }

    private void ShowInvalidFactoryDialog(SimulationResult result)
    {
        DismissCreatorCardPopup();
        string reason = FormatFailureForUi(result);
        SetExecutionStatus("INVALID FACTORY", FriendlyNodeName(result.Failure?.NodeId ?? "—"), reason);

        _creatorCardLayer = CreateResultOverlayLayer("InvalidFactoryLayer");
        PanelContainer card = CreateResultCardHost(
            _creatorCardLayer,
            cardName: "InvalidCard",
            minSize: new Vector2(400, 220));
        var body = new VBoxContainer { Name = "InvalidBody" };
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        var title = new Label
        {
            Text = "INVALID FACTORY",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", ColorDangerFlash);
        body.AddChild(title);

        body.AddChild(new Label
        {
            Text = reason,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });

        var fixButton = new Button
        {
            Name = "FixFactoryButton",
            Text = "FIX FACTORY",
            CustomMinimumSize = new Vector2(200, 48)
        };
        RunPipelineButtonStyle.Apply(fixButton);
        fixButton.Text = "FIX FACTORY";
        fixButton.Disabled = false;
        fixButton.Pressed += () =>
        {
            DismissCreatorCardPopup();
            SetExecutionStatus("BUILDING", FriendlyMachineName(_activeEngineNode?.BlockId), "Connect the missing ports.");
            _tutorialCoach?.ShowStep(1);
        };
        body.AddChild(fixButton);
    }

    /// <summary>
    /// Builds the Algorithm Creator Card popup after a successful factory summary Continue.
    /// </summary>
    private void InstantiateWinEvaluationPopup(string evaluationMessage)
    {
        DismissCreatorCardPopup();

        if (_activeLevel?.OptimalCreator is null)
        {
            SetExecutionStatus("FACTORY COMPLETE", "—", evaluationMessage);
            return;
        }

        CreatorMetadata creator = _activeLevel.OptimalCreator;

        _creatorCardLayer = new CanvasLayer
        {
            Name = "CreatorCardLayer",
            Layer = 100
        };
        AddChild(_creatorCardLayer);

        var dimmer = new ColorRect
        {
            Name = "Dimmer",
            Color = new Color(0f, 0f, 0f, 0.55f)
        };
        dimmer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _creatorCardLayer.AddChild(dimmer);

        var center = new CenterContainer
        {
            Name = "CreatorCardCenter"
        };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _creatorCardLayer.AddChild(center);

        var card = new PanelContainer
        {
            Name = "CreatorCard",
            CustomMinimumSize = new Vector2(420, 360)
        };
        center.AddChild(card);

        var body = new VBoxContainer
        {
            Name = "CreatorCardBody"
        };
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        body.AddChild(new Label
        {
            Name = "CreatorCardTitle",
            Text = "Algorithm Creator Unlocked!",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var avatarRect = new TextureRect
        {
            Name = "CreatorAvatarTextureRect",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(128, 128),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter
        };
        avatarRect.Texture = LoadCreatorAvatarTexture(creator.AvatarTexturePath);
        body.AddChild(avatarRect);

        body.AddChild(new Label
        {
            Name = "InventorNameLabel",
            Text = creator.InventorName,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        body.AddChild(new Label
        {
            Name = "AlgorithmMetaLabel",
            Text = $"{creator.AlgorithmName}  ·  {creator.InventionYear}",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        body.AddChild(new Label
        {
            Name = "KidFriendlyStoryLabel",
            Text = creator.KidFriendlyStory,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        body.AddChild(new Label
        {
            Name = "HistoricalContextLabel",
            Text = creator.HistoricalContext,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.85f, 0.85f, 0.90f)
        });

        var closeButton = new Button
        {
            Name = "CloseCreatorCardButton",
            Text = "Continue"
        };
        closeButton.Pressed += DismissCreatorCardPopup;
        body.AddChild(closeButton);

        card.PivotOffset = card.CustomMinimumSize * 0.5f;
        card.Scale = new Vector2(0.85f, 0.85f);
        Tween tween = CreateTween();
        tween.TweenProperty(card, "scale", Vector2.One, 0.28f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Loads a creator avatar via <see cref="GD.Load{T}(string)"/> at runtime.
    /// Falls back to the default architect portrait when the path is blank, missing, or unloadable.
    /// </summary>
    private static Texture2D LoadCreatorAvatarTexture(string? avatarTexturePath)
    {
        Texture2D? texture = TryLoadTexture2D(avatarTexturePath);
        if (texture is not null)
        {
            return texture;
        }

        texture = TryLoadTexture2D(DefaultAvatarTexturePath);
        if (texture is not null)
        {
            return texture;
        }

        // Absolute last resort: procedural portrait so the popup never crashes the game.
        GD.PushWarning(
            $"Creator avatar missing for '{avatarTexturePath}' and default '{DefaultAvatarTexturePath}'. " +
            "Using procedural fallback texture.");
        return CreateProceduralDefaultAvatarTexture();
    }

    private static Texture2D CreateProceduralDefaultAvatarTexture()
    {
        const int size = 128;
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0.27f, 0.43f, 0.63f));

        // Simple silhouette: head + shoulders.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = x - size / 2;
                int dyHead = y - 48;
                int dyBody = y - 90;
                bool head = dx * dx + dyHead * dyHead <= 22 * 22;
                bool body = Math.Abs(dx) < 28 && y >= 70 && y <= 118;
                if (head)
                {
                    image.SetPixel(x, y, new Color(0.92f, 0.78f, 0.66f));
                }
                else if (body)
                {
                    image.SetPixel(x, y, new Color(0.35f, 0.55f, 0.78f));
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D? TryLoadTexture2D(string? resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return null;
        }

        if (!ResourceLoader.Exists(resourcePath))
        {
            GD.PushWarning($"Avatar texture path does not exist: {resourcePath}");
            return null;
        }

        try
        {
            // GD.Load resolves the resource synchronously on the main thread at runtime.
            return GD.Load<Texture2D>(resourcePath);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to GD.Load avatar '{resourcePath}': {ex.Message}");
            return null;
        }
    }

    private void DismissCreatorCardPopup()
    {
        if (_creatorCardLayer is not null && GodotObject.IsInstanceValid(_creatorCardLayer))
        {
            _creatorCardLayer.QueueFree();
        }

        _creatorCardLayer = null;
    }

    private void ClearAllHighlights()
    {
        _inputStreamNode?.ClearHighlights();
        _outputSinkNode?.ClearHighlights();
        _activeEngineNode?.ClearHighlights();

        if (_pipelineCanvas is null)
        {
            return;
        }

        foreach (Node child in _pipelineCanvas.GetChildren())
        {
            if (child is GraphNode graphNode && GodotObject.IsInstanceValid(graphNode))
            {
                graphNode.Modulate = ColorIdle;
            }
        }
    }

    private void FlashCpuWarning()
    {
        Tween tween = CreateTween();
        tween.TweenProperty(_cpuMeter, "modulate", ColorDangerFlash, 0.08f);
        tween.TweenProperty(_cpuMeter, "modulate", ColorIdle, 0.18f);
        tween.SetLoops(3);
    }

    private void PlaySuccessScalePop()
    {
        _inputStreamNode?.PlaySuccessPop();
        _activeEngineNode?.PlaySuccessPop();
        _outputSinkNode?.PlaySuccessPop();
    }

    #endregion
}

/// <summary>
/// Immutable key for a GraphEdit connection edge mapped into the logical pipeline.
/// </summary>
public readonly record struct PipelineConnection(
    StringName FromNode,
    int FromPort,
    StringName ToNode,
    int ToPort);

/// <summary>
/// Shared StyleBox / theme helpers that strip Godot's default gray GraphNode chrome
/// and apply the industrial cyan border palette with mobile-sized ports.
/// </summary>
public static class PipelineGraphNodeTheme
{
    private static readonly Color PanelFill = new("1F242C");
    private static readonly Color TitleFill = new("161A20");
    private static readonly Color CyanBorder = new("219EBC");
    private static readonly Color TextWhite = new("F8F9FA");

    /// <summary>
    /// Replaces default GraphNode panel/titlebar styles and loosens content margins.
    /// </summary>
    public static void ApplyModernChrome(GraphNode node, Color accentBorder)
    {
        var panel = new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = accentBorder,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomRight = 10,
            CornerRadiusBottomLeft = 10,
            ShadowSize = 5,
            ShadowColor = new Color(0f, 0f, 0f, 0.4f),
            ShadowOffset = new Vector2(0, 3),
            ContentMarginLeft = 14,
            ContentMarginTop = 12,
            ContentMarginRight = 14,
            ContentMarginBottom = 14
        };

        var titlebar = new StyleBoxFlat
        {
            BgColor = TitleFill,
            BorderColor = accentBorder.Darkened(0.15f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 0,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomRight = 0,
            CornerRadiusBottomLeft = 0,
            ContentMarginLeft = 12,
            ContentMarginTop = 8,
            ContentMarginRight = 12,
            ContentMarginBottom = 8
        };

        var titlebarSelected = (StyleBoxFlat)titlebar.Duplicate();
        titlebarSelected.BorderColor = accentBorder.Lightened(0.2f);
        titlebarSelected.BgColor = TitleFill.Lightened(0.05f);

        var panelSelected = (StyleBoxFlat)panel.Duplicate();
        panelSelected.BorderColor = accentBorder.Lightened(0.25f);
        panelSelected.ShadowSize = 8;

        // Strip default gray window frames.
        node.AddThemeStyleboxOverride("panel", panel);
        node.AddThemeStyleboxOverride("panel_selected", panelSelected);
        node.AddThemeStyleboxOverride("titlebar", titlebar);
        node.AddThemeStyleboxOverride("titlebar_selected", titlebarSelected);
        node.AddThemeColorOverride("title_color", TextWhite);
        node.AddThemeConstantOverride("separation", 12);
        node.AddThemeConstantOverride("port_h_offset", 4);

        node.Draggable = true;
        node.Selectable = true;
    }

    /// <summary>
    /// Enables factory-floor interaction after quizzes:
    /// - Body-center drag via GuiInput + workspace _Input (GraphEdit native drag is unreliable)
    /// - Port-rim wiring left alone (no GraphEdit.GuiInput AcceptEvent steal)
    /// </summary>
    public static void ConfigureInteractiveNode(GraphNode node, bool locked)
    {
        node.Draggable = !locked;
        node.Selectable = !locked;
        // Pass keeps GraphEdit port hotzones on the outside rim working.
        node.MouseFilter = Control.MouseFilterEnum.Pass;
        MakeContentMousePassThrough(node);
        EnsureBodyDragHandler(node);
    }

    /// <summary>
    /// Lets presses hit the GraphNode body instead of labels/chips.
    /// </summary>
    public static void MakeContentMousePassThrough(GraphNode node)
    {
        foreach (Node child in node.GetChildren())
        {
            SetMouseIgnoreRecursive(child);
        }
    }

    private static GraphNode? _activeDragNode;

    /// <summary>Keep left/right edges free so GraphEdit can start port connections.</summary>
    private const float PortEdgeReservePx = 48f;

    private static void EnsureBodyDragHandler(GraphNode node)
    {
        if (node.HasMeta(DragWiredMeta))
        {
            return;
        }

        node.SetMeta(DragWiredMeta, true);
        node.GuiInput += (InputEvent inputEvent) => OnNodeBodyGuiInput(node, inputEvent);
    }

    /// <summary>
    /// Continues an active body-drag from <see cref="PipelineWorkspace._Input"/>.
    /// </summary>
    public static bool TryHandleActiveDrag(InputEvent inputEvent)
    {
        if (_activeDragNode is null || !GodotObject.IsInstanceValid(_activeDragNode))
        {
            _activeDragNode = null;
            return false;
        }

        GraphNode node = _activeDragNode;
        if (!node.Draggable)
        {
            EndDrag(node);
            return false;
        }

        if (inputEvent is InputEventMouseButton mouseButton
            && mouseButton.ButtonIndex == MouseButton.Left
            && !mouseButton.Pressed)
        {
            EndDrag(node);
            return true;
        }

        if (inputEvent is InputEventMouseMotion mouseMotion && IsDragging(node))
        {
            ApplyDragMotion(node, mouseMotion.GlobalPosition);
            return true;
        }

        return false;
    }

    private static void OnNodeBodyGuiInput(GraphNode node, InputEvent inputEvent)
    {
        if (!node.Draggable || !GodotObject.IsInstanceValid(node))
        {
            return;
        }

        if (inputEvent is not InputEventMouseButton mouseButton
            || mouseButton.ButtonIndex != MouseButton.Left
            || !mouseButton.Pressed)
        {
            return;
        }

        // Only the center of the machine starts a drag — left/right rims are for wiring.
        if (!IsBodyCenterPress(node, mouseButton.Position))
        {
            return;
        }

        BeginDrag(node, mouseButton.GlobalPosition);
        node.AcceptEvent();
    }

    private static bool IsBodyCenterPress(GraphNode node, Vector2 localPos)
    {
        float width = Math.Max(node.Size.X, 1f);
        return localPos.X > PortEdgeReservePx && localPos.X < width - PortEdgeReservePx;
    }

    private static void BeginDrag(GraphNode node, Vector2 globalMouse)
    {
        node.SetMeta(DraggingMeta, true);
        node.SetMeta(DragStartMouseMeta, globalMouse);
        node.SetMeta(DragStartOffsetMeta, node.PositionOffset);
        node.Selected = true;
        node.MoveToFront();
        _activeDragNode = node;
    }

    private static void EndDrag(GraphNode node)
    {
        if (GodotObject.IsInstanceValid(node))
        {
            node.SetMeta(DraggingMeta, false);
        }

        _activeDragNode = null;
    }

    private static void ApplyDragMotion(GraphNode node, Vector2 globalMouse)
    {
        Vector2 startMouse = node.GetMeta(DragStartMouseMeta).AsVector2();
        Vector2 startOffset = node.GetMeta(DragStartOffsetMeta).AsVector2();
        GraphEdit? edit = node.GetParent() as GraphEdit;
        float zoom = edit is not null ? Math.Max(edit.Zoom, 0.01f) : 1f;
        Vector2 next = startOffset + (globalMouse - startMouse) / zoom;
        if (edit is not null && edit.SnappingEnabled)
        {
            float snap = Math.Max(edit.SnappingDistance, 1);
            next = new Vector2(
                Mathf.Snapped(next.X, snap),
                Mathf.Snapped(next.Y, snap));
        }

        node.PositionOffset = next;
        Vector2 scroll = edit?.ScrollOffset ?? Vector2.Zero;
        node.Scale = new Vector2(zoom, zoom);
        node.Position = next * zoom - scroll;
    }

    private static bool IsDragging(GraphNode node) =>
        node.HasMeta(DraggingMeta) && node.GetMeta(DraggingMeta).AsBool();

    private static void SetMouseIgnoreRecursive(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        foreach (Node child in node.GetChildren())
        {
            SetMouseIgnoreRecursive(child);
        }
    }

    private const string DragWiredMeta = "pa_drag_wired";
    private const string DraggingMeta = "pa_dragging";
    private const string DragStartMouseMeta = "pa_drag_start_mouse";
    private const string DragStartOffsetMeta = "pa_drag_start_offset";
}

/// <summary>
/// Graph node representing a single input-stream data box on the pipeline canvas.
/// Visual content only — the Input Stream node owns the logical output port.
/// </summary>
public partial class PipelineDataSlotNode : GraphNode
{
    public int SlotIndex { get; private set; } = -1;
    public int SlotValue { get; private set; }
    public string VisualSkin { get; private set; } = VisualSkinIds.None;

    private Label _valueLabel = null!;
    private TextureRect _itemSprite = null!;

    public void Initialize(int index, int value, string? visualSkin = null, bool enableOutputPort = false)
    {
        SlotIndex = index;
        SlotValue = value;
        VisualSkin = NormalizeSkin(visualSkin);
        Title = $"Box [{index}]";
        CustomMinimumSize = new Vector2(140, 110);

        PipelineGraphNodeTheme.ApplyModernChrome(this, new Color("219EBC"));

        _valueLabel = new Label
        {
            Name = "ValueLabel",
            Text = value.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _valueLabel.AddThemeColorOverride("font_color", new Color("F8F9FA"));
        _valueLabel.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_valueLabel);

        _itemSprite = new TextureRect
        {
            Name = "ItemSprite",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(56, 56),
            Visible = false
        };
        AddChild(_itemSprite);

        ApplyVisualPresentation();

        if (enableOutputPort)
        {
            Texture2D portIcon = PipelineWorkspace.GetTouchPortTexture();
            SetSlot(
                slotIndex: 0,
                enableLeftPort: false,
                typeLeft: PipelineWorkspace.PortTypeDataStream,
                colorLeft: Colors.Gray,
                enableRightPort: true,
                typeRight: PipelineWorkspace.PortTypeDataStream,
                colorRight: new Color("00D4FF"),
                customIconLeft: null,
                customIconRight: portIcon);
        }

        PipelineGraphNodeTheme.ConfigureInteractiveNode(this, locked: false);
    }

    public void SetSlotValue(int value)
    {
        SlotValue = value;
        ApplyVisualPresentation();
    }

    private void ApplyVisualPresentation()
    {
        bool usesSpritePresentation = IsSkinned(VisualSkin);

        if (!usesSpritePresentation)
        {
            _valueLabel.Visible = true;
            _valueLabel.Text = SlotValue.ToString();
            _itemSprite.Visible = false;
            _itemSprite.Texture = null;
            return;
        }

        _valueLabel.Visible = false;
        _itemSprite.Visible = true;
        _itemSprite.Texture = VisualItemSpriteFactory.CreateSprite(VisualSkin, SlotValue);
        _itemSprite.CustomMinimumSize = VisualItemSpriteFactory.ResolveDisplaySize(VisualSkin, SlotValue);
        TooltipText = $"Value {SlotValue}";
    }

    private static string NormalizeSkin(string? visualSkin)
    {
        return string.IsNullOrWhiteSpace(visualSkin) ? VisualSkinIds.None : visualSkin.Trim();
    }

    private static bool IsSkinned(string skin)
    {
        return skin.Equals(VisualSkinIds.ToyBricks, StringComparison.OrdinalIgnoreCase)
            || skin.Equals(VisualSkinIds.Colors, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Builds lightweight procedural item textures for kid-friendly skins.
/// Keeps presentation out of <see cref="SimulationEngine"/> — no gameplay branching.
/// </summary>
public static class VisualItemSpriteFactory
{
    private static readonly Color[] ColorPalette =
    {
        new(0.95f, 0.35f, 0.35f), // coral red
        new(1.00f, 0.65f, 0.20f), // orange
        new(1.00f, 0.90f, 0.25f), // yellow
        new(0.35f, 0.80f, 0.45f), // green
        new(0.30f, 0.65f, 0.95f), // sky blue
        new(0.65f, 0.45f, 0.95f), // violet
        new(0.95f, 0.45f, 0.75f), // pink
        new(0.40f, 0.85f, 0.85f), // teal
    };

    private static readonly Color[] BrickPalette =
    {
        new(0.90f, 0.25f, 0.22f),
        new(0.20f, 0.55f, 0.85f),
        new(0.95f, 0.75f, 0.15f),
        new(0.25f, 0.70f, 0.35f),
        new(0.85f, 0.40f, 0.15f),
        new(0.55f, 0.30f, 0.75f),
        new(0.15f, 0.75f, 0.70f),
        new(0.95f, 0.55f, 0.70f),
    };

    public static Texture2D CreateSprite(string visualSkin, int value)
    {
        if (visualSkin.Equals(VisualSkinIds.ToyBricks, StringComparison.OrdinalIgnoreCase))
        {
            return CreateToyBrickTexture(value);
        }

        // Colors skin (and any future color-mapped aliases).
        return CreateColorChipTexture(value);
    }

    public static Vector2 ResolveDisplaySize(string visualSkin, int value)
    {
        int magnitude = Math.Max(1, Math.Abs(value));

        if (visualSkin.Equals(VisualSkinIds.ToyBricks, StringComparison.OrdinalIgnoreCase))
        {
            // Larger values → taller bricks (scaled item), still kid-readable.
            float width = 52f;
            float height = Mathf.Clamp(28f + magnitude * 8f, 36f, 96f);
            return new Vector2(width, height);
        }

        // Colors: uniform chip size; identity is carried by hue, not scale.
        return new Vector2(52f, 52f);
    }

    private static ImageTexture CreateColorChipTexture(int value)
    {
        const int size = 64;
        Color fill = ColorPalette[PositiveMod(value, ColorPalette.Length)];
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));

        // Rounded-ish filled square with a soft border.
        for (int y = 4; y < size - 4; y++)
        {
            for (int x = 4; x < size - 4; x++)
            {
                bool onBorder = x < 8 || x >= size - 8 || y < 8 || y >= size - 8;
                image.SetPixel(x, y, onBorder ? fill.Darkened(0.25f) : fill);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture CreateToyBrickTexture(int value)
    {
        const int width = 64;
        int studs = Mathf.Clamp(Math.Abs(value), 1, 8);
        int height = 36 + studs * 6;
        Color brick = BrickPalette[PositiveMod(value, BrickPalette.Length)];
        Color mortar = new(0.92f, 0.90f, 0.86f);

        Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(mortar);

        // Brick body.
        for (int y = 8; y < height - 4; y++)
        {
            for (int x = 4; x < width - 4; x++)
            {
                image.SetPixel(x, y, brick);
            }
        }

        // LEGO-style studs across the top — count scales subtly with value.
        int studCount = Mathf.Clamp(studs, 1, 4);
        int studWidth = 10;
        int gap = (width - 8 - studCount * studWidth) / Math.Max(1, studCount + 1);
        for (int s = 0; s < studCount; s++)
        {
            int sx = 4 + gap + s * (studWidth + gap);
            for (int y = 2; y < 10; y++)
            {
                for (int x = sx; x < sx + studWidth && x < width - 4; x++)
                {
                    image.SetPixel(x, y, brick.Lightened(0.15f));
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static int PositiveMod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}

/// <summary>
/// Graph node representing a computational engine (e.g. Bubble Sort Machine).
/// Shows live stream chips while the machine works.
/// </summary>
public partial class PipelineEngineNode : GraphNode
{
    public string BlockId { get; private set; } = string.Empty;

    private PipelineStreamChips _chips = null!;
    private Label _operationLabel = null!;

    public void Initialize(string blockId, IReadOnlyList<int>? initialValues = null)
    {
        BlockId = blockId;
        Title = blockId.Contains("Bubble_Sort", StringComparison.OrdinalIgnoreCase)
            ? "⚙ Bubble Sort"
            : $"⚙ {blockId.Replace('_', ' ')}";
        CustomMinimumSize = new Vector2(260, 160);

        PipelineGraphNodeTheme.ApplyModernChrome(this, new Color("2A9D8F"));

        var body = new VBoxContainer
        {
            Name = "EngineBody",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 6);
        AddChild(body);

        var portsHint = new Label
        {
            Name = "PortsHint",
            Text = "● IN (left edge)     OUT ● (right edge)",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        portsHint.AddThemeColorOverride("font_color", new Color("00D4FF"));
        portsHint.AddThemeFontSizeOverride("font_size", 12);
        body.AddChild(portsHint);

        _chips = new PipelineStreamChips { Name = "WorkingChips" };
        _chips.SetValues(initialValues ?? Array.Empty<int>());
        body.AddChild(_chips);

        _operationLabel = new Label
        {
            Name = "OperationLabel",
            Text = "Waiting for shipment…",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _operationLabel.AddThemeColorOverride("font_color", new Color("F8F9FA"));
        _operationLabel.AddThemeFontSizeOverride("font_size", 13);
        body.AddChild(_operationLabel);

        Texture2D portIcon = PipelineWorkspace.GetTouchPortTexture();
        SetSlot(
            slotIndex: 0,
            enableLeftPort: true,
            typeLeft: PipelineWorkspace.PortTypeDataStream,
            colorLeft: new Color("00D4FF"),
            enableRightPort: true,
            typeRight: PipelineWorkspace.PortTypeDataStream,
            colorRight: new Color("00D4FF"),
            customIconLeft: portIcon,
            customIconRight: portIcon);

        PipelineGraphNodeTheme.ConfigureInteractiveNode(this, locked: false);
    }

    public void SetWorkingValues(IReadOnlyList<int> values) => _chips.UpdateValues(values);
    public void HighlightCompare(int a, int b) => _chips.FocusIndices(a, b, forSwap: false);
    public void HighlightSwap(int a, int b) => _chips.FocusIndices(a, b, forSwap: true);
    public void ClearHighlights() => _chips.ClearHighlights();
    public void PlaySuccessPop() => _chips.PlaySuccessPop();
    public PipelineStreamChips Chips => _chips;

    public void SetOperationText(string text)
    {
        if (GodotObject.IsInstanceValid(_operationLabel))
        {
            _operationLabel.Text = text;
        }
    }
}

/// <summary>Logical Input Shipment — one OUT port carries the entire array as a stream.</summary>
public partial class PipelineInputStreamGraphNode : GraphNode
{
    private PipelineStreamChips _chips = null!;

    public void Initialize(IReadOnlyList<int> values)
    {
        Title = "INPUT SHIPMENT";
        CustomMinimumSize = new Vector2(260, 130);
        PipelineGraphNodeTheme.ApplyModernChrome(this, new Color("219EBC"));

        var body = new VBoxContainer { Name = "StreamBody" };
        body.AddThemeConstantOverride("separation", 8);
        AddChild(body);

        var hint = new Label
        {
            Text = "One stream = whole shipment",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        hint.AddThemeColorOverride("font_color", new Color(0.75f, 0.8f, 0.86f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        body.AddChild(hint);

        _chips = new PipelineStreamChips { Name = "InputChips" };
        _chips.SetValues(values);
        body.AddChild(_chips);

        var outHint = new Label
        {
            Text = "Drag OUT ● from the right edge",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        outHint.AddThemeColorOverride("font_color", new Color("00D4FF"));
        body.AddChild(outHint);

        Texture2D portIcon = PipelineWorkspace.GetTouchPortTexture();
        SetSlot(
            slotIndex: 0,
            enableLeftPort: false,
            typeLeft: PipelineWorkspace.PortTypeDataStream,
            colorLeft: Colors.Gray,
            enableRightPort: true,
            typeRight: PipelineWorkspace.PortTypeDataStream,
            colorRight: new Color("00D4FF"),
            customIconLeft: null,
            customIconRight: portIcon);
        PipelineGraphNodeTheme.ConfigureInteractiveNode(this, locked: false);
    }

    public void SetStreamValues(IReadOnlyList<int> values) => _chips.UpdateValues(values);
    public void ClearHighlights() => _chips.ClearHighlights();
    public void PlaySuccessPop() => _chips.PlaySuccessPop();
}

/// <summary>Logical Output sink — receives the final IntegerStream for evaluation.</summary>
public partial class PipelineOutputSinkGraphNode : GraphNode
{
    private PipelineStreamChips _chips = null!;
    private Label _status = null!;

    public void Initialize(IReadOnlyList<int>? targetPreview = null)
    {
        Title = "OUTPUT";
        CustomMinimumSize = new Vector2(220, 140);
        PipelineGraphNodeTheme.ApplyModernChrome(this, new Color("E9C46A"));

        var body = new VBoxContainer { Name = "OutputBody" };
        body.AddThemeConstantOverride("separation", 6);
        AddChild(body);

        var inHint = new Label
        {
            Text = "● IN — drag onto the left edge",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        inHint.AddThemeColorOverride("font_color", new Color("00D4FF"));
        body.AddChild(inHint);

        _chips = new PipelineStreamChips { Name = "OutputChips" };
        _chips.SetValues(targetPreview ?? Array.Empty<int>());
        // Dim target preview until real data arrives.
        _chips.Modulate = new Color(1f, 1f, 1f, 0.35f);
        body.AddChild(_chips);

        _status = new Label
        {
            Text = targetPreview is null ? "Waiting…" : "Target preview",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _status.AddThemeColorOverride("font_color", new Color("F8F9FA"));
        _status.AddThemeFontSizeOverride("font_size", 12);
        body.AddChild(_status);

        Texture2D portIcon = PipelineWorkspace.GetTouchPortTexture();
        SetSlot(
            slotIndex: 0,
            enableLeftPort: true,
            typeLeft: PipelineWorkspace.PortTypeDataStream,
            colorLeft: new Color("00D4FF"),
            enableRightPort: false,
            typeRight: PipelineWorkspace.PortTypeDataStream,
            colorRight: Colors.Gray,
            customIconLeft: portIcon,
            customIconRight: null);
        PipelineGraphNodeTheme.ConfigureInteractiveNode(this, locked: false);
    }

    public void SetReceivedValues(IReadOnlyList<int> values)
    {
        _chips.Modulate = Colors.White;
        _chips.UpdateValues(values);
        _status.Text = "Received";
    }

    public void ClearHighlights() => _chips.ClearHighlights();
    public void PlaySuccessPop() => _chips.PlaySuccessPop();
}
