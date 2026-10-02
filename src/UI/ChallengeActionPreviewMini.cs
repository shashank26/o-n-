using System;
using Godot;

namespace PipelineArchitect.UI;

/// <summary>
/// Lightweight looping miniature that previews SWAP or KEEP for a value pair.
/// Presentation-only — never touches <see cref="Challenges.Core.ChallengeSession"/>.
/// </summary>
public partial class ChallengeActionPreviewMini : Control
{
    private static readonly Color ChipFill = new("161A20");
    private static readonly Color ChipBorder = new("219EBC");
    private static readonly Color ChipText = new("F8F9FA");

    private const float ChipW = 36f;
    private const float ChipH = 36f;
    private const float Gap = 10f;

    public enum PreviewKind
    {
        Swap,
        Keep
    }

    private PreviewKind _kind;
    private int _left;
    private int _right;
    private PanelContainer _chipA = null!;
    private PanelContainer _chipB = null!;
    private Tween? _loopTween;
    private bool _running;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(ChipW * 2 + Gap + 8, ChipH + 8);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildChips();
        LayoutRest();
    }

    public void Configure(PreviewKind kind, int left, int right)
    {
        _kind = kind;
        _left = left;
        _right = right;
        EnsureChips();
        SetChipLabel(_chipA, _left);
        SetChipLabel(_chipB, _right);
        LayoutRest();
        RestartLoop();
    }

    public void Stop()
    {
        _running = false;
        KillLoop();
        LayoutRest();
    }

    public void RestartLoop()
    {
        KillLoop();
        _running = true;
        LayoutRest();
        if (!IsInsideTree())
        {
            return;
        }

        if (_kind == PreviewKind.Swap)
        {
            StartSwapLoop();
        }
        else
        {
            StartKeepLoop();
        }
    }

    private void EnsureChips()
    {
        if (_chipA is not null)
        {
            return;
        }

        BuildChips();
    }

    private void BuildChips()
    {
        _chipA = CreateChip();
        _chipB = CreateChip();
        AddChild(_chipA);
        AddChild(_chipB);
    }

    private static PanelContainer CreateChip()
    {
        var chip = new PanelContainer
        {
            CustomMinimumSize = new Vector2(ChipW, ChipH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        var style = new StyleBoxFlat
        {
            BgColor = ChipFill,
            BorderColor = ChipBorder,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5
        };
        chip.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Name = "Value",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", ChipText);
        chip.AddChild(label);
        return chip;
    }

    private static void SetChipLabel(PanelContainer chip, int value)
    {
        if (chip.GetNodeOrNull("Value") is Label label)
        {
            label.Text = value.ToString();
        }
    }

    private Vector2 SlotA => new(4, 4);
    private Vector2 SlotB => new(4 + ChipW + Gap, 4);

    private void LayoutRest()
    {
        if (_chipA is null || _chipB is null)
        {
            return;
        }

        _chipA.Position = SlotA;
        _chipB.Position = SlotB;
        _chipA.Modulate = Colors.White;
        _chipB.Modulate = Colors.White;
        _chipA.Scale = Vector2.One;
        _chipB.Scale = Vector2.One;
        SetChipLabel(_chipA, _left);
        SetChipLabel(_chipB, _right);
    }

    private void StartSwapLoop()
    {
        // Identity-preserving cross: chips keep their labels; positions swap then return.
        Tween tween = CreateTween().SetLoops();
        _loopTween = tween;
        float half = 0.50f;
        float hold = 0.40f;
        float pause = 0.30f;

        Vector2 a0 = SlotA;
        Vector2 b0 = SlotB;
        Vector2 midA = new((a0.X + b0.X) * 0.5f, a0.Y - 10f);
        Vector2 midB = new((a0.X + b0.X) * 0.5f, b0.Y + 10f);

        tween.TweenCallback(Callable.From(LayoutRest));
        tween.TweenInterval(pause);

        tween.SetParallel(true);
        tween.TweenProperty(_chipA, "position", midA, half)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_chipB, "position", midB, half)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

        tween.Chain().SetParallel(true);
        tween.TweenProperty(_chipA, "position", b0, half)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.TweenProperty(_chipB, "position", a0, half)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);

        tween.Chain().TweenInterval(hold);
        tween.TweenCallback(Callable.From(LayoutRest));
        tween.TweenInterval(pause);
    }

    private void StartKeepLoop()
    {
        Tween tween = CreateTween().SetLoops();
        _loopTween = tween;
        float pulse = 0.45f;
        float pause = 0.40f;

        tween.TweenInterval(pause * 0.5f);
        tween.SetParallel(true);
        tween.TweenProperty(_chipA, "scale", new Vector2(1.06f, 1.06f), pulse)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(_chipB, "scale", new Vector2(1.06f, 1.06f), pulse)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Chain().SetParallel(true);
        tween.TweenProperty(_chipA, "scale", Vector2.One, pulse)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(_chipB, "scale", Vector2.One, pulse)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.Chain().TweenInterval(pause);
    }

    private void KillLoop()
    {
        if (_loopTween is not null && GodotObject.IsInstanceValid(_loopTween))
        {
            _loopTween.Kill();
        }

        _loopTween = null;
    }

    public override void _ExitTree()
    {
        KillLoop();
        base._ExitTree();
    }
}
