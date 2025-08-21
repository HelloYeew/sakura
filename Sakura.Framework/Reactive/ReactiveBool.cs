// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Reactive;

public class ReactiveBool : Reactive<bool>
{
    public ReactiveBool(bool defaultValue = false) : base(defaultValue)
    {
    }

    public override void Parse(object input, IFormatProvider? formatProvider = null)
    {
        switch (input)
        {
            case "1":
                Value = true;
                break;
            case "0":
                Value = false;
                break;
            default:
                base.Parse(input, formatProvider);
                break;
        }
    }

    public void Toggle()
    {
        Value = !Value;
    }
}
