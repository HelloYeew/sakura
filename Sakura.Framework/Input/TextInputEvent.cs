// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

public class TextInputEvent
{
    public string Text { get; }
    public TextInputEvent(string text) => Text = text;
}
