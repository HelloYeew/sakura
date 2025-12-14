// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
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

    public void LoadDefaultFont(Storage resourceStorage)
    {
        try
        {
            if (resourceStorage.Exists("NotoSans-Regular.ttf"))
            {
                using var stream = resourceStorage.GetStream("NotoSans-Regular.ttf");
                defaultFont = loadFontFromStream("NotoSans-Regular", stream);
                fontCache["NotoSans"] = defaultFont;
                fontCache["NotoSans-Regular"] = defaultFont;
                fontCache["Default"] = defaultFont;
            }
            else
            {
                Logger.Warning("Default font 'NotoSans-Regular.ttf' not found in resources. Text rendering may fail.");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to load default font: {e.Message}");
        }
    }

    public void AddFont(Storage storage, string filename, string alias = null)
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
            Logger.Verbose($"Loaded font: {name}");
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
        // 1. Construct lookup keys
        string specificKey = $"{usage.Family}-{usage.Weight}";
        string familyKey = usage.Family;

        if (fontCache.TryGetValue(specificKey, out var font))
            return font;

        if (fontCache.TryGetValue(familyKey, out font))
            return font;

        // 2. Fallback to default
        if (defaultFont != null)
            return defaultFont;

        // 3. Absolute fallback
        foreach (var f in fontCache.Values)
            return f;

        // If we reach here, we are likely crashing soon if used, but let's return null to handle gracefully upstream
        return null;
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
