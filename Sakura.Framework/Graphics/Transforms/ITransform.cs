// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Transforms;

public interface ITransform
{
    double StartTime { get; }
    double EndTime { get;  }
    bool IsLooping { get; set; }
    void Apply(Drawable drawable, double time);
}
