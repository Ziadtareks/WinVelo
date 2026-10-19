using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace WinVelo.App.Services;

/// <summary>
/// Manages dynamic design token brushes across Dark and Light themes.
/// Enforces WCAG AA text contrast ratio >= 4.5:1 in both modes.
/// </summary>
public static class DesignThemeManager
{
    public static void ApplyTheme(ApplicationTheme theme)
    {
        if (Application.Current == null) return;
        var res = Application.Current.Resources;

        if (theme == ApplicationTheme.Light)
        {
            SetBrush(res, "BgDeepBrush", "#F3F4F6");
            SetBrush(res, "BgSidebarBrush", "#E5E7EB");
            SetBrush(res, "BgCardBrush", "#FFFFFF");
            SetBrush(res, "BgCardHoverBrush", "#F9FAFB");
            SetBrush(res, "BorderSubtleBrush", "#D1D5DB");
            SetBrush(res, "BorderHoverBrush", "#9CA3AF");

            // Text contrast: #111827 on #FFFFFF is 16:1, #374151 is 9:1, #4B5563 is 7:1
            SetBrush(res, "TextPrimaryBrush", "#111827");
            SetBrush(res, "TextSecondaryBrush", "#374151");
            SetBrush(res, "TextMutedBrush", "#4B5563");

            SetBrush(res, "AccentPrimaryBrush", "#2563EB");
            SetBrush(res, "AccentHoverBrush", "#1D4ED8");
            SetBrush(res, "AccentPressedBrush", "#1E40AF");
            SetBrush(res, "AccentBrush", "#1D4ED8");
            SetBrush(res, "AccentSubtleBrush", "#DBEAFE");

            // Status Badges
            SetBrush(res, "StatusAppliedBgBrush", "#D1FAE5");
            SetBrush(res, "StatusAppliedFgBrush", "#065F46");
            SetBrush(res, "StatusAppliedBorderBrush", "#A7F3D0");

            SetBrush(res, "StatusDefaultBgBrush", "#F3F4F6");
            SetBrush(res, "StatusDefaultFgBrush", "#374151");
            SetBrush(res, "StatusDefaultBorderBrush", "#D1D5DB");

            // Risk Badges (Light Theme)
            SetBrush(res, "RiskSafeBgBrush", "#D1FAE5");
            SetBrush(res, "RiskSafeFgBrush", "#065F46");
            SetBrush(res, "RiskSafeBorderBrush", "#A7F3D0");

            SetBrush(res, "RiskLowBgBrush", "#DBEAFE");
            SetBrush(res, "RiskLowFgBrush", "#1E40AF");
            SetBrush(res, "RiskLowBorderBrush", "#BFDBFE");

            SetBrush(res, "RiskModerateBgBrush", "#FEF3C7");
            SetBrush(res, "RiskModerateFgBrush", "#92400E");
            SetBrush(res, "RiskModerateBorderBrush", "#FDE68A");
            SetBrush(res, "RiskMediumBgBrush", "#FEF3C7");
            SetBrush(res, "RiskMediumFgBrush", "#92400E");
            SetBrush(res, "RiskMediumBorderBrush", "#FDE68A");

            SetBrush(res, "RiskCautionBgBrush", "#FEE2E2");
            SetBrush(res, "RiskCautionFgBrush", "#991B1B");
            SetBrush(res, "RiskCautionBorderBrush", "#FECACA");
            SetBrush(res, "RiskHighBgBrush", "#FEE2E2");
            SetBrush(res, "RiskHighFgBrush", "#991B1B");
            SetBrush(res, "RiskHighBorderBrush", "#FECACA");
        }
        else
        {
            SetBrush(res, "BgDeepBrush", "#0F1117");
            SetBrush(res, "BgSidebarBrush", "#161922");
            SetBrush(res, "BgCardBrush", "#1E222D");
            SetBrush(res, "BgCardHoverBrush", "#262B38");
            SetBrush(res, "BorderSubtleBrush", "#2A303C");
            SetBrush(res, "BorderHoverBrush", "#3B4252");

            // Text contrast: #F9FAFB on #0F1117 is >14:1, #D1D5DB is >8.5:1, #9CA3AF is >=4.8:1
            SetBrush(res, "TextPrimaryBrush", "#F9FAFB");
            SetBrush(res, "TextSecondaryBrush", "#D1D5DB");
            SetBrush(res, "TextMutedBrush", "#9CA3AF");

            SetBrush(res, "AccentPrimaryBrush", "#3B82F6");
            SetBrush(res, "AccentHoverBrush", "#60A5FA");
            SetBrush(res, "AccentPressedBrush", "#2563EB");
            SetBrush(res, "AccentBrush", "#60A5FA");
            SetBrush(res, "AccentSubtleBrush", "#263B82F6");

            // Status Badges
            SetBrush(res, "StatusAppliedBgBrush", "#064E3B");
            SetBrush(res, "StatusAppliedFgBrush", "#34D399");
            SetBrush(res, "StatusAppliedBorderBrush", "#059669");

            SetBrush(res, "StatusDefaultBgBrush", "#1F2937");
            SetBrush(res, "StatusDefaultFgBrush", "#D1D5DB");
            SetBrush(res, "StatusDefaultBorderBrush", "#374151");

            // Risk Badges (Dark Theme)
            SetBrush(res, "RiskSafeBgBrush", "#064E3B");
            SetBrush(res, "RiskSafeFgBrush", "#34D399");
            SetBrush(res, "RiskSafeBorderBrush", "#059669");

            SetBrush(res, "RiskLowBgBrush", "#1E3A8A");
            SetBrush(res, "RiskLowFgBrush", "#93C5FD");
            SetBrush(res, "RiskLowBorderBrush", "#3B82F6");

            SetBrush(res, "RiskModerateBgBrush", "#451A03");
            SetBrush(res, "RiskModerateFgBrush", "#FCD34D");
            SetBrush(res, "RiskModerateBorderBrush", "#D97706");
            SetBrush(res, "RiskMediumBgBrush", "#451A03");
            SetBrush(res, "RiskMediumFgBrush", "#FCD34D");
            SetBrush(res, "RiskMediumBorderBrush", "#D97706");

            SetBrush(res, "RiskCautionBgBrush", "#450A0A");
            SetBrush(res, "RiskCautionFgBrush", "#FCA5A5");
            SetBrush(res, "RiskCautionBorderBrush", "#DC2626");
            SetBrush(res, "RiskHighBgBrush", "#450A0A");
            SetBrush(res, "RiskHighFgBrush", "#FCA5A5");
            SetBrush(res, "RiskHighBorderBrush", "#DC2626");
        }
    }

    private static void SetBrush(ResourceDictionary res, string key, string hexColor)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        brush.Freeze();
        res[key] = brush;
    }
}
