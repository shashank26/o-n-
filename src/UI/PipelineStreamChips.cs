using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace PipelineArchitect.UI;

/// <summary>
/// Stream chips with manual layout so identity-preserving compare/swap motion works.
/// Presentation-only — simulation state is never derived from chip positions.
/// </summary>
public partial class PipelineStreamChips : Control
{
    private static readonly Color ChipFill = new("161A20");
    private static readonly Color ChipBorder = new("219EBC");
    private static readonly Color ChipText = new("F8F9FA");
    private static readonly Color FocusCompare = new(1.0f, 0.88f, 0.25f, 1.0f);
    private static readonly Color FocusSwap = new(0.35f, 0.95f, 0.60f, 1.0f);
    private static readonly Color Quiet = new(1f, 1f, 1f, 0.28f);

    private const float ChipSize = 40f;
    private const float Separation = 8f;

    private readonly List<PanelContainer> _chips = new();
    private readonly List<int> _values = new();
    private Tween? _activeTween;

    public IReadOnlyList<int> Values => _values;

    public override void _Ready()
    {
        // Display-only: never steal GraphNode drag gestures from the factory floor.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetValues(IReadOnlyList<int> values)
    {
        KillActiveTween();
        ClearChips();
        _values.Clear();
        for (int i = 0; i < values.Count; i++)
        {
            _values.Add(values[i]);
            PanelContainer chip = CreateChip(values[i]);
            _chips.Add(chip);
            AddChild(chip);
        }

        LayoutChipsInstant();
    }

    /// <summary>Instant value sync without recreating chips when counts match.</summary>
    public void UpdateValues(IReadOnlyList<int> values)
    {
        if (values.Count != _chips.Count)
        {
            SetValues(values);
            return;
        }

        for (int i = 0; i < values.Count; i++)
        {
            _values[i] = values[i];
            if (_chips[i].GetNodeOrNull("Value") is Label label)
            {
                label.Text = values[i].ToString();
            }
        }
    }

    public void FocusIndices(int indexA, int indexB, bool forSwap)
    {
        Color focus = forSwap ? FocusSwap : FocusCompare;
        for (int i = 0; i < _chips.Count; i++)
        {
            bool active = i == indexA || i == indexB;
            _chips[i].Modulate = active ? focus : Quiet;
            _chips[i].Scale = active ? new Vector2(1.08f, 1.08f) : Vector2.One;
            _chips[i].ZIndex = active ? 2 : 0;
        }
    }

    public void ClearHighlights()
    {
        foreach (PanelContainer chip in _chips)
        {
            if (!GodotObject.IsInstanceValid(chip))
            {
                continue;
            }

            chip.Modulate = Colors.White;
            chip.Scale = Vector2.One;
            chip.ZIndex = 0;
        }
    }

    public void SoftenAll()
    {
        foreach (PanelContainer chip in _chips)
        {
            if (GodotObject.IsInstanceValid(chip))
            {
                chip.Modulate = Quiet;
                chip.Scale = Vector2.One;
            }
        }
    }

    /// <summary>
    /// Visually swaps two chip identities into each other's slots, then updates logical order.
    /// </summary>
    public async Task AnimateSwapAsync(int indexA, int indexB, float durationSeconds)
    {
        if (!IsValidPair(indexA, indexB) || durationSeconds <= 0.01f)
        {
            ApplySwapInstant(indexA, indexB);
            return;
        }

        KillActiveTween();
        LayoutChipsInstant();
        FocusIndices(indexA, indexB, forSwap: true);

        PanelContainer chipA = _chips[indexA];
        PanelContainer chipB = _chips[indexB];
        Vector2 posA = SlotPosition(indexA);
        Vector2 posB = SlotPosition(indexB);
        chipA.Position = posA;
        chipB.Position = posB;
        chipA.PivotOffset = chipA.Size / 2f;
        chipB.PivotOffset = chipB.Size / 2f;

        // Slight lift so the crossing path reads clearly.
        float arc = 14f;
        Vector2 midA = new((posA.X + posB.X) * 0.5f, posA.Y - arc);
        Vector2 midB = new((posA.X + posB.X) * 0.5f, posB.Y + arc);

        Tween tween = CreateTween();
        _activeTween = tween;
        tween.SetParallel(true);
        float half = durationSeconds * 0.5f;
        tween.TweenProperty(chipA, "position", midA, half)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(chipB, "position", midB, half)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        await ToSignal(tween, Tween.SignalName.Finished);

        if (!GodotObject.IsInstanceValid(this))
        {
            return;
        }

        tween = CreateTween();
        _activeTween = tween;
        tween.SetParallel(true);
        tween.TweenProperty(chipA, "position", posB, half)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(chipB, "position", posA, half)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        await ToSignal(tween, Tween.SignalName.Finished);

        if (!GodotObject.IsInstanceValid(this))
        {
            return;
        }

        // Swap logical slots while preserving the same Control instances.
        (_chips[indexA], _chips[indexB]) = (_chips[indexB], _chips[indexA]);
        (_values[indexA], _values[indexB]) = (_values[indexB], _values[indexA]);
        LayoutChipsInstant();
        FocusIndices(indexA, indexB, forSwap: true);
    }

    /// <summary>Smoothly restores chip values/order toward <paramref name="target"/> without destroying chips.</summary>
    public async Task AnimateRestoreAsync(IReadOnlyList<int> target, float durationSeconds)
    {
        if (target.Count != _chips.Count)
        {
            SetValues(target);
            return;
        }

        // Find a transposition if the restore is a single swap; otherwise fade-update.
        int diffA = -1;
        int diffB = -1;
        for (int i = 0; i < target.Count; i++)
        {
            if (target[i] != _values[i])
            {
                if (diffA < 0)
                {
                    diffA = i;
                }
                else if (diffB < 0)
                {
                    diffB = i;
                }
                else
                {
                    diffA = -1;
                    break;
                }
            }
        }

        if (diffA >= 0 && diffB >= 0
            && target[diffA] == _values[diffB]
            && target[diffB] == _values[diffA])
        {
            await AnimateSwapAsync(diffA, diffB, durationSeconds).ConfigureAwait(false);
            return;
        }

        // Multi-diff restore: briefly quiet, then settle labels.
        SoftenAll();
        if (durationSeconds > 0.05f)
        {
            await ToSignal(GetTree().CreateTimer(durationSeconds * 0.45), SceneTreeTimer.SignalName.Timeout);
        }

        UpdateValues(target);
        ClearHighlights();
        LayoutChipsInstant();
    }

    public void PlaySuccessPop()
    {
        foreach (PanelContainer chip in _chips)
        {
            if (!GodotObject.IsInstanceValid(chip))
            {
                continue;
            }

            chip.PivotOffset = chip.Size / 2f;
            Tween tween = CreateTween();
            tween.TweenProperty(chip, "scale", Vector2.One * 1.12f, 0.14f)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(chip, "scale", Vector2.One, 0.16f);
            chip.Modulate = FocusSwap;
            tween.Parallel().TweenProperty(chip, "modulate", Colors.White, 0.35f);
        }
    }

    private void ApplySwapInstant(int indexA, int indexB)
    {
        if (!IsValidPair(indexA, indexB))
        {
            return;
        }

        (_chips[indexA], _chips[indexB]) = (_chips[indexB], _chips[indexA]);
        (_values[indexA], _values[indexB]) = (_values[indexB], _values[indexA]);
        LayoutChipsInstant();
    }

    private bool IsValidPair(int indexA, int indexB) =>
        indexA >= 0 && indexB >= 0
        && indexA < _chips.Count && indexB < _chips.Count
        && indexA != indexB;

    private void LayoutChipsInstant()
    {
        for (int i = 0; i < _chips.Count; i++)
        {
            PanelContainer chip = _chips[i];
            chip.Position = SlotPosition(i);
            chip.Size = new Vector2(ChipSize, ChipSize);
            chip.CustomMinimumSize = new Vector2(ChipSize, ChipSize);
        }

        float width = _chips.Count == 0
            ? ChipSize
            : _chips.Count * ChipSize + Math.Max(0, _chips.Count - 1) * Separation;
        CustomMinimumSize = new Vector2(width, ChipSize + 4f);
        Size = CustomMinimumSize;
    }

    private static Vector2 SlotPosition(int index) =>
        new(index * (ChipSize + Separation), 0f);

    private void KillActiveTween()
    {
        if (_activeTween is not null && GodotObject.IsInstanceValid(_activeTween))
        {
            _activeTween.Kill();
        }

        _activeTween = null;
    }

    private void ClearChips()
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _chips.Clear();
    }

    private static PanelContainer CreateChip(int value)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(ChipSize, ChipSize),
            Size = new Vector2(ChipSize, ChipSize),
            MouseFilter = MouseFilterEnum.Ignore
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = ChipFill,
            BorderColor = ChipBorder,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusBottomLeft = 6,
            ContentMarginLeft = 4,
            ContentMarginTop = 4,
            ContentMarginRight = 4,
            ContentMarginBottom = 4
        });

        var label = new Label
        {
            Name = "Value",
            Text = value.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        label.AddThemeColorOverride("font_color", ChipText);
        label.AddThemeFontSizeOverride("font_size", 16);
        panel.AddChild(label);
        return panel;
    }
}
