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
    private readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
    private readonly List<string> fallbackFamilies = new List<string>();

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
            defaultFont = reg;
            fontCache["Default"] = reg;
            fontCache["NotoSans"] = reg; // Allow lookup by just family name
        }
        else
        {
            Logger.Warning("FontLoader : NotoSans-Regular.ttf was not found. Default font is missing.");
        }

        // fallback families for various languages
        string[] fallbackFamilies = new[]
        {
            "NotoSansThai",
            "NotoSansJP",
            "NotoSansKR",
            "NotoSansSC",
            "NotoSansTC",
            "NotoSansArabic",
            "NotoSansDevanagari",
            "NotoSansHebrew"
        };

        foreach (string family in fallbackFamilies)
        {
            // These families don't have italics
            loadFamily(resourceStorage, family, hasItalics: false);
            AddFallbackFamily(family);
        }

        // emoji support as a final fallback (color emoji font, so no italics or weights)
        AddFont(resourceStorage, "NotoColorEmoji-Regular.ttf", "NotoColorEmoji-Regular");
        AddFallbackFamily("NotoColorEmoji");
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
        try
        {
            using var stream = storage.GetStream(filename);
            if (stream == null)
            {
                Logger.Error($"Could not find font file: {filename}");
                return;
            }

            string name = alias ?? Path.GetFileNameWithoutExtension(filename);
            var font = loadFontFromStream(name, stream);

            fontCache[name] = font;
            GlobalStatistics.Get<int>("Fonts", "Loaded Fonts").Value = fontCache.Count;
            Logger.Verbose($"Loaded font {name} from {filename}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load font {filename}: {ex.Message}");
        }
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

        string familyKey = usage.Family;

        if (fontCache.TryGetValue(specificKey, out var font))
            return font;

        if (usage.Italics)
        {
            string nonItalicKey = $"{usage.Family}-{usage.Weight}";
            if (fontCache.TryGetValue(nonItalicKey, out font))
                return font;
        }

        if (fontCache.TryGetValue(usage.Family, out font))
            return font;

        return defaultFont;
    }

    public Font Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return defaultFont;
        if (fontCache.TryGetValue(name, out var font)) return font;
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
        var fallbacks = new List<Font>();

        foreach (var family in fallbackFamilies)
        {
            var fallbackUsage = usage.With(family: family);
            var fallbackFont = Get(fallbackUsage);

            // If the font exists and isn't just returning the default NotoSans-Regular fallback
            if (fallbackFont != null && fallbackFont != defaultFont && !fallbacks.Contains(fallbackFont))
            {
                fallbacks.Add(fallbackFont);
            }
        }

        return fallbacks;
    }

    public void Dispose()
    {
        foreach (var font in fontCache.Values)
            font.Dispose();

        atlas.Dispose();
    }
}
