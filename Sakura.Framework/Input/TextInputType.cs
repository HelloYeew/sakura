// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Input;

/// <summary>
/// The kind of text a field takes. A touchscreen keyboard uses it to pick its layout, its capitalisation, and
/// whether it suggests and remembers words. Physical keyboards and desktop IMEs ignore it.
/// </summary>
public enum TextInputType
{
    /// <summary>
    /// Ordinary text: suggestions on, and sentences start with a capital.
    /// </summary>
    Text,

    /// <summary>
    /// A person's name: each word starts with a capital.
    /// </summary>
    Name,

    /// <summary>
    /// An e-mail address: no capitals, with @ and . on the keyboard.
    /// </summary>
    Email,

    /// <summary>
    /// A user name: no capitals and no suggestions.
    /// </summary>
    Username,

    /// <summary>
    /// A password: no suggestions, and the keyboard doesn't remember what was typed. Hiding the text on screen
    /// is up to the field.
    /// </summary>
    Password,

    /// <summary>
    /// A number: the keyboard shows digits.
    /// </summary>
    Number,

    /// <summary>
    /// A numeric PIN: digits, and the keyboard doesn't remember them.
    /// </summary>
    NumberPassword
}
