// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Text;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class RendererFontStore : IFontStore
{
    private readonly TextureAtlas atlas;
    private readonly Dictionary<string, Lazy<Font>> fontCache = new Dictionary<string, Lazy<Font>>();

    /// <summary>
    /// One registered fallback family and the script it was claimed for. <see cref="FontScript.Any"/>
    /// means the family applies to every script, which is what the script-less
    /// <see cref="AddFallbackFamily(string)"/> registers.
    /// </summary>
    /// <param name="Family">The family name, as registered in the font cache.</param>
    /// <param name="Script">The script claimed, or <see cref="FontScript.Any"/> for every script.</param>
    /// <param name="Framework">
    /// True for the framework's own bundled families. An application claim always outranks these for
    /// the script it claims, regardless of registration order — the framework registers its fonts
    /// during <see cref="LoadDefaultFont"/>, which runs before any application code, so ordering alone
    /// would make an application claim unreachable.
    /// </param>
    private readonly record struct FallbackEntry(string Family, FontScript Script, bool Framework);

    private readonly List<FallbackEntry> fallbackEntries = new List<FallbackEntry>();

    /// <summary>
    /// Families registered with <see cref="FontScript.Auto"/>, whose claims are derived from what the
    /// font actually covers. Resolution needs the font loaded, so it is deferred until a chain is
    /// first built rather than done at registration time.
    /// </summary>
    private readonly List<string> pendingAutoClaims = new List<string>();

    /// <summary>
    /// Fallback family names we have already warned about being unloaded, so the warning in
    /// GetFallbacks fires once per family rather than on every text layout.
    /// </summary>
    private readonly HashSet<string> warnedMissingFallbacks = new HashSet<string>();

    /// <summary>
    /// Claims we have already warned do not match the font's coverage, so the warning fires once per
    /// (family, script) rather than on every text layout.
    /// </summary>
    private readonly HashSet<(string Family, FontScript Script)> warnedUncoveredClaims = new HashSet<(string, FontScript)>();

    private FontScript hanScript = defaultHanScript();

    /// <summary>
    /// Which language's forms to prefer for unified CJK ideographs, the one thing a codepoint cannot
    /// settle: 漢 is drawn differently in Japanese, Korean and the two Chinese variants while being the
    /// same character. Defaults to the OS UI language, and is only consulted after the application's
    /// own CJK claims — an application shipping a single CJK family never needs to set it.
    /// </summary>
    public FontScript HanScript
    {
        get => hanScript;
        set
        {
            if (hanScript == value)
                return;

            hanScript = value;
            invalidateFallbackCache();
        }
    }

    /// <summary>
    /// The CJK language to assume from the OS UI language. Falls back to simplified Chinese, which is
    /// the order the framework's own families were historically registered in.
    /// </summary>
    private static FontScript defaultHanScript()
    {
        var culture = CultureInfo.CurrentUICulture;

        switch (culture.TwoLetterISOLanguageName)
        {
            case "ja":
                return FontScript.Japanese;

            case "ko":
                return FontScript.Korean;

            case "zh":
                string name = culture.Name;

                bool traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                                   || name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                                   || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                                   || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);

                return traditional ? FontScript.ChineseTraditional : FontScript.ChineseSimplified;

            default:
                return FontScript.ChineseSimplified;
        }
    }

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

        // The base font is a fallback in its own right, not just the default primary: it is the only
        // bundled family covering Cyrillic, Greek and Vietnamese, so a label asking for a Latin-only
        // application font needs to reach it. Registered first so it leads the generic tail.
        addFrameworkFallback("NotoSans", FontScript.Any);

        // Per-script fallback families. Each is claimed for the script it is drawn for, so a request
        // for Japanese reaches NotoSansJP instead of whichever family happens to have the codepoint.
        (string Family, FontScript Script)[] fallbackFamiliesList =
        {
            ("NotoSansSC", FontScript.ChineseSimplified),
            ("NotoSansTC", FontScript.ChineseTraditional),
            ("NotoSansJP", FontScript.Japanese),
            ("NotoSansKR", FontScript.Korean),
            ("NotoSansThai", FontScript.Thai),
            ("NotoSansArabic", FontScript.Arabic),
            ("NotoSansDevanagari", FontScript.Devanagari),
            ("NotoSansHebrew", FontScript.Hebrew)
        };

        foreach ((string family, var script) in fallbackFamiliesList)
        {
            // These families don't have italics
            loadFamily(resourceStorage, family, hasItalics: false);
            addFrameworkFallback(family, script);
        }

        loadEmojiFonts(resourceStorage);

        // Material Symbols for IconSprite. These files are themselves variable fonts (fvar axes
        // wght / FILL / GRAD / opsz); the variable machinery lets IconSprite drive weight and
        // fill per icon (see IconSprite / FontUsage.Fill). We keep the single-file registration
        // here, the axes are applied at render time via FontVariation, not by loading extra files.
        loadMaterialSymbol(resourceStorage, "MaterialSymbolsOutlined");
        loadMaterialSymbol(resourceStorage, "MaterialSymbolsRounded");
        loadMaterialSymbol(resourceStorage, "MaterialSymbolsSharp");
        addFrameworkFallback("MaterialSymbolsOutlined", FontScript.Any);
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

    /// <summary>
    /// Public entry point for applications to register their own font family with the same
    /// variable-aware loading the framework uses for its built-in fonts. Delegates to <see cref="loadFamily"/>,
    /// and claims <paramref name="script"/> for the family when one is given.
    /// </summary>
    public void AddFontFamily(Storage storage, string family, bool hasItalics = false, FontScript? script = null)
    {
        loadFamily(storage, family, hasItalics);

        if (script.HasValue)
            AddFallbackFamily(family, script.Value);
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
                addFrameworkFallback("AppleColorEmoji", FontScript.Emoji);
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
            addFrameworkFallback("NotoColorEmoji", FontScript.Emoji);
            colorEmojiInChain = true;
        }

        // Monochrome NotoEmoji
        loadFamily(resourceStorage, "NotoEmoji", hasItalics: false);
        addFrameworkFallback("NotoEmoji", FontScript.Emoji);

        if (!colorEmojiInChain)
            Logger.Debug("No color emoji font available; falling back to monochrome NotoEmoji.");
    }

    public void LoadDefaultFont(Storage resourceStorage)
    {
        loadFrameworkFonts(resourceStorage);
    }

    /// <summary>
    /// Adds a single font file under one lookup key (<paramref name="alias"/>, or the filename without
    /// extension). Low-level primitive as it does not create <c>{family}-{weight}</c> keys or expand a
    /// variable font into weights, so <see cref="Get(FontUsage)"/> will only resolve it via an exact
    /// key/bare-family match. For loading a font family prefer <see cref="AddFontFamily"/>; reach for
    /// <see cref="AddFont"/> only when you deliberately want manual control over a single key.
    /// </summary>
    public void AddFont(Storage storage, string filename, string alias = null!)
    {
        invalidateFallbackCache();

        string name = alias ?? Path.GetFileNameWithoutExtension(filename);

        fontCache[name] = new Lazy<Font>(() =>
        {
            try
            {
                // A filesystem-backed storage can be mapped rather than read, which is the cheap path.
                // GetFileSystemPath returns null for anything else (an embedded resource, an archive), and
                // those genuinely have to be copied. Same routing SF-13 uses for audio.
                string? filePath = storage.GetFileSystemPath(filename);

                if (filePath != null)
                {
                    var mapped = loadFontFromFile(name, filePath);

                    if (mapped != null)
                    {
                        GlobalStatistics.Get<int>("Fonts", "Loaded Fonts").Value++;
                        return mapped;
                    }
                }

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
        invalidateFallbackCache();

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

                var font = loadFontFromFile(name, filePath);

                if (font == null)
                {
                    Logger.Error($"Font file could not be read: {filePath}");
                    return null!;
                }

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
        {
            fontCache[alias] = existing;
            invalidateFallbackCache();
        }
        else
            Logger.Warning($"Cannot alias font '{alias}' to missing key '{existingKey}'.");
    }

    /// <summary>
    /// Builds a face from a file on disk, mapping it rather than reading it where the platform allows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Falls back to reading the file into unmanaged memory when mapping is unavailable — a filesystem that
    /// does not support it, or a file held exclusively elsewhere. Mapping is an optimization, not a
    /// contract.
    /// </para>
    /// </remarks>
    /// <returns>The face, or null if the file could not be read at all.</returns>
    private Font? loadFontFromFile(string name, string filePath)
    {
        INativeBytes? fontData = NativeFileMapping.CreateFrom(filePath);

        if (fontData != null)
            Logger.Debug($"Mapped font {name} from {filePath} ({fontData.Length / 1024} KB, no copy)");
        else
        {
            fontData = NativeMemoryBuffer.CreateFromFile(filePath, NativeMemoryCategory.Fonts);

            if (fontData == null)
                return null;

            Logger.Debug($"Read font {name} from {filePath} ({fontData.Length / 1024} KB into unmanaged memory)");
        }

        return new Font(name, fontData, atlas);
    }

    /// <summary>
    /// Reads a font from a stream into unmanaged memory and builds the face from it.
    /// </summary>
    /// <exception cref="InvalidDataException">If the stream held no bytes.</exception>
    private Font loadFontFromStream(string name, Stream stream)
    {
        var fontData = NativeMemoryBuffer.CreateFrom(stream, NativeMemoryCategory.Fonts)
                       ?? throw new InvalidDataException($"Font stream for '{name}' held no bytes.");

        return new Font(name, fontData, atlas);
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
    /// Resolves <paramref name="family"/> at the usage's weight/italics through registered keys only,
    /// returning null when the family was never loaded rather than standing in the default font.
    /// </summary>
    /// <remarks>
    /// Fallback resolution needs this distinction: <see cref="Get(FontUsage)"/> answers with the default
    /// font both for a family that failed to load and for the base family itself, so a chain built on it
    /// can never contain the base family.
    /// </remarks>
    private Font getRegistered(FontUsage usage, string family)
    {
        if (string.IsNullOrEmpty(family))
            return null;

        string weighted = $"{family}-{usage.Weight}";

        if (usage.Italics && tryGetLoaded(weighted + "Italic", out var italic))
            return italic;

        if (tryGetLoaded(weighted, out var font))
            return font;

        return tryGetLoaded(family, out var bare) ? bare : null;
    }

    private bool tryGetLoaded(string key, out Font font)
    {
        font = fontCache.TryGetValue(key, out var lazy) ? lazy.Value : null;
        return font != null;
    }

    /// <summary>
    /// Derives the <see cref="FontVariation"/> for the requested usage (weight → <c>wght</c>, plus any
    /// Fill/Grade/OpticalSize overrides). Applied at render time; harmlessly ignored by static fonts.
    /// </summary>
    public FontVariation GetVariation(FontUsage usage) => usage.ToVariation();

    public void AddFallbackFamily(string familyName) => AddFallbackFamily(familyName, FontScript.Any);

    public void AddFallbackFamily(string familyName, FontScript script)
    {
        if (script == FontScript.Auto)
        {
            if (!pendingAutoClaims.Contains(familyName))
            {
                pendingAutoClaims.Add(familyName);
                invalidateFallbackCache();
            }

            return;
        }

        addEntry(new FallbackEntry(familyName, script, Framework: false));
    }

    public void SetScriptFamily(FontScript script, string familyName)
    {
        if (script == FontScript.Auto)
        {
            AddFallbackFamily(familyName, FontScript.Auto);
            return;
        }

        var entry = new FallbackEntry(familyName, script, Framework: false);

        if (fallbackEntries.Contains(entry))
            fallbackEntries.Remove(entry);

        // Ahead of every existing claim, so the last caller wins for this script. Claims for other
        // scripts are unaffected: ordering within the list only decides ties inside one tier.
        fallbackEntries.Insert(0, entry);
        invalidateFallbackCache();
    }

    /// <summary>
    /// Registers one of the framework's own bundled families. Kept separate from
    /// <see cref="AddFallbackFamily(string,FontScript)"/> so an application claim can outrank it for the
    /// same script even though the framework registers first.
    /// </summary>
    private void addFrameworkFallback(string familyName, FontScript script)
        => addEntry(new FallbackEntry(familyName, script, Framework: true));

    private void addEntry(FallbackEntry entry)
    {
        // A family may hold more than one claim (a CJK family covering both kana and ideographs), so
        // duplicates are rejected per (family, script) rather than per family.
        if (fallbackEntries.Contains(entry))
            return;

        fallbackEntries.Add(entry);
        invalidateFallbackCache();
    }

    public void InsertFallbackFamily(int index, string familyName)
    {
        var entry = new FallbackEntry(familyName, FontScript.Any, Framework: false);

        if (fallbackEntries.Contains(entry))
            return;

        fallbackEntries.Insert(Math.Clamp(index, 0, fallbackEntries.Count), entry);
        invalidateFallbackCache();
    }

    public void ClearFallbackFamilies()
    {
        fallbackEntries.Clear();
        pendingAutoClaims.Clear();
        invalidateFallbackCache();
    }

    /// <summary>
    /// Turns every <see cref="FontScript.Auto"/> registration into concrete claims, by probing what the
    /// font actually covers. Runs once per registration, the first time a chain is built — probing needs
    /// the font loaded, and doing it eagerly would defeat the store's lazy loading.
    /// </summary>
    /// <remarks>
    /// A script already claimed by the application is left alone, so an explicit claim always beats a
    /// derived one and two auto-registered families resolve in registration order. Latin is never
    /// auto-claimed (see <see cref="FontScripts.AutoClaimable"/>).
    /// </remarks>
    private void resolvePendingAutoClaims()
    {
        if (pendingAutoClaims.Count == 0)
            return;

        // Taken by value: addEntry invalidates, and the pending list is emptied as we go.
        string[] pending = pendingAutoClaims.ToArray();
        pendingAutoClaims.Clear();

        foreach (string family in pending)
        {
            var font = Get(family);

            if (font.IsNull() || (font == defaultFont && family != "NotoSans"))
            {
                Logger.Warning($"[FontLoader] '{family}' was registered with FontScript.Auto but is not loaded, so no script could be claimed for it.");
                continue;
            }

            var claimed = new List<FontScript>();

            foreach (var script in FontScripts.AutoClaimable)
            {
                if (isClaimedByApplication(script))
                    continue;

                uint probe = FontScripts.ProbeFor(script);

                if (probe == 0 || !font.HasGlyph(probe))
                    continue;

                addEntry(new FallbackEntry(family, script, Framework: false));
                claimed.Add(script);
            }

            if (claimed.Count == 0)
                Logger.Debug($"[FontLoader] '{family}' (FontScript.Auto) covers no unclaimed script; it stays available as a primary font only.");
            else
                Logger.Debug($"[FontLoader] '{family}' (FontScript.Auto) claimed {string.Join(", ", claimed)}.");
        }
    }

    private bool isClaimedByApplication(FontScript script)
    {
        foreach (var entry in fallbackEntries)
        {
            if (!entry.Framework && entry.Script == script)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The fallback entries to consult for <paramref name="script"/>, most preferred first.
    /// </summary>
    /// <remarks>
    /// <para>The tiers, in order:</para>
    /// <list type="number">
    /// <item>application claims for the script itself, then for the scripts related to it</item>
    /// <item>application families registered for no particular script</item>
    /// <item>the framework's family for the script (for ideographs, the one matching <see cref="HanScript"/>)</item>
    /// <item>everything else in registration order</item>
    /// </list>
    /// <para>
    /// An application claim outranking the framework's family is the whole point: the framework loads
    /// its fonts before any application code runs, so a single ordered list can never let an
    /// application override one script without also overriding every script that family happens to
    /// cover. The last tier is what keeps text rendering at all for scripts nobody claimed — dropping
    /// it would turn a wrong-font glyph into a missing one.
    /// </para>
    /// </remarks>
    private List<FallbackEntry> orderedEntriesFor(FontScript script)
    {
        var ordered = new List<FallbackEntry>();
        var used = new HashSet<FallbackEntry>();

        void emit(Func<FallbackEntry, bool> match)
        {
            foreach (var entry in fallbackEntries)
            {
                if (match(entry) && used.Add(entry))
                    ordered.Add(entry);
            }
        }

        bool han = script == FontScript.Han;
        bool cjk = FontScripts.IsCJK(script);

        emit(e => !e.Framework && e.Script == script);

        // Ideographs draw on every CJK claim the application made, in registration order: a game
        // shipping one Japanese family gets its kanji from that family without having to say so twice.
        // Conversely kana and hangul draw on a family claimed only for ideographs, which is normally a
        // full CJK family.
        if (han)
            emit(e => !e.Framework && FontScripts.IsCJK(e.Script));
        else if (cjk)
            emit(e => !e.Framework && e.Script == FontScript.Han);

        emit(e => !e.Framework && e.Script == FontScript.Any);

        emit(e => e.Framework && e.Script == script);

        if (han)
        {
            emit(e => e.Framework && e.Script == HanScript);
            emit(e => e.Framework && FontScripts.IsCJK(e.Script));
        }
        else if (cjk)
            emit(e => e.Framework && e.Script == FontScript.Han);

        emit(_ => true);

        return ordered;
    }

    /// <summary>
    /// Warns once when a family was claimed for a script it does not actually cover — a typo in a
    /// claim, or a font that only looked like it covered the script. The claim is left in place: a
    /// chain entry that has no glyph is skipped per codepoint anyway, so honouring it costs nothing,
    /// while dropping it would make the mistake harder to see rather than easier.
    /// </summary>
    private void verifyClaim(FallbackEntry entry, Font font)
    {
        // The framework's own families are verified by the resource build, and a missing file already
        // warns through the not-loaded path above.
        if (entry.Framework || font == null)
            return;

        uint probe = FontScripts.ProbeFor(entry.Script);

        if (probe == 0 || font.HasGlyph(probe))
            return;

        if (warnedUncoveredClaims.Add((entry.Family, entry.Script)))
            Logger.Warning($"[FontLoader] '{entry.Family}' is registered for FontScript.{entry.Script} but has no glyph for U+{probe:X4}, so the claim will contribute nothing. Did you mean a different script or a different family?");
    }

    /// <summary>
    /// Fallback sources, keyed by the only parts of a <see cref="FontUsage"/> that affect the result (the
    /// family is substituted per fallback, and size plays no part in resolution).
    /// </summary>
    private readonly Dictionary<(string weight, bool italics), FallbackSource> fallbackSources = new Dictionary<(string, bool), FallbackSource>();

    /// <summary>
    /// The per-script fallback chains for this usage, as <see cref="Font.ProcessText"/> needs them: it
    /// classifies each codepoint while segmenting and asks for the chain matching the script it found.
    /// </summary>
    public IFontFallbackSource GetFallbackSource(FontUsage usage)
    {
        resolvePendingAutoClaims();

        var key = (usage.Weight, usage.Italics);

        if (!fallbackSources.TryGetValue(key, out var source))
            fallbackSources[key] = source = new FallbackSource(this, usage);

        return source;
    }

    /// <summary>
    /// The fallback fonts to try, in order, for glyphs the primary font does not cover. Resolves for the
    /// script the usage names, or the script-agnostic chain when it names none.
    /// </summary>
    public IEnumerable<Font> GetFallbacks(FontUsage usage) => GetFallbacks(usage, usage.Script ?? FontScript.Any);

    /// <summary>
    /// The fallback fonts to try, in order, for a glyph belonging to <paramref name="script"/>.
    /// </summary>
    public IEnumerable<Font> GetFallbacks(FontUsage usage, FontScript script) => GetFallbackSource(usage).GetFallbacks(script);

    /// <summary>
    /// The fallback chains for one (weight, italics) combination, one per script, each built on first
    /// use. Holding them together means a layout that mixes scripts resolves each script once for the
    /// whole application run rather than once per label.
    /// </summary>
    private sealed class FallbackSource : IFontFallbackSource
    {
        private readonly RendererFontStore store;
        private readonly FontUsage usage;

        private readonly Dictionary<FontScript, FallbackChain> chains = new Dictionary<FontScript, FallbackChain>();

        public FallbackSource(RendererFontStore store, FontUsage usage)
        {
            this.store = store;
            this.usage = usage;
        }

        public IEnumerable<Font> GetFallbacks(FontScript script)
        {
            // Auto is a registration-time sentinel; if one reaches here it describes no script.
            if (script == FontScript.Auto)
                script = FontScript.Any;

            if (!chains.TryGetValue(script, out var chain))
                chains[script] = chain = new FallbackChain(store, usage, store.orderedEntriesFor(script));

            return chain;
        }
    }

    /// <summary>
    /// A fallback chain for one (weight, italics, script) combination, resolved one family at a time as
    /// a consumer walks it, and remembering what it resolved so later layouts pay nothing.
    /// </summary>
    private sealed class FallbackChain : IEnumerable<Font>
    {
        private readonly RendererFontStore store;
        private readonly FontUsage usage;

        /// <summary>
        /// The families to try, in priority order for this chain's script. Ordering is pure string work
        /// so it happens up front; loading the fonts they name is what stays lazy.
        /// </summary>
        private readonly List<FallbackEntry> entries;

        /// <summary>
        /// Fonts resolved so far, in chain order. Append-only.
        /// </summary>
        private readonly List<Font> resolved = new List<Font>();

        private readonly HashSet<Font> seen = new HashSet<Font>();

        /// <summary>
        /// How far into <see cref="entries"/> resolution has reached.
        /// </summary>
        private int nextFamily;

        public FallbackChain(RendererFontStore store, FontUsage usage, List<FallbackEntry> entries)
        {
            this.store = store;
            this.usage = usage;
            this.entries = entries;
        }

        public IEnumerator<Font> GetEnumerator()
        {
            for (int i = 0; ; i++)
            {
                if (i >= resolved.Count && !resolveNext())
                    yield break;

                yield return resolved[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Loads and appends the next family that resolves to a usable font, skipping ones that are
        /// registered but not loaded. Returns false once the chain is exhausted.
        /// </summary>
        private bool resolveNext()
        {
            while (nextFamily < entries.Count)
            {
                var entry = entries[nextFamily++];
                string family = entry.Family;

                // Resolved through the registered keys only. Get(FontUsage) would answer with the
                // default font for a family that was never loaded, which is indistinguishable from a
                // family that legitimately *is* the default font — and the base family is a fallback in
                // its own right (it is the only bundled one covering Cyrillic and Greek).
                var fallbackFont = store.getRegistered(usage, family);

                // A registered fallback family that resolves to nothing was never loaded (its file is
                // missing, or AddFallbackFamily was called without a matching AddFont/loadFamily). Such
                // a family contributes nothing to glyph coverage, warn once so the misconfiguration is
                // visible instead of silently rendering missing glyphs as .notdef ("tofu").
                if (fallbackFont == null)
                {
                    if (store.warnedMissingFallbacks.Add(family))
                        Logger.Debug($"Fallback family '{family}' is registered but not loaded, it will not contribute glyphs. Did you forget to load it?");

                    continue;
                }

                store.verifyClaim(entry, fallbackFont);

                if (!seen.Add(fallbackFont))
                    continue;

                resolved.Add(fallbackFont);
                return true;
            }

            return false;
        }
    }

    #region Shaped text cache

    /// <summary>
    /// The default number of shaped strings kept. A screen's worth of labels is tens of entries, this
    /// leaves room for a scrolling list to churn through several screens without evicting text that is
    /// still on display.
    /// </summary>
    public const int DEFAULT_SHAPE_CACHE_SIZE = 1024;

    /// <summary>
    /// Maximum number of shaped strings to keep
    /// </summary>
    public int ShapeCacheSize { get; set; } = DEFAULT_SHAPE_CACHE_SIZE;

    private static readonly GlobalStatistic<long> stat_shape_hits = GlobalStatistics.Get<long>("Fonts", "Shape Cache Hits");
    private static readonly GlobalStatistic<long> stat_shape_misses = GlobalStatistics.Get<long>("Fonts", "Shape Cache Misses");
    private static readonly GlobalStatistic<int> stat_shape_entries = GlobalStatistics.Get<int>("Fonts", "Shaped Text Entries");
    private static readonly GlobalStatistic<long> stat_shape_bytes = GlobalStatistics.Get<long>("Fonts", "Shaped Text Bytes");

    /// <summary>
    /// Everything about a request that changes the shaped output. <see cref="FontUsage"/> carries family,
    /// size, weight, italics and the variation axes; <c>dpiScale</c> is separate because it comes from the
    /// window rather than the usage.
    /// </summary>
    private readonly record struct ShapeKey(FontUsage Usage, string Text, float DpiScale);

    private readonly Dictionary<ShapeKey, LinkedListNode<(ShapeKey Key, ShapedText Shaped)>> shapeCache
        = new Dictionary<ShapeKey, LinkedListNode<(ShapeKey, ShapedText)>>();

    /// <summary>
    /// Most-recently-used at the front
    /// </summary>
    private readonly LinkedList<(ShapeKey Key, ShapedText Shaped)> shapeLru
        = new LinkedList<(ShapeKey, ShapedText)>();

    private long shapeBytes;

    public ShapedText Shape(FontUsage usage, string text, float dpiScale)
    {
        if (string.IsNullOrEmpty(text))
            return ShapedText.Empty;

        var key = new ShapeKey(usage, text, dpiScale);

        if (shapeCache.TryGetValue(key, out var existing))
        {
            // Move to the front by relinking the existing node: no allocation, which is what makes a hit
            // free.
            shapeLru.Remove(existing);
            shapeLru.AddFirst(existing);

            stat_shape_hits.Value++;
            return existing.Value.Shaped;
        }

        stat_shape_misses.Value++;

        var font = Get(usage);
        if (font.IsNull())
            return ShapedText.Empty;

        var shaped = font.ProcessText(text, usage.Size, dpiScale, GetFallbackSource(usage), GetVariation(usage), usage.Script);

        var node = shapeLru.AddFirst((key, shaped));
        shapeCache[key] = node;
        shapeBytes += shaped.EstimatedBytes;

        while (shapeCache.Count > Math.Max(1, ShapeCacheSize))
        {
            var evicted = shapeLru.Last!;
            shapeLru.RemoveLast();
            shapeCache.Remove(evicted.Value.Key);
            shapeBytes -= evicted.Value.Shaped.EstimatedBytes;
        }

        updateShapeStatistics();

        return shaped;
    }

    private void updateShapeStatistics()
    {
        stat_shape_entries.Value = shapeCache.Count;
        stat_shape_bytes.Value = shapeBytes;
    }

    /// <summary>
    /// Drops every shaped result.
    /// </summary>
    private void invalidateShapeCache()
    {
        shapeCache.Clear();
        shapeLru.Clear();
        shapeBytes = 0;

        updateShapeStatistics();
    }

    #endregion

    /// <summary>
    /// Drops resolved fallback chains and shaped results, so a newly registered family or font is picked
    /// up. Both are derived from which fonts resolve and in what order, so they go stale together.
    /// </summary>
    private void invalidateFallbackCache()
    {
        fallbackSources.Clear();
        invalidateShapeCache();
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

        invalidateFallbackCache();

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
