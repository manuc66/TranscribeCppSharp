using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TranscribeCppSharp.Audio;
using TranscribeCppSharp.Interop;
using Avalonia.Input.Platform;
using TranscribeCppSharp.Models;
using TranscribeCppSharp.SharedSource;
using TranscribeCppSharp.UI.Models;
using TranscribeCppSharp.UI.Services;

namespace TranscribeCppSharp.UI.ViewModels;

public partial class TranscriptionViewModel
{
    /// <summary>
    /// Rebuilds the global language list from the union of what the downloaded
    /// models declare, plus the ten fallback codes. The list is no longer derived
    /// from the selected model: the language is chosen first and filters the
    /// models, so it must stand on its own.
    /// </summary>
    private void RebuildGlobalLanguages()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string alias in DownloadedModelCatalog.Aliases())
        {
            var langs = UpstreamCatalog.Read(alias)?.Languages;
            if (langs is not null)
            {
                foreach (string l in langs)
                {
                    if (!string.IsNullOrWhiteSpace(l))
                    {
                        codes.Add(l);
                    }
                }
            }
        }

        bool anyLangDetect = DownloadedModelCatalog.Aliases().Any(a =>
        {
            var rec = UpstreamCatalog.Read(a);
            return rec?.Capabilities is not null
                && rec.Capabilities.TryGetValue("lang_detect", out var cap)
                && cap.Supported;
        });

        foreach (string f in new[] { "en", "fr", "de", "es", "it", "pt", "ru", "zh", "ja", "ko" })
        {
            codes.Add(f);
        }

        _syncingLanguage = true;
        try
        {
            AvailableLanguages.Clear();
            if (anyLangDetect)
            {
                AvailableLanguages.Add("auto");
            }
            // Keep the familiar fallback order first (en, fr, ...), so the default
            // language stays the first entry and existing selections survive.
            foreach (string f in new[] { "en", "fr", "de", "es", "it", "pt", "ru", "zh", "ja", "ko" })
            {
                if (codes.Contains(f))
                {
                    AvailableLanguages.Add(f);
                }
            }
            foreach (string c in codes)
            {
                if (!string.Equals(c, "auto", StringComparison.Ordinal)
                    && !new[] { "en", "fr", "de", "es", "it", "pt", "ru", "zh", "ja", "ko" }.Contains(c))
                {
                    AvailableLanguages.Add(c);
                }
            }
            if (AvailableLanguages.Count == 0)
            {
                AvailableLanguages.Add("en");
            }
            // "auto" is allowed to stay even when no downloaded model declares
            // language detection: the warning logic and the run treat it as a
            // distinct choice, and resetting it here would silently change the
            // reader's pick. Other values are repaired to the first available.
            if (!string.Equals(Language, "auto", StringComparison.Ordinal)
                && !AvailableLanguages.Contains(Language))
            {
                Language = AvailableLanguages.First();
            }

            // Re-push the selection: rebuilding the list drops the ComboBox's
            // match, and a value that did not change would otherwise never be
            // re-pushed. See LanguagePickerTests.
            OnPropertyChanged(nameof(Language));
        }
        finally
        {
            _syncingLanguage = false;
        }
    }

    private void UpdateAvailableLanguages(ModelCapabilitySnapshot? capabilities)
    {
        // The language list is now global (chosen first). The probe only
        // validates the current choice against what the model actually accepts,
        // and grows the global list with anything the catalogue did not declare.
        if (capabilities is null)
        {
            return;
        }

        foreach (string lang in capabilities.Languages)
        {
            if (!string.IsNullOrWhiteSpace(lang) && !AvailableLanguages.Contains(lang))
            {
                AvailableLanguages.Add(lang);
            }
        }
        if (capabilities.SupportsLanguageDetect && !AvailableLanguages.Contains("auto"))
        {
            AvailableLanguages.Insert(0, "auto");
        }

        if (AvailableLanguages.Count == 0)
        {
            // Empty here, so the "does it already contain en" test the earlier
            // version made could never be true.
            AvailableLanguages.Add("en");
        }

        // If the model does not accept the chosen language, fall back to the
        // first one it does accept (or "en" when it declares nothing).
        if (capabilities.Languages.Count > 0
            && !capabilities.Languages.Contains(Language)
            && !string.Equals(Language, "auto", StringComparison.Ordinal))
        {
            Language = capabilities.Languages[0];
        }
        else if (string.Equals(Language, "auto", StringComparison.Ordinal)
                 && !capabilities.SupportsLanguageDetect)
        {
            Language = capabilities.Languages.Count > 0
                ? capabilities.Languages[0]
                : (AvailableLanguages.Contains("en") ? "en" : AvailableLanguages[0]);
        }

        OnPropertyChanged(nameof(Language));
    }

    /// <summary>
    /// Rebuilds the target-language choices from what a model says it translates
    /// into, or leaves the single default when nothing has answered.
    /// </summary>
    /// <remarks>
    /// The selection is re-seated explicitly rather than left alone. The list is
    /// rebuilt with new objects, so the ComboBox's selection is dropped, and
    /// reassigning an equal-but-different record would not fire the setter that
    /// restores it. Going through null first guarantees the transition; the value
    /// that matters is the one assigned last, so a transient unset is never seen by
    /// a run — nothing runs while a model is still being probed.
    /// <para>
    /// A choice the model does not declare is dropped rather than kept: the native
    /// library rejects a target outside the model's list, so keeping it would hand
    /// the reader a value that cannot work.
    /// </para>
    /// </remarks>
    private void UpdateTargetOptions(ModelCapabilitySnapshot? capabilities)
    {
        string? current = TargetLanguage;

        TargetOptions.Clear();
        TargetOptions.Add(DefaultTargetOption);
        if (capabilities is not null)
        {
            foreach (string code in capabilities.TranslateTargetLanguages)
            {
                if (!string.IsNullOrEmpty(code))
                {
                    TargetOptions.Add(new TargetOption(code, code));
                }
            }
        }

        SelectedTargetOption = null;
        SelectedTargetOption = TargetOptions.FirstOrDefault(o => o.Code == current)
            ?? DefaultTargetOption;
    }

    /// <summary>
    /// The languages offered before a model has answered.
    /// </summary>
    /// <remarks>
    /// Called twice: once in the constructor, so the picker has items before the
    /// window's DataContext is set, and again when a model cannot be probed — not
    /// downloaded, or the probe failed. It is a fixed list because nothing has said
    /// otherwise yet; once a model reports, its own languages replace it.
    /// </remarks>
    private void AddFallbackLanguages()
    {
        AvailableLanguages.Add("en");
        AvailableLanguages.Add("fr");
        AvailableLanguages.Add("de");
        AvailableLanguages.Add("es");
        AvailableLanguages.Add("it");
        AvailableLanguages.Add("pt");
        AvailableLanguages.Add("ru");
        AvailableLanguages.Add("zh");
        AvailableLanguages.Add("ja");
        AvailableLanguages.Add("ko");
    }
}
