// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// One finger on a touchscreen. A finger going down takes the lowest number not in use, so the first finger of
/// a gesture is always <see cref="Touch1"/>.
/// </summary>
public enum TouchSource
{
    Touch1,
    Touch2,
    Touch3,
    Touch4,
    Touch5,
    Touch6,
    Touch7,
    Touch8,
    Touch9,
    Touch10
}
