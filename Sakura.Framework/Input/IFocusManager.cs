// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Input;

public interface IFocusManager
{
    /// <summary>
    /// Changes the currently focused drawable.
    /// </summary>
    bool ChangeFocus(Drawable? potentialFocusTarget);

    /// <summary>
    /// Evaluates focus state, usually triggered when a drawable requests focus.
    /// </summary>
    void TriggerFocusContention(Drawable? triggerSource);
}
