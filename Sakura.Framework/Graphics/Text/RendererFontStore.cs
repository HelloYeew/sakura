// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Text;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class RendererFontStore : IFontStore
{
    private readonly TextureAtlas atlas;
    private readonly Dictionary<string, Lazy<Font>> fontCache = new Dictionary<string, Lazy<Font>>();
    private readonly List<string> fallbackFamilies = new List<string>();

    /// <summary>
    /// Fallback family names we have already warned about being unloaded, so the warning in
    /// GetFallbacks fires once per family rather than on every text layout.
    /// </summary>
    private readonly HashSet<string> warnedMissingFallbacks = new HashSet<string>();

    public int CacheVersion { get; private set; }

    private Font defaultFont;

    public RendererFontStore(IRenderer renderer)
    {
        atlas = new TextureAtlas(renderer, 1024, 1024);
    }

    private void loadFrameworkFonts(Storage resourceStorage)
    {
        // primary base font (NotoSans with Italics)
        loadFamily(resourceStorage, "NotoSans", hasItalics: true);

        if (fontCache.TryGetValue("NotoSans-Regular", out var reg))
        {
            defaultFont = reg.Value;
            fontCache["Default"] = reg;
            fontCache["NotoSans"] = reg; // Allow lookup by just family name
        }
        else
        {
            Logger.Warning("[FontLoader] NotoSans-Regular.ttf was not found. Default font is missing.");
        }

        // fallback families for various languages
        string[] fallbackFamiliesList = new[]
        {
            "NotoSansSC",
            "NotoSansTC",
            "NotoSansJP",
            "NotoSansKR",
            "NotoSansThai",
            "NotoSansArabic",
            "NotoSansDevanagari",
            "NotoSansHebrew"
        };

        foreach (string family in fallbackFamiliesList)
        {
            // These families don't have italics
            loadFamily(resourceStorage, family, hasItalics: false);
            AddFallbackFamily(family);
        }

        loadEmojiFonts(resourceStorage);

        // Material Symbols for IconSprite. These files are themselves variable fonts (fvar axes
        // wght / FILL / GRAD / opsz); the variable machinery lets IconSprite drive weight and
        // fill per icon (see IconSprite / FontUsage.Fill). We keep the single-file registration
        // here, the axes are applied at render time via FontVariation, not by loading extra files.
        loadMaterialSymbol(resourceStorage, "MaterialSymbolsOutlined");
        loadMaterialSymbol(resourceStorage, "MaterialSymbolsRounded");
        loadMaterialSymbol(resourceStorage, "MaterialSymbolsSharp");
        AddFallbackFamily("MaterialSymbolsOutlined");
    }

    /// <summary>
    /// Registers a single Material Symbols style, tolerating either the variable filename
    /// (<c>{style}-VF.ttf</c>) or the legacy per-style filename (<c>{style}-Regular.ttf</c>).
    /// Both the <c>{style}-Regular</c> and bare <c>{style}</c> keys resolve to the loaded font.
    /// </summary>
    private void loadMaterialSymbol(Storage storage, string style)
    {
        string filename = storage.Exists($"{style}-VF.ttf")
            ? $"{style}-VF.ttf"
            : $"{style}-Regular.ttf";

        AddFont(storage, filename, alias: $"{style}-Regular");
        addFontAlias($"{style}-Regular", style);
    }

    private void loadFamily(Storage storage, string family, bool hasItalics)
    {
        // Prefer a single OpenType variable file when one is present (collapses 9+ per-weight files
        // into one), otherwise fall back to the per-weight static files. Callers don't opt in — a
        // variable file "just works" and a static family behaves exactly as before.
        if (tryLoadVariableFamily(storage, family, hasItalics))
            return;

        loadStaticFamily(storage, family, hasItalics);
    }

    /// <summary>
    /// Attempts to load <paramref name="family"/> from a single variable file (Google Fonts naming
    /// <c>{family}[wght].ttf</c>, plus <c>{family}-Italic[wght].ttf</c> when italics are requested).
    /// Registers one shared <see cref="Font"/> and aliases every <c>{family}-{weight}</c> key to it;
    /// the requested weight is applied per-glyph at render time via <see cref="FontVariation"/>.
    /// Returns false if no variable upright file exists (so the static path can take over).
    /// </summary>
    private bool tryLoadVariableFamily(Storage storage, string family, bool hasItalics)
    {
        string uprightFile = findVariableFile(storage, family, italic: false);
        if (uprightFile == null)
            return false;

        string uprightKey = $"{family}-Variable";
        AddFont(storage, uprightFile, alias: uprightKey);

        // Every named weight resolves to the same variable instance.
        foreach (string weight in Enum.GetNames(typeof(FontWeights)))
            addFontAlias(uprightKey, $"{family}-{weight}");

        // Bare family name resolves to the variable instance too.
        addFontAlias(uprightKey, family);

        if (hasItalics)
        {
            string italicFile = findVariableFile(storage, family, italic: true);
            if (italicFile != null)
            {
                string italicKey = $"{family}-VariableItalic";
                AddFont(storage, italicFile, alias: italicKey);

                foreach (string weight in Enum.GetNames(typeof(FontWeights)))
                    addFontAlias(italicKey, $"{family}-{weight}Italic");
            }
        }

        Logger.Debug($"[FontLoader] loaded '{family}' as a variable font from {uprightFile}.");
        return true;
    }

    /// <summary>
    /// Locates a variable font file for <paramref name="family"/>, tolerating the common naming
    /// conventions Google Fonts ships and a bracket-free short form
    /// <c>{family}-VF.ttf</c>. Returns the first that exists or null.
    /// </summary>
    private static string findVariableFile(Storage storage, string family, bool italic)
    {
        string[] candidates = italic
            ? new[] { $"{family}-Italic[wght].ttf", $"{family}-Italic-VariableFont_wght.ttf", $"{family}-ItalicVF.ttf" }
            : new[] { $"{family}[wght].ttf", $"{family}-VariableFont_wght.ttf", $"{family}-VF.ttf" };

        foreach (string candidate in candidates)
        {
            if (storage.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Legacy path: one static TTF per weight (<c>{family}-{weight}.ttf</c>) plus optional italics.
    /// Unchanged behaviour, preserved so third-party apps that ship per-weight fonts keep working.
    /// </summary>
    private void loadStaticFamily(Storage storage, string family, bool hasItalics)
    {
        string[] weights = Enum.GetNames(typeof(FontWeights));

        foreach (string weight in weights)
        {
            string normalFileName = $"{family}-{weight}.ttf";

            // AddFont already has a try-catch and checks if the stream is null,
            // so it will safely skip weights that don't exist in the storage.
            AddFont(storage, normalFileName, alias: $"{family}-{weight}");

            // Add regular font as normal fallback too
            if (weight == nameof(FontWeights.Regular))
                addFontAlias($"{family}-{weight}", family);

            if (hasItalics)
            {
                string italicFileName = weight == "Regular" ? $"{family}-Italic.ttf" : $"{family}-{weight}Italic.ttf";
                AddFont(storage, italicFileName, alias: $"{family}-{weight}Italic");
            }
        }
    }

    /// <summary>
    /// Registers emoji fallback fonts in priority order:
    /// <list type="number">
    /// <item>On desktop macOS, the system "Apple Color Emoji" font (best native appearance, always
    /// up to date). Loaded from an absolute system path (skipped on iOS, which sandboxes system fonts)</item>
    /// <item>The bundled cross-platform <c>NotoColorEmoji.ttf</c></item>
    /// <item>Monochrome <c>NotoEmoji</c></item>
    /// </list>
    /// </summary>
    private void loadEmojiFonts(Storage resourceStorage)
    {
        bool notoColorAvailable = resourceStorage.Exists("NotoColorEmoji-Regular.ttf");
        if (notoColorAvailable)
            AddFont(resourceStorage, "NotoColorEmoji-Regular.ttf", alias: "NotoColorEmoji");

        Logger.Debug($"NotoColorEmoji.ttf is {(notoColorAvailable ? "available" : "not available")} in the resource storage.");

        bool colorEmojiInChain = false;

        // macOS system Apple Color Emoji
        if (RuntimeInfo.IsMacOS)
        {
            string[] appleEmojiPaths =
            {
                "/System/Library/Fonts/Apple Color Emoji.ttc",
                "/Library/Fonts/Apple Color Emoji.ttc"
            };

            foreach (string path in appleEmojiPaths)
            {
                if (!File.Exists(path))
                    continue;

                AddFontFromFile(path, alias: "AppleColorEmoji");
                AddFallbackFamily("AppleColorEmoji");
                colorEmojiInChain = true;
                Logger.Debug($"Using system Apple Color Emoji font from {path}");
                break;
            }
        }

        // NotoColorEmoji
        // Note for me in future: Still can't render COLRv1 version, please use normal bitmap version
        // https://github.com/googlefonts/noto-emoji/blob/main/fonts/NotoColorEmoji.ttf
        if (notoColorAvailable)
        {
            AddFallbackFamily("NotoColorEmoji");
            colorEmojiInChain = true;
        }

        // Monochrome NotoEmoji
        loadFamily(resourceStorage, "NotoEmoji", hasItalics: false);
        AddFallbackFamily("NotoEmoji");

        if (!colorEmojiInChain)
            Logger.Debug("No color emoji font available; falling back to monochrome NotoEmoji.");
    }

    public void LoadDefaultFont(Storage resourceStorage)
    {
        loadFrameworkFonts(resourceStorage);
    }

    public void AddFont(Storage storage, string filename, string alias = null!)
    {
        string name = alias ?? Path.GetFileNameWithoutExtension(filename);

        fontCache[name] = new Lazy<Font>(() =>
        {
            try
            {
                using var stream = storage.GetStream(filename);
                if (stream == null)
                {
                    Logger.Error($"Could not find font file: {filename}");
                    return null!;
                }

                var font = loadFontFromStream(name, stream);
                Logger.Debug($"Loaded font {name} from {filename}");

                GlobalStatistics.Get<int>("Fonts", "Loaded Fonts").Value++;

                return font;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load font {filename}: {ex.Message}");
                return null!;
            }
        });
    }

    /// <summary>
    /// Adds a font from an absolute path on the local filesystem, rather than from a
    /// <see cref="Storage"/>. Used for platform-provided system fonts (e.g. macOS "Apple Color Emoji").
    /// Loading is deferred until the font is first requested.
    /// </summary>
    /// <param name="filePath">Absolute path to the font file.</param>
    /// <param name="alias">Cache key for the font. If null, uses the filename without extension.</param>
    public void AddFontFromFile(string filePath, string alias = null!)
    {
        string name = alias ?? Path.GetFileNameWithoutExtension(filePath);

        fontCache[name] = new Lazy<Font>(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Logger.Error($"Could not find font file: {filePath}");
                    return null!;
                }

                var font = new Font(name, File.ReadAllBytes(filePath), atlas);
                Logger.Debug($"Loaded font {name} from {filePath}");

                GlobalStatistics.Get<int>("Fonts", "Loaded Fonts").Value++;

                return font;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load font {filePath}: {ex.Message}");
                return null!;
            }
        });
    }

    /// <summary>
    /// Registers an additional cache key that points at an already-registered font, sharing the same
    /// underlying <see cref="Font"/> instance. Use this instead of calling <see cref="AddFont"/> again
    /// for the same file, so the font is loaded once and lookups by either key return the same object
    /// (important for reference-identity comparisons in fallback resolution).
    /// </summary>
    private void addFontAlias(string existingKey, string alias)
    {
        if (existingKey == alias) return;

        if (fontCache.TryGetValue(existingKey, out var existing))
            fontCache[alias] = existing;
        else
            Logger.Warning($"Cannot alias font '{alias}' to missing key '{existingKey}'.");
    }

    private Font loadFontFromStream(string name, Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return new Font(name, ms.ToArray(), atlas);
    }

    public Font Get(FontUsage usage)
    {
        string specificKey = $"{usage.Family}-{usage.Weight}";

        if (usage.Italics)
            specificKey += "Italic";

        if (fontCache.TryGetValue(specificKey, out var font) && font.Value != null)
            return font.Value;

        if (usage.Italics)
        {
            string nonItalicKey = $"{usage.Family}-{usage.Weight}";
            if (fontCache.TryGetValue(nonItalicKey, out var nonItalic) && nonItalic.Value != null)
                return nonItalic.Value;
        }

        if (fontCache.TryGetValue(usage.Family, out var family) && family.Value != null)
            return family.Value;

        return defaultFont;
    }

    public Font Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return defaultFont;
        if (fontCache.TryGetValue(name, out var font) && font.Value != null) return font.Value;
        return defaultFont;
    }

    /// <summary>
    /// Derives the <see cref="FontVariation"/> for the requested usage (weight → <c>wght</c>, plus any
    /// Fill/Grade/OpticalSize overrides). Applied at render time; harmlessly ignored by static fonts.
    /// </summary>
    public FontVariation GetVariation(FontUsage usage) => usage.ToVariation();

    public void AddFallbackFamily(string familyName)
    {
        if (!fallbackFamilies.Contains(familyName))
            fallbackFamilies.Add(familyName);
    }

    public void InsertFallbackFamily(int index, string familyName)
    {
        if (!fallbackFamilies.Contains(familyName))
            fallbackFamilies.Insert(index, familyName);
    }

    public void ClearFallbackFamilies()
    {
        fallbackFamilies.Clear();
    }

    public IEnumerable<Font> GetFallbacks(FontUsage usage)
    {
        var returnedFonts = new HashSet<Font>();

        foreach (string family in fallbackFamilies)
        {
            var fallbackFont = Get(usage.With(family: family));

            if (fallbackFont == defaultFont || fallbackFont == null)
                fallbackFont = Get(family);

            // A registered fallback family that resolves to the default font was never loaded (its
            // file is missing, or AddFallbackFamily was called without a matching AddFont/loadFamily).
            // Such a family contributes nothing to glyph coverage; warn once so the misconfiguration
            // is visible instead of silently rendering missing glyphs as .notdef ("tofu").
            if (fallbackFont == null || fallbackFont == defaultFont)
            {
                if (warnedMissingFallbacks.Add(family))
                    Logger.Warning($"Fallback family '{family}' is registered but not loaded; it will not contribute glyphs. Did you forget to load it?");
                continue;
            }

            if (returnedFonts.Add(fallbackFont))
                yield return fallbackFont;
        }
    }

    public void ClearCaches()
    {
        atlas.Clear();

        foreach (var font in fontCache.Values)
        {
            if (font.IsValueCreated && font.Value != null)
            {
                font.Value.ClearCache();
            }
        }

        CacheVersion++;

        GlobalStatistics.Get<int>("Fonts", "Cached Glyphs").Value = 0;
        GlobalStatistics.Get<int>("Fonts", "Cache Version").Value = CacheVersion;
        Logger.Debug($"Font caches evicted. Cache version is now {CacheVersion}.");
    }

    public TextureAtlas Atlas => atlas;

    public void Dispose()
    {
        foreach (var font in fontCache.Values)
        {
            if (font.IsValueCreated && font.Value != null)
            {
                font.Value.Dispose();
            }
        }

        atlas.Dispose();
    }
}
