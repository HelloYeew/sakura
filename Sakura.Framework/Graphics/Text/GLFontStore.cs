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

    private Font defaultFont;

    public GLFontStore(GL gl)
    {
        atlas = new TextureAtlas(gl, 1024, 1024);
    }

    private void loadFrameworkFonts(Storage resourceStorage)
    {
        string[] weights = Enum.GetNames(typeof(DefaultFontWeights));

        string family = "NotoSans";

        foreach (string weight in weights)
        {
            string normalFileName = $"{family}-{weight}.ttf";
            AddFont(resourceStorage, normalFileName, alias: $"{family}-{weight}");

            string italicFileName;
            string italicKey = $"{family}-{weight}Italic";

            italicFileName = weight == "Regular" ? $"{family}-Italic.ttf" : $"{family}-{weight}Italic.ttf";

            AddFont(resourceStorage, italicFileName, alias: italicKey);
        }

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

    public void Dispose()
    {
        foreach (var font in fontCache.Values)
            font.Dispose();

        atlas.Dispose();
    }
}
