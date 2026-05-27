// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

public class TextEditingEvent
{
    public string Text { get; }
    public int Start { get; }
    public int Length { get; }

    public TextEditingEvent(string text, int start, int length)
    {
        Text = text;
        Start = start;
        Length = length;
    }
}
