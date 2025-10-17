// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Configurations;

/// <summary>
/// A <see cref="ConfigManager{TLookup}"/> that does not persist settings to disk.
/// </summary>
/// <typeparam name="TLookup">The enum type used to look up settings.</typeparam>
public class InMemoryConfigManager<TLookup> : ConfigManager<TLookup> where TLookup : struct, Enum
{
    public InMemoryConfigManager() : base(null)
    {
    }

    public override void Load()
    {
        // Don't load from a file
    }

    public override void Save()
    {
        // Don't write to a file
    }
}
