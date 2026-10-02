using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PipelineArchitect.Data;

namespace PipelineArchitect.UI;

/// <summary>
/// Spotlight-style tutorial coach. Dims the UI, cuts a hole around the active control,
/// and floats a tip bubble next to that control as steps advance.
/// Click-through everywhere so players can still use the highlighted controls.
/// </summary>
public partial class TutorialCoach : Control
{
    private static readonly Color DimColor = new(0.02f, 0.04f, 0.07f, 0.55f);
    private static readonly Color SpotlightBorder = new("00D4FF");
    private static readonly Color PanelFill = new("1F242C");
    private static readonly Color CyanBorder = new("219EBC");
    private static readonly Color TextWhite = new("F8F9FA");

    private const float HolePadding = 10f;
    private const float TipGap = 12f;
    private const float PulseAmplitude = 4f;

    /// <summary>Pixels at the bottom left undimmed so Execute Factory stays visible.</summary>
    public float SafeBottomReserve { get; set; }

    /// <summary>Resolves a tutorial target id (from level JSON) to a live Control.</summary>
    public Func<string, Control?>? ResolveTarget { get; set; }

    private IReadOnlyList<TutorialStep> _steps = Array.Empty<TutorialStep>();
    private int _currentIndex = -1;
    private string _currentTargetId = string.Empty;
    private Control? _currentTarget;
    private Rect2 _holeRect;
    private PanelContainer _tipCard = null!;
    private Label _stepIndexLabel = null!;
    private Label _messageLabel = null!;
    private float _pulseTime;

    public int CurrentStepIndex => _currentIndex;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildTipCard();
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _steps.Count == 0)
        {
            return;
        }

        _pulseTime += (float)delta;
        RefreshTargetBinding();
        LayoutTipBesideHole();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Visible)
        {
            return;
        }

        Rect2 bounds = new(Vector2.Zero, Size);
        float usableBottom = Math.Max(0f, bounds.Size.Y - Math.Max(0f, SafeBottomReserve));
        Rect2 dimBounds = new(0, 0, bounds.Size.X, usableBottom);

        if (_holeRect.Size.X <= 1f || _holeRect.Size.Y <= 1f)
        {
            DrawRect(dimBounds, DimColor, filled: true);
            return;
        }

        float pulse = Mathf.Sin(_pulseTime * 4f) * PulseAmplitude;
        Rect2 hole = _holeRect.Grow(pulse);
        hole = hole.Intersection(dimBounds);

        // Four dim panels around the spotlight hole (keeps the target fully visible).
        DrawRect(new Rect2(0, 0, dimBounds.Size.X, Math.Max(0, hole.Position.Y)), DimColor, true);
        float bottomY = hole.Position.Y + hole.Size.Y;
        DrawRect(
            new Rect2(0, bottomY, dimBounds.Size.X, Math.Max(0, usableBottom - bottomY)),
            DimColor,
            true);
        DrawRect(
            new Rect2(0, hole.Position.Y, Math.Max(0, hole.Position.X), hole.Size.Y),
            DimColor,
            true);
        float rightX = hole.Position.X + hole.Size.X;
        DrawRect(
            new Rect2(rightX, hole.Position.Y, Math.Max(0, dimBounds.Size.X - rightX), hole.Size.Y),
            DimColor,
            true);

        if (hole.Size.X > 1f && hole.Size.Y > 1f)
        {
            DrawRect(hole, SpotlightBorder, filled: false, width: 2.5f);
            DrawArc(
                hole.GetCenter(),
                Math.Max(hole.Size.X, hole.Size.Y) * 0.55f + 6f + pulse,
                0,
                Mathf.Tau,
                48,
                new Color(SpotlightBorder, 0.35f),
                2f);
        }
    }

    /// <summary>
    /// Binds the coach to a level's tutorial sequence and shows the first step.
    /// </summary>
    public void BindSteps(IReadOnlyList<TutorialStep> steps)
    {
        _steps = steps ?? Array.Empty<TutorialStep>();
        Visible = _steps.Count > 0;
        if (_steps.Count > 0)
        {
            ShowStep(ResolveInitialStepIndex());
        }
    }

    /// <summary>
    /// Advances to a step by list ordinal or by <see cref="TutorialStep.StepIndex"/>.
    /// Repositions the spotlight onto that step's target control.
    /// </summary>
    public void ShowStep(int index)
    {
        if (_steps.Count == 0)
        {
            return;
        }

        int listIndex = FindListIndex(index);
        if (listIndex < 0)
        {
            GD.PushWarning($"[TutorialCoach] No tutorial step matching index {index}.");
            return;
        }

        TutorialStep step = _steps[listIndex];
        _currentIndex = step.StepIndex;
        _currentTargetId = step.TargetElement ?? string.Empty;
        _messageLabel.Text = step.Message ?? string.Empty;
        _stepIndexLabel.Text = $"TIP {listIndex + 1} / {_steps.Count}";
        Visible = true;
        _pulseTime = 0f;
        RefreshTargetBinding();
        LayoutTipBesideHole();
        PlayTipPop();
        QueueRedraw();
        GD.Print($"[TutorialCoach] ShowStep({step.StepIndex}) → target={_currentTargetId}");
    }

    /// <summary>No-op retained for callers that used the old static tip mouse filter helper.</summary>
    public void MakeClickThrough()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetMouseIgnoreRecursive(this);
    }

    private void BuildTipCard()
    {
        _tipCard = new PanelContainer
        {
            Name = "TipCard",
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(280, 0)
        };
        _tipCard.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = CyanBorder,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomRight = 10,
            CornerRadiusBottomLeft = 10,
            ShadowSize = 8,
            ShadowColor = new Color(0f, 0f, 0f, 0.45f),
            ShadowOffset = new Vector2(0, 3),
            ContentMarginLeft = 14,
            ContentMarginTop = 10,
            ContentMarginRight = 14,
            ContentMarginBottom = 10
        });
        AddChild(_tipCard);

        var body = new VBoxContainer
        {
            Name = "TipBody",
            MouseFilter = MouseFilterEnum.Ignore
        };
        body.AddThemeConstantOverride("separation", 4);
        _tipCard.AddChild(body);

        _stepIndexLabel = new Label
        {
            Name = "StepIndexLabel",
            Text = "TIP",
            MouseFilter = MouseFilterEnum.Ignore
        };
        _stepIndexLabel.AddThemeColorOverride("font_color", CyanBorder);
        _stepIndexLabel.AddThemeFontSizeOverride("font_size", 12);
        body.AddChild(_stepIndexLabel);

        _messageLabel = new Label
        {
            Name = "MessageLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(252, 0)
        };
        _messageLabel.AddThemeColorOverride("font_color", TextWhite);
        _messageLabel.AddThemeFontSizeOverride("font_size", 15);
        body.AddChild(_messageLabel);
    }

    private void RefreshTargetBinding()
    {
        Control? resolved = null;
        if (!string.IsNullOrWhiteSpace(_currentTargetId) && ResolveTarget is not null)
        {
            resolved = ResolveTarget(_currentTargetId);
        }

        _currentTarget = resolved is not null && GodotObject.IsInstanceValid(resolved)
            ? resolved
            : null;

        if (_currentTarget is null)
        {
            // Fallback: keep a small hole near the tip so the screen is not fully blacked out.
            Vector2 tipPos = _tipCard.Position;
            Vector2 tipSize = _tipCard.Size;
            if (tipSize == Vector2.Zero)
            {
                tipSize = _tipCard.GetCombinedMinimumSize();
            }

            _holeRect = new Rect2(tipPos, tipSize).Grow(HolePadding);
            return;
        }

        Rect2 global = _currentTarget.GetGlobalRect();
        _holeRect = new Rect2(global.Position - GlobalPosition, global.Size).Grow(HolePadding);
        _holeRect = _holeRect.Intersection(new Rect2(Vector2.Zero, Size));
    }

    private void LayoutTipBesideHole()
    {
        Vector2 tipSize = _tipCard.GetCombinedMinimumSize();
        if (_tipCard.Size.X > 1f)
        {
            tipSize = _tipCard.Size;
        }

        tipSize = new Vector2(
            Math.Max(tipSize.X, 280f),
            Math.Max(tipSize.Y, 64f));

        Vector2 preferred;
        if (_holeRect.Size.X > 1f)
        {
            // Prefer below the hole; flip above if that would leave the viewport.
            preferred = new Vector2(
                _holeRect.Position.X + (_holeRect.Size.X - tipSize.X) * 0.5f,
                _holeRect.Position.Y + _holeRect.Size.Y + TipGap);

            if (preferred.Y + tipSize.Y > Size.Y - 8f)
            {
                preferred.Y = _holeRect.Position.Y - tipSize.Y - TipGap;
            }
        }
        else
        {
            preferred = new Vector2((Size.X - tipSize.X) * 0.5f, Size.Y * 0.35f);
        }

        preferred.X = Mathf.Clamp(preferred.X, 8f, Math.Max(8f, Size.X - tipSize.X - 8f));
        float maxY = Math.Max(8f, Size.Y - tipSize.Y - 8f - Math.Max(0f, SafeBottomReserve));
        preferred.Y = Mathf.Clamp(preferred.Y, 8f, maxY);
        _tipCard.Position = preferred;
    }

    private void PlayTipPop()
    {
        _tipCard.Scale = new Vector2(0.94f, 0.94f);
        _tipCard.Modulate = new Color(1f, 1f, 1f, 0.7f);
        Tween tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_tipCard, "scale", Vector2.One, 0.18f)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_tipCard, "modulate:a", 1f, 0.18f);
    }

    private int ResolveInitialStepIndex()
    {
        TutorialStep first = _steps.OrderBy(s => s.StepIndex).First();
        return first.StepIndex;
    }

    private int FindListIndex(int requestedIndex)
    {
        for (int i = 0; i < _steps.Count; i++)
        {
            if (_steps[i].StepIndex == requestedIndex)
            {
                return i;
            }
        }

        if (requestedIndex >= 0 && requestedIndex < _steps.Count)
        {
            return requestedIndex;
        }

        return -1;
    }

    private static void SetMouseIgnoreRecursive(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = MouseFilterEnum.Ignore;
        }

        foreach (Node child in node.GetChildren())
        {
            SetMouseIgnoreRecursive(child);
        }
    }
}
