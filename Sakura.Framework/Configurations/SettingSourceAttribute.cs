// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Configurations;

/// <summary>
/// An attribute to specify the backing file for a <see cref="ConfigManager{TLookup}"/>
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public class SettingSourceAttribute : Attribute
{
    /// <summary>
    /// The name of the file that stores the settings.
    /// </summary>
    public string FileName { get; }

    public SettingSourceAttribute(string fileName)
    {
        FileName = fileName;
    }
}
