// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Sakura.Framework.Statistic;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Text;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GLFontStore : IFontStore
{
    private readonly TextureAtlas atlas;
    private readonly Dictionary<string, Lazy<Font>> fontCache = new Dictionary<string, Lazy<Font>>();
    private readonly List<string> fallbackFamilies = new List<string>();
    public int CacheVersion { get; private set; }

    private Font defaultFont;

    public GLFontStore(GL gl)
    {
        atlas = new TextureAtlas(gl, 1024, 1024);
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
            Logger.Warning("FontLoader : NotoSans-Regular.ttf was not found. Default font is missing.");
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

        // Use NotoEmoji for fallback of emoji and other symbols
        // Since to use NotoColorEmoji need to change the freetype to compile with libpng so just use monochrome NotoEmoji for now
        // which is still better than missing glyphs.
        // TODO: Add support for color emoji in the future
        AddFallbackFamily("NotoEmoji");

        // Material Symbols for IconSprite
        // TODO: The material symbols font support variable font with different weights and italics, should add support in future.
        AddFont(resourceStorage, "MaterialSymbolsOutlined-Regular.ttf", alias: "MaterialSymbolsOutlined-Regular");
        AddFont(resourceStorage, "MaterialSymbolsRounded-Regular.ttf", alias: "MaterialSymbolsRounded-Regular");
        AddFont(resourceStorage, "MaterialSymbolsSharp-Regular.ttf", alias: "MaterialSymbolsSharp-Regular");
        AddFallbackFamily("MaterialSymbolsOutlined");
    }

    private void loadFamily(Storage storage, string family, bool hasItalics)
    {
        string[] weights = Enum.GetNames(typeof(DefaultFontWeights));

        foreach (string weight in weights)
        {
            string normalFileName = $"{family}-{weight}.ttf";

            // AddFont already has a try-catch and checks if the stream is null,
            // so it will safely skip weights that don't exist in the storage.
            AddFont(storage, normalFileName, alias: $"{family}-{weight}");

            if (hasItalics)
            {
                string italicFileName = weight == "Regular" ? $"{family}-Italic.ttf" : $"{family}-{weight}Italic.ttf";
                AddFont(storage, italicFileName, alias: $"{family}-{weight}Italic");
            }
        }
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
            var fallbackUsage = usage.With(family: family);
            var fallbackFont = Get(fallbackUsage);

            if (fallbackFont != null && fallbackFont != defaultFont && !returnedFonts.Contains(fallbackFont))
            {
                returnedFonts.Add(fallbackFont);
                yield return fallbackFont;
            }
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
