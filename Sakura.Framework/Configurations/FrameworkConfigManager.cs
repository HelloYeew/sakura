// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Platform;

namespace Sakura.Framework.Configurations;

public class FrameworkConfigManager : ConfigManager<FrameworkSetting>
{
    public FrameworkConfigManager(Storage storage) : base(storage)
    {
        initializeDefaults();
    }

    private void initializeDefaults()
    {
        Get(FrameworkSetting.FrameLimiter, FrameSync.Limit2x);
    }
}
