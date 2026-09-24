// Loc.cs
// Minimal localization layer — every user-facing string in the game goes
// through Loc.Get(key). Start simple (in-code table); later this can load
// from JSON/CSV or Unity Localization without touching call sites.
//
// ARABIC NOTE: stock TextMeshPro does NOT shape Arabic (letters disconnect).
// Use the free RTLTMPro package: its RTLTextMeshPro component inherits from
// TextMeshProUGUI, so every TMP_Text reference in our scripts accepts it.

using System;
using System.Collections.Generic;

public enum GameLanguage { English, Arabic }

public static class Loc
{
    public static GameLanguage Current { get; private set; } = GameLanguage.English;

    /// UI subscribes to refresh its texts when the language changes.
    public static event Action OnLanguageChanged;

    public static bool IsRTL => Current == GameLanguage.Arabic;

    public static void SetLanguage(GameLanguage lang)
    {
        if (Current == lang) return;
        Current = lang;
        OnLanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        var table = Current == GameLanguage.Arabic ? AR : EN;
        if (table.TryGetValue(key, out var value)) return value;
        return $"#{key}#";   // visible marker for missing keys
    }

    // ------------------------------------------------------------ TABLES

    private static readonly Dictionary<string, string> EN = new()
    {
        ["loading.generic"]     = "Loading...",
        ["loading.signin"]      = "Signing in...",
        ["loading.connecting"]  = "Connecting...",

        ["popup.error.title"]   = "Error",
        ["popup.info.title"]    = "Info",
        ["error.signin"]        = "Could not sign in. Check your connection and try again.",
        ["error.network"]       = "Network error. Please try again.",

        ["btn.ok"]              = "OK",
        ["btn.retry"]           = "Retry",
        ["btn.cancel"]          = "Cancel",
        ["btn.yes"]             = "Yes",
        ["btn.no"]              = "No",

        ["lobby.copied"]        = "Copied!",
    };

    private static readonly Dictionary<string, string> AR = new()
    {
        ["loading.generic"]     = "جاري التحميل...",
        ["loading.signin"]      = "جاري تسجيل الدخول...",
        ["loading.connecting"]  = "جاري الاتصال...",

        ["popup.error.title"]   = "خطأ",
        ["popup.info.title"]    = "معلومة",
        ["error.signin"]        = "تعذر تسجيل الدخول. تحقق من اتصالك وحاول مجددًا.",
        ["error.network"]       = "خطأ في الشبكة. حاول مرة أخرى.",

        ["btn.ok"]              = "حسنًا",
        ["btn.retry"]           = "إعادة المحاولة",
        ["btn.cancel"]          = "إلغاء",
        ["btn.yes"]             = "نعم",
        ["btn.no"]              = "لا",

        ["lobby.copied"]        = "تم النسخ!",
    };
}
