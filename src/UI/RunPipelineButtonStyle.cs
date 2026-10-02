using Godot;

namespace PipelineArchitect.UI;

/// <summary>
/// Shared industrial styling for the interactive factory Execute action button.
/// </summary>
public static class RunPipelineButtonStyle
{
    public const string IdleText = "▶ EXECUTE FACTORY";
    public const string RunningText = "⏸ RUNNING...";
    public const string SuccessText = "✓ FACTORY COMPLETE";
    public const string FailedText = "⚠ PIPELINE STOPPED";
    public const string EditingText = "▶ EXECUTE FACTORY";

    public static readonly Color SystemGreen = new("2A9D8F");
    public static readonly Color SystemGreenHover = new("34B4A3");
    public static readonly Color SystemGreenPressed = new("238578");
    public static readonly Color SystemGreenDisabled = new("1A5C55");
    public static readonly Color WarningAmber = new("E9C46A");
    public static readonly Color DangerRed = new("E63946");
    public static readonly Color LabelWhite = new("F8F9FA");

    private const float HoverScale = 1.05f;

    public static void Apply(Button button)
    {
        button.Text = IdleText;
        button.CustomMinimumSize = new Vector2(220, 56);
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.FocusMode = Control.FocusModeEnum.None;
        button.AddThemeColorOverride("font_color", LabelWhite);
        button.AddThemeColorOverride("font_hover_color", LabelWhite);
        button.AddThemeColorOverride("font_pressed_color", LabelWhite);
        button.AddThemeColorOverride("font_disabled_color", new Color(1f, 1f, 1f, 0.55f));
        button.AddThemeFontSizeOverride("font_size", 17);

        ApplyGreenStyles(button);

        button.Resized += () => button.PivotOffset = button.Size / 2f;
        button.PivotOffset = button.Size / 2f;

        button.MouseEntered += () => PlayHoverScale(button, HoverScale);
        button.MouseExited += () => PlayHoverScale(button, 1f);
    }

    public static void SetIdle(Button button)
    {
        ApplyGreenStyles(button);
        button.Text = IdleText;
        button.Scale = Vector2.One;
    }

    public static void SetRunning(Button button)
    {
        ApplyGreenStyles(button);
        button.Text = RunningText;
        button.Disabled = true;
        button.Scale = Vector2.One;
    }

    public static void SetSuccess(Button button)
    {
        ApplyGreenStyles(button);
        button.Text = SuccessText;
        button.Disabled = true;
        button.Scale = Vector2.One;
    }

    public static void SetFailed(Button button)
    {
        button.AddThemeStyleboxOverride("normal", CreateFlat(DangerRed));
        button.AddThemeStyleboxOverride("hover", CreateFlat(DangerRed.Lightened(0.08f)));
        button.AddThemeStyleboxOverride("pressed", CreateFlat(DangerRed.Darkened(0.1f)));
        button.AddThemeStyleboxOverride("disabled", CreateFlat(DangerRed.Darkened(0.25f), cornerRadius: 8, shadowSize: 0));
        button.Text = FailedText;
        button.Disabled = true;
        button.Scale = Vector2.One;
    }

    private static void ApplyGreenStyles(Button button)
    {
        button.AddThemeStyleboxOverride("normal", CreateFlat(SystemGreen));
        button.AddThemeStyleboxOverride("hover", CreateFlat(SystemGreenHover));
        button.AddThemeStyleboxOverride("pressed", CreateFlat(SystemGreenPressed));
        button.AddThemeStyleboxOverride("disabled", CreateFlat(SystemGreenDisabled, cornerRadius: 8, shadowSize: 0));
    }

    private static StyleBoxFlat CreateFlat(Color fill, int cornerRadius = 10, int shadowSize = 4)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            CornerRadiusTopLeft = cornerRadius,
            CornerRadiusTopRight = cornerRadius,
            CornerRadiusBottomRight = cornerRadius,
            CornerRadiusBottomLeft = cornerRadius,
            ContentMarginLeft = 18,
            ContentMarginTop = 12,
            ContentMarginRight = 18,
            ContentMarginBottom = 12,
            ShadowSize = shadowSize,
            ShadowColor = new Color(0f, 0f, 0f, 0.35f),
            ShadowOffset = new Vector2(0, 3)
        };
    }

    private static void PlayHoverScale(Button button, float targetScale)
    {
        if (!GodotObject.IsInstanceValid(button) || button.Disabled)
        {
            return;
        }

        button.PivotOffset = button.Size / 2f;
        Tween tween = button.CreateTween();
        tween.TweenProperty(button, "scale", Vector2.One * targetScale, 0.12f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }
}
