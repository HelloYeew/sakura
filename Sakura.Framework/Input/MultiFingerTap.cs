// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Input;

/// <summary>
/// Spots several fingers tapping together: they all go down and come back up within
/// <see cref="MAX_MILLISECONDS"/>, none of them moves past <see cref="TouchMouseEmulation.SLOP"/>, and no drawable
/// takes any of them, so a game that reads fingers itself never sets one off.
/// </summary>
internal sealed class MultiFingerTap
{
    /// <summary>
    /// The longest a tap can take, from its first finger landing to its last lifting.
    /// </summary>
    public const double MAX_MILLISECONDS = 400;

    private readonly Vector2[] starts = new Vector2[InputState.MAX_TOUCHES];

    // The fingers of the gesture still down, as bits.
    private int down;

    private int count;
    private Vector2 sum;
    private bool spoiled;
    private double startTime;

    /// <param name="e">The finger.</param>
    /// <param name="taken">Whether a drawable took it.</param>
    /// <param name="now">The current time, in milliseconds.</param>
    public void TouchDown(TouchEvent e, bool taken, double now)
    {
        if (down == 0)
        {
            count = 0;
            sum = Vector2.Zero;
            spoiled = false;
            startTime = now;
        }

        if (taken || now - startTime > MAX_MILLISECONDS)
            spoiled = true;

        int i = (int)e.Source;
        down |= 1 << i;
        starts[i] = e.ScreenSpacePosition;
        sum += e.ScreenSpacePosition;
        count++;
    }

    public void TouchMove(TouchEvent e)
    {
        int i = (int)e.Source;

        if ((down & (1 << i)) != 0 && (e.ScreenSpacePosition - starts[i]).LengthSquared() > TouchMouseEmulation.SLOP * TouchMouseEmulation.SLOP)
            spoiled = true;
    }

    /// <param name="e">The finger.</param>
    /// <param name="now">The current time, in milliseconds.</param>
    /// <param name="centre">Where the fingers landed on average, for a completed tap.</param>
    /// <returns>How many fingers tapped, once the last of them lifts; otherwise zero.</returns>
    public int TouchUp(TouchEvent e, double now, out Vector2 centre)
    {
        centre = Vector2.Zero;

        int i = (int)e.Source;

        if ((down & (1 << i)) == 0)
            return 0;

        down &= ~(1 << i);

        if (e.Cancelled || now - startTime > MAX_MILLISECONDS)
            spoiled = true;

        if (down != 0 || spoiled)
            return 0;

        centre = sum / count;
        return count;
    }
}
