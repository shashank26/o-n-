using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PipelineArchitect.Challenges;
using PipelineArchitect.Challenges.Core;

namespace PipelineArchitect.UI;

/// <summary>
/// Godot view for interactive discovery + unguided practice.
/// Observes <see cref="ChallengeProgressionController"/>; never mutates LevelData / SimulationEngine.
/// </summary>
public partial class DiscoveryChallengeView : CanvasLayer
{
    private static readonly Color ColorPanel = new("1F242C");
    private static readonly Color ColorText = new("F8F9FA");
    private static readonly Color ColorAccent = new("219EBC");
    private static readonly Color ColorDanger = new(1.0f, 0.45f, 0.35f, 1.0f);
    private static readonly Color ColorSuccess = new(0.25f, 0.95f, 0.55f, 1.0f);

    private ChallengeProgressionController? _progression;
    private ChallengeSession? _session;
    private Label _titleLabel = null!;
    private Label _exerciseCounterLabel = null!;
    private Label _exerciseProgressLabel = null!;
    private Label _passLabel = null!;
    private Label _promptLabel = null!;
    private Label _feedbackLabel = null!;
    private PipelineStreamChips _mainChips = null!;
    private HBoxContainer _actionsRow = null!;
    private HBoxContainer _secondaryActionsRow = null!;
    private Button _swapButton = null!;
    private Button _keepButton = null!;
    private Button _nextPassButton = null!;
    private Button _completeButton = null!;
    private ChallengeActionPreviewMini _swapPreview = null!;
    private ChallengeActionPreviewMini _keepPreview = null!;
    private Button _continueButton = null!;
    private Button _tryAgainButton = null!;
    private Button _replayGuidedButton = null!;
    private Button _replayPracticeButton = null!;
    private Control _playPanel = null!;
    private Control _revealPanel = null!;
    private Label _revealTitle = null!;
    private Label _revealBody = null!;
    private VBoxContainer _revealButtons = null!;
    private enum UiMode { Playing, ConceptReveal, MachineReveal, Result }
    private UiMode _mode = UiMode.Playing;
    private bool _transitionRunning;

    public event Action? FactoryHandoffRequested;

    public override void _Ready()
    {
        Layer = 90;
        BuildChrome();
    }

    public void Bind(ChallengeProgressionController progression)
    {
        UnbindSessionOnly();
        _progression = progression;
        _progression.PhaseChanged += OnPhaseChanged;
        BindActiveSession(_progression.ActiveSession);
        _mode = UiMode.Playing;
        _transitionRunning = false;
        ShowPlayUi();
        ApplyPresentation(_session!.GetPresentation(), refreshPreviews: true);
        Visible = true;
    }

    /// <summary>Legacy single-session bind (guided only, no practice).</summary>
    public void Bind(ChallengeSession session)
    {
        var config = new InteractiveChallengeConfig
        {
            Enabled = true,
            ChallengeId = session.Definition.ChallengeId,
            PlayerTitle = session.Definition.PlayerTitle,
            PlayerDescription = session.Definition.PlayerDescription,
            InitialValues = session.Definition.InitialValues,
            RevealConceptAfterCompletion = session.Definition.RevealConceptAfterCompletion,
            ConceptName = session.Definition.ConceptName,
            ConceptExplanation = session.Definition.ConceptExplanation,
            MachineBlockId = session.Definition.MachineBlockId,
            MachineRevealTitle = session.Definition.MachineRevealTitle,
            MachineRevealBody = session.Definition.MachineRevealBody,
            PracticeChallenge = new PracticeChallengeConfig { Enabled = false }
        };
        Bind(new ChallengeProgressionController(config));
    }

    public void Unbind()
    {
        if (_progression is not null)
        {
            _progression.PhaseChanged -= OnPhaseChanged;
        }

        UnbindSessionOnly();
        _progression = null;
        _swapPreview?.Stop();
        _keepPreview?.Stop();
        Visible = false;
    }

    private void UnbindSessionOnly()
    {
        if (_session is not null)
        {
            _session.PresentationChanged -= OnPresentationChanged;
        }

        _session = null;
    }

    private void BindActiveSession(ChallengeSession session)
    {
        UnbindSessionOnly();
        _session = session;
        _session.PresentationChanged += OnPresentationChanged;
    }

    private void OnPhaseChanged(ChallengeFlowPhase phase)
    {
        switch (phase)
        {
            case ChallengeFlowPhase.Guided:
                BindActiveSession(_progression!.GuidedSession);
                ShowPlayUi();
                ApplyPresentation(_session!.GetPresentation(), refreshPreviews: true);
                break;
            case ChallengeFlowPhase.Unguided:
                BindActiveSession(_progression!.PracticeSession!);
                ShowPlayUi();
                ApplyPresentation(_session!.GetPresentation(), refreshPreviews: true);
                break;
            case ChallengeFlowPhase.Mastery:
                BindActiveSession(_progression!.MasterySession!);
                ShowPlayUi();
                ApplyPresentation(_session!.GetPresentation(), refreshPreviews: true);
                break;
            case ChallengeFlowPhase.ExerciseComplete:
                ShowExerciseCompleteResult();
                break;
            case ChallengeFlowPhase.ProblemComplete:
                ShowProblemCompleteResult();
                break;
            case ChallengeFlowPhase.Failure:
                ShowFailureResult();
                break;
        }
    }

    private void BuildChrome()
    {
        var dimmer = new ColorRect
        {
            Name = "Dimmer",
            Color = new Color(0f, 0f, 0f, 0.55f)
        };
        dimmer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dimmer.OffsetBottom = -88f;
        AddChild(dimmer);

        var margin = new MarginContainer { Name = "ChallengeMargin" };
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.OffsetBottom = -88f;
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var center = new CenterContainer();
        center.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        center.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        margin.AddChild(center);

        _playPanel = BuildPlayCard();
        center.AddChild(_playPanel);

        _revealPanel = BuildRevealCard();
        _revealPanel.Visible = false;
        center.AddChild(_revealPanel);
    }

    private Control BuildPlayCard()
    {
        var card = new PanelContainer
        {
            Name = "DiscoveryPlayCard",
            CustomMinimumSize = new Vector2(460, 520)
        };
        ApplyCardStyle(card);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        card.AddChild(body);

        _titleLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 22);
        _titleLabel.AddThemeColorOverride("font_color", ColorAccent);
        body.AddChild(_titleLabel);

        _exerciseCounterLabel = new Label
        {
            Name = "ExerciseCounterLabel",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _exerciseCounterLabel.AddThemeFontSizeOverride("font_size", 14);
        _exerciseCounterLabel.AddThemeColorOverride("font_color", ColorAccent);
        body.AddChild(_exerciseCounterLabel);

        _exerciseProgressLabel = new Label
        {
            Name = "ExerciseProgressLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _exerciseProgressLabel.AddThemeFontSizeOverride("font_size", 13);
        _exerciseProgressLabel.AddThemeColorOverride("font_color", ColorText);
        body.AddChild(_exerciseProgressLabel);

        _passLabel = new Label
        {
            Name = "PassLabel",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _passLabel.AddThemeFontSizeOverride("font_size", 16);
        _passLabel.AddThemeColorOverride("font_color", ColorText);
        body.AddChild(_passLabel);

        _mainChips = new PipelineStreamChips
        {
            Name = "DiscoveryMainChips",
            CustomMinimumSize = new Vector2(320, 52)
        };
        var chipsCenter = new CenterContainer();
        chipsCenter.AddChild(_mainChips);
        body.AddChild(chipsCenter);

        _promptLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _promptLabel.AddThemeColorOverride("font_color", ColorText);
        _promptLabel.AddThemeFontSizeOverride("font_size", 15);
        body.AddChild(_promptLabel);

        _feedbackLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false
        };
        _feedbackLabel.AddThemeColorOverride("font_color", ColorDanger);
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 14);
        body.AddChild(_feedbackLabel);

        _actionsRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _actionsRow.AddThemeConstantOverride("separation", 16);
        body.AddChild(_actionsRow);

        _swapButton = BuildPreviewActionButton(
            "SwapButton", "SWAP", ChallengeActionPreviewMini.PreviewKind.Swap,
            out _swapPreview, () => _ = SubmitAsync(ChallengeActionIds.Swap));
        _actionsRow.AddChild(_swapButton);

        _keepButton = BuildPreviewActionButton(
            "KeepButton", "KEEP", ChallengeActionPreviewMini.PreviewKind.Keep,
            out _keepPreview, () => _ = SubmitAsync(ChallengeActionIds.Keep));
        _actionsRow.AddChild(_keepButton);

        _secondaryActionsRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _secondaryActionsRow.AddThemeConstantOverride("separation", 12);
        body.AddChild(_secondaryActionsRow);

        _nextPassButton = CreateTextButton("NextPassButton", "NEXT PASS", new Vector2(160, 48));
        _nextPassButton.Pressed += () => _ = SubmitAsync(ChallengeActionIds.NextPass);
        _nextPassButton.Visible = false;
        _secondaryActionsRow.AddChild(_nextPassButton);

        _completeButton = CreateTextButton("CompleteButton", "COMPLETE", new Vector2(160, 48));
        _completeButton.Pressed += () => _ = SubmitAsync(ChallengeActionIds.Complete);
        _completeButton.Visible = false;
        _secondaryActionsRow.AddChild(_completeButton);

        return card;
    }

    private Button BuildPreviewActionButton(
        string name,
        string caption,
        ChallengeActionPreviewMini.PreviewKind kind,
        out ChallengeActionPreviewMini preview,
        Action onPressed)
    {
        var button = new Button
        {
            Name = name,
            CustomMinimumSize = new Vector2(168, 118)
        };
        RunPipelineButtonStyle.Apply(button);
        button.Text = string.Empty;
        button.ClipContents = true;

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        column.OffsetLeft = 8;
        column.OffsetRight = -8;
        column.OffsetTop = 8;
        column.OffsetBottom = -8;
        button.AddChild(column);

        var captionLabel = new Label
        {
            Text = caption,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        captionLabel.AddThemeFontSizeOverride("font_size", 16);
        captionLabel.AddThemeColorOverride("font_color", ColorText);
        column.AddChild(captionLabel);

        preview = new ChallengeActionPreviewMini { MouseFilter = Control.MouseFilterEnum.Ignore };
        var previewCenter = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        previewCenter.AddChild(preview);
        column.AddChild(previewCenter);
        preview.Configure(kind, 0, 0);
        button.Pressed += onPressed;
        return button;
    }

    private Button CreateTextButton(string name, string text, Vector2 minSize)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            CustomMinimumSize = minSize
        };
        RunPipelineButtonStyle.Apply(button);
        button.Text = text;
        return button;
    }

    private Control BuildRevealCard()
    {
        var card = new PanelContainer
        {
            Name = "DiscoveryRevealCard",
            CustomMinimumSize = new Vector2(440, 360)
        };
        ApplyCardStyle(card);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        card.AddChild(body);

        _revealTitle = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _revealTitle.AddThemeFontSizeOverride("font_size", 22);
        _revealTitle.AddThemeColorOverride("font_color", ColorAccent);
        body.AddChild(_revealTitle);

        _revealBody = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _revealBody.AddThemeColorOverride("font_color", ColorText);
        _revealBody.AddThemeFontSizeOverride("font_size", 15);
        body.AddChild(_revealBody);

        _revealButtons = new VBoxContainer();
        _revealButtons.AddThemeConstantOverride("separation", 10);
        body.AddChild(_revealButtons);

        _continueButton = CreateTextButton("ContinueRevealButton", "CONTINUE", new Vector2(220, 48));
        _continueButton.Pressed += OnContinuePressed;
        _revealButtons.AddChild(_continueButton);

        _tryAgainButton = CreateTextButton("TryAgainButton", "TRY AGAIN", new Vector2(220, 48));
        _tryAgainButton.Pressed += OnTryAgainPressed;
        _tryAgainButton.Visible = false;
        _revealButtons.AddChild(_tryAgainButton);

        _replayGuidedButton = CreateTextButton("ReplayGuidedButton", "REPLAY EXERCISE 1", new Vector2(280, 48));
        _replayGuidedButton.Pressed += OnReplayGuidedPressed;
        _replayGuidedButton.Visible = false;
        _revealButtons.AddChild(_replayGuidedButton);

        _replayPracticeButton = CreateTextButton("ReplayPracticeButton", "REPLAY EXERCISE 2", new Vector2(280, 48));
        _replayPracticeButton.Pressed += OnReplayPracticePressed;
        _replayPracticeButton.Visible = false;
        _revealButtons.AddChild(_replayPracticeButton);

        return card;
    }

    private static void ApplyCardStyle(PanelContainer card)
    {
        var style = new StyleBoxFlat
        {
            BgColor = ColorPanel,
            BorderColor = ColorAccent,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 16,
            ContentMarginBottom = 16
        };
        card.AddThemeStyleboxOverride("panel", style);
    }

    private async Task SubmitAsync(string actionId)
    {
        if (_session is null || _progression is null || _mode != UiMode.Playing || _transitionRunning)
        {
            return;
        }

        if (actionId == ChallengeActionIds.Complete
            && _progression.Phase is ChallengeFlowPhase.Unguided or ChallengeFlowPhase.Mastery)
        {
            await SubmitPracticeCompleteAsync().ConfigureAwait(true);
            return;
        }

        if (!_session.BeginAuthoritativeTransition())
        {
            return;
        }

        _transitionRunning = true;
        SetActionButtonsEnabled(false);
        _swapPreview.Stop();
        _keepPreview.Stop();

        ChallengeAction action = new() { ActionId = actionId };
        ChallengeActionResult evaluated = _session.Evaluate(action);
        ChallengePresentation frozen = _session.GetPresentation();

        try
        {
            if (!evaluated.Accepted)
            {
                await PlayRejectionFeedbackAsync(frozen).ConfigureAwait(true);
                _session.PublishRejectionFeedback(evaluated.Feedback);
                _session.EndAuthoritativeTransition();
                ApplyPresentation(_session.GetPresentation(), refreshPreviews: true);
                return;
            }

            if (actionId == ChallengeActionIds.NextPass)
            {
                await PlayPassTransitionAsync().ConfigureAwait(true);
            }
            else
            {
                await PlayAuthoritativeTransitionAsync(frozen, evaluated).ConfigureAwait(true);
            }

            ChallengeActionResult committed = _session.CommitWhileLocked(action);
            ApplyPresentation(_session.GetPresentation(), refreshPreviews: true);

            if (_progression.Phase == ChallengeFlowPhase.Guided
                && committed.ChallengeCompleted
                && _session.Reveal is not null)
            {
                await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
                _progression.NotifyGuidedCompleted();
                ShowConceptReveal(_session.Reveal);
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"Discovery transition failed: {ex.Message}");
            if (_session.IsInteractionLocked)
            {
                _session.EndAuthoritativeTransition();
            }
        }
        finally
        {
            _transitionRunning = false;
            if (_mode == UiMode.Playing && _session is { IsComplete: false })
            {
                SetActionButtonsEnabled(true);
                RefreshPreviewLoops(_session.GetPresentation());
            }
        }
    }

    private async Task SubmitPracticeCompleteAsync()
    {
        if (_progression is null || _session is null)
        {
            return;
        }

        _transitionRunning = true;
        SetActionButtonsEnabled(false);
        _swapPreview.Stop();
        _keepPreview.Stop();

        try
        {
            await PlayPassTransitionAsync().ConfigureAwait(true);
            ChallengeValidationResult result = _progression.SubmitActiveUnguidedComplete();
            if (result.IsSuccess)
            {
                ShowExerciseCompleteResult();
            }
            else
            {
                ShowFailureResult();
            }
        }
        finally
        {
            _transitionRunning = false;
        }
    }

    private async Task PlayAuthoritativeTransitionAsync(
        ChallengePresentation before,
        ChallengeActionResult evaluated)
    {
        if (before.HighlightIndexA is not int a || before.HighlightIndexB is not int b)
        {
            return;
        }

        _mainChips.FocusIndices(a, b, forSwap: evaluated.VisualHint?.DidSwap == true);
        if (evaluated.VisualHint?.DidSwap == true)
        {
            await _mainChips.AnimateSwapAsync(a, b, 0.70f).ConfigureAwait(true);
        }
        else
        {
            Tween pulse = CreateTween();
            pulse.TweenProperty(_mainChips, "modulate", new Color(0.35f, 0.95f, 0.60f, 1f), 0.18f);
            pulse.TweenProperty(_mainChips, "modulate", Colors.White, 0.28f);
            await ToSignal(pulse, Tween.SignalName.Finished);
            await ToSignal(GetTree().CreateTimer(0.20), SceneTreeTimer.SignalName.Timeout);
        }

        _mainChips.ClearHighlights();
    }

    private async Task PlayPassTransitionAsync()
    {
        Tween tween = CreateTween();
        tween.TweenProperty(_mainChips, "modulate", new Color(1f, 1f, 1f, 0.45f), 0.18f);
        tween.TweenProperty(_passLabel, "modulate", new Color(1f, 1f, 1f, 0.35f), 0.18f);
        await ToSignal(tween, Tween.SignalName.Finished);
        await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
        Tween settle = CreateTween();
        settle.TweenProperty(_mainChips, "modulate", Colors.White, 0.22f);
        settle.TweenProperty(_passLabel, "modulate", Colors.White, 0.22f);
        await ToSignal(settle, Tween.SignalName.Finished);
    }

    private async Task PlayRejectionFeedbackAsync(ChallengePresentation before)
    {
        if (before.HighlightIndexA is int a && before.HighlightIndexB is int b)
        {
            _mainChips.FocusIndices(a, b, forSwap: false);
        }

        Tween shake = CreateTween();
        Vector2 origin = _mainChips.Position;
        shake.TweenProperty(_mainChips, "position", origin + new Vector2(-6, 0), 0.05f);
        shake.TweenProperty(_mainChips, "position", origin + new Vector2(6, 0), 0.08f);
        shake.TweenProperty(_mainChips, "position", origin, 0.08f);
        shake.Parallel().TweenProperty(_mainChips, "modulate", ColorDanger, 0.12f);
        shake.TweenProperty(_mainChips, "modulate", Colors.White, 0.20f);
        await ToSignal(shake, Tween.SignalName.Finished);
        await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
        _mainChips.ClearHighlights();
    }

    private void OnPresentationChanged(ChallengePresentation presentation)
    {
        if (_mode != UiMode.Playing || _transitionRunning)
        {
            return;
        }

        ApplyPresentation(presentation, refreshPreviews: true);
    }

    private void ApplyPresentation(ChallengePresentation presentation, bool refreshPreviews)
    {
        _titleLabel.Text = presentation.Title;
        UpdateExerciseProgressLabels();
        _passLabel.Text = presentation.PassLabel ?? string.Empty;
        _passLabel.Visible = !string.IsNullOrEmpty(presentation.PassLabel);
        _promptLabel.Text = presentation.Prompt;

        if (!string.IsNullOrWhiteSpace(presentation.Feedback))
        {
            _feedbackLabel.Text = presentation.Feedback;
            _feedbackLabel.Visible = true;
        }
        else
        {
            _feedbackLabel.Visible = false;
        }

        _mainChips.SetValues(presentation.Values);
        if (presentation.HighlightIndexA is int ha && presentation.HighlightIndexB is int hb)
        {
            _mainChips.FocusIndices(ha, hb, forSwap: false);
        }
        else
        {
            _mainChips.ClearHighlights();
        }

        bool canAct = !_transitionRunning && _session is { IsInteractionLocked: false };
        bool hasSwap = presentation.AvailableActionIds.Contains(ChallengeActionIds.Swap);
        bool hasKeep = presentation.AvailableActionIds.Contains(ChallengeActionIds.Keep);
        bool hasNext = presentation.AvailableActionIds.Contains(ChallengeActionIds.NextPass);
        bool hasComplete = presentation.AvailableActionIds.Contains(ChallengeActionIds.Complete);

        _swapButton.Visible = hasSwap;
        _keepButton.Visible = hasKeep;
        _nextPassButton.Visible = hasNext;
        _completeButton.Visible = hasComplete;
        _swapButton.Disabled = !canAct || !hasSwap;
        _keepButton.Disabled = !canAct || !hasKeep;
        _nextPassButton.Disabled = !canAct || !hasNext;
        _completeButton.Disabled = !canAct || !hasComplete;

        if (refreshPreviews && hasSwap)
        {
            RefreshPreviewLoops(presentation);
        }
        else if (!hasSwap)
        {
            _swapPreview.Stop();
            _keepPreview.Stop();
        }
    }

    private void RefreshPreviewLoops(ChallengePresentation presentation)
    {
        if (presentation.HighlightIndexA is not int a
            || presentation.HighlightIndexB is not int b
            || a < 0
            || b >= presentation.Values.Count)
        {
            _swapPreview.Stop();
            _keepPreview.Stop();
            return;
        }

        int left = presentation.Values[a];
        int right = presentation.Values[b];
        _swapPreview.Configure(ChallengeActionPreviewMini.PreviewKind.Swap, left, right);
        _keepPreview.Configure(ChallengeActionPreviewMini.PreviewKind.Keep, left, right);
    }

    private void UpdateExerciseProgressLabels()
    {
        if (_progression is null)
        {
            _exerciseCounterLabel.Text = string.Empty;
            _exerciseProgressLabel.Text = string.Empty;
            return;
        }

        int current = _progression.CurrentExerciseNumber;
        _exerciseCounterLabel.Text = $"EXERCISE {current} / {ChallengeProgressionController.TotalExercises}";

        string Mark(int n)
        {
            if (_progression.IsExerciseCompleted(n))
            {
                return $"✓ Exercise {n}";
            }

            return n == current ? $"● Exercise {n}" : $"○ Exercise {n}";
        }

        _exerciseProgressLabel.Text = $"{Mark(1)}\n{Mark(2)}\n{Mark(3)}";
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        _swapButton.Disabled = !enabled || !_swapButton.Visible;
        _keepButton.Disabled = !enabled || !_keepButton.Visible;
        _nextPassButton.Disabled = !enabled || !_nextPassButton.Visible;
        _completeButton.Disabled = !enabled || !_completeButton.Visible;
    }

    private void ShowPlayUi()
    {
        _playPanel.Visible = true;
        _revealPanel.Visible = false;
        _mode = UiMode.Playing;
        _continueButton.Visible = true;
        _tryAgainButton.Visible = false;
        _replayGuidedButton.Visible = false;
        _replayPracticeButton.Visible = false;
        UpdateExerciseProgressLabels();
    }

    private void ShowConceptReveal(ConceptReveal reveal)
    {
        _swapPreview.Stop();
        _keepPreview.Stop();
        _mode = UiMode.ConceptReveal;
        _playPanel.Visible = false;
        _revealPanel.Visible = true;
        _revealTitle.AddThemeColorOverride("font_color", ColorAccent);
        _revealTitle.Text = "YOU DISCOVERED IT";
        _revealBody.Text =
            $"What you just did is called {reveal.ConceptName}.\n\n{reveal.Explanation}";
        _continueButton.Text = "CONTINUE";
        _continueButton.Visible = true;
        _tryAgainButton.Visible = false;
        _replayGuidedButton.Visible = false;
        _replayPracticeButton.Visible = false;
    }

    private void ShowMachineReveal(ConceptReveal reveal)
    {
        _mode = UiMode.MachineReveal;
        _playPanel.Visible = false;
        _revealPanel.Visible = true;
        _revealTitle.AddThemeColorOverride("font_color", ColorAccent);
        _revealTitle.Text = reveal.MachineRevealTitle;
        _revealBody.Text = $"{reveal.ConceptName}\n\n{reveal.MachineRevealBody}";
        _continueButton.Text = "CONTINUE";
        _continueButton.Visible = true;
        _tryAgainButton.Visible = false;
        _replayGuidedButton.Visible = false;
        _replayPracticeButton.Visible = false;
    }

    private void ShowExerciseCompleteResult()
    {
        _swapPreview.Stop();
        _keepPreview.Stop();
        _mode = UiMode.Result;
        _playPanel.Visible = false;
        _revealPanel.Visible = true;
        _revealTitle.AddThemeColorOverride("font_color", ColorSuccess);
        int n = _progression?.CurrentExerciseNumber ?? 1;
        _revealTitle.Text = $"EXERCISE {n} COMPLETE ✓";
        _revealBody.Text = n switch
        {
            1 => "Next: Sort It Yourself.",
            2 => "Next: Final Challenge.",
            _ => _progression?.LastValidation?.SummaryMessage ?? "You figured it out."
        };
        _continueButton.Text = "CONTINUE";
        _continueButton.Visible = true;
        _tryAgainButton.Visible = false;
        _replayGuidedButton.Visible = false;
        _replayPracticeButton.Visible = false;
        UpdateExerciseProgressLabels();
    }

    private void ShowProblemCompleteResult()
    {
        _swapPreview.Stop();
        _keepPreview.Stop();
        _mode = UiMode.Result;
        _playPanel.Visible = false;
        _revealPanel.Visible = true;
        _revealTitle.AddThemeColorOverride("font_color", ColorSuccess);
        _revealTitle.Text = "PROBLEM COMPLETE ✓";
        _revealBody.Text = "You mastered this problem.";
        _continueButton.Text = "CONTINUE";
        _continueButton.Visible = true;
        _tryAgainButton.Visible = false;
        _replayGuidedButton.Visible = false;
        _replayPracticeButton.Visible = false;
        UpdateExerciseProgressLabels();
    }

    private void ShowFailureResult()
    {
        _swapPreview.Stop();
        _keepPreview.Stop();
        _mode = UiMode.Result;
        _playPanel.Visible = false;
        _revealPanel.Visible = true;
        _revealTitle.AddThemeColorOverride("font_color", ColorDanger);
        _revealTitle.Text = "NOT QUITE";
        _revealBody.Text = _progression?.LastValidation?.SummaryMessage
            ?? "You can try again or review what you learned.";
        _continueButton.Visible = false;
        _tryAgainButton.Visible = true;
        _replayGuidedButton.Visible = true;
        _replayPracticeButton.Visible =
            _progression?.CurrentExercise == ProblemExerciseSlot.FinalChallenge;
        UpdateExerciseProgressLabels();
    }

    private void OnContinuePressed()
    {
        if (_progression is null)
        {
            FactoryHandoffRequested?.Invoke();
            return;
        }

        if (_mode == UiMode.ConceptReveal)
        {
            _progression.ContinueFromConceptReveal();
            if (_progression.GuidedSession.Reveal is not null)
            {
                ShowMachineReveal(_progression.GuidedSession.Reveal);
            }

            return;
        }

        if (_mode == UiMode.MachineReveal)
        {
            _progression.ContinueFromMachineReveal();
            if (_progression.Phase == ChallengeFlowPhase.ProblemComplete)
            {
                ShowProblemCompleteResult();
            }
            else if (_progression.Phase == ChallengeFlowPhase.ExerciseComplete)
            {
                ShowExerciseCompleteResult();
            }

            return;
        }

        if (_mode == UiMode.Result)
        {
            if (_progression.Phase == ChallengeFlowPhase.ExerciseComplete)
            {
                _progression.ContinueAfterExerciseComplete();
                return;
            }

            if (_progression.Phase == ChallengeFlowPhase.ProblemComplete
                && _progression.IsProblemComplete)
            {
                FactoryHandoffRequested?.Invoke();
            }
        }
    }

    private void OnTryAgainPressed()
    {
        _progression?.RetryCurrentExercise();
    }

    private void OnReplayGuidedPressed()
    {
        _progression?.ReplayExercise(ProblemExerciseSlot.GuidedDiscovery);
    }

    private void OnReplayPracticePressed()
    {
        _progression?.ReplayExercise(ProblemExerciseSlot.SortItYourself);
    }
}
