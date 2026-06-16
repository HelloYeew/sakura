// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Transforms;

/// <summary>
/// Represents a chainable sequence of transforms applied to a <typeparamref name="T"/>.
/// </summary>
public class TransformSequence<T> where T : Drawable
{
    private readonly T target;

    /// <summary>
    /// The time at which this sequence starts (clock time when the sequence was created).
    /// </summary>
    private readonly double sequenceStartTime;

    /// <summary>
    /// The accumulated delay offset from the sequence start, in ms.
    /// Each transform added advances this by its duration when <see cref="Then"/> is called.
    /// </summary>
    private double currentOffset;

    /// <summary>
    /// Snapshot of <c>target.TimeUntilTransformsCanStart</c> before we applied our first transform,
    /// so we can restore it when the sequence ends without clobbering a pre-existing delay.
    /// </summary>
    private readonly double savedDelay;

    /// <summary>
    /// End time of the last transform added to this sequence.
    /// Used to wire up Loop and callbacks.
    /// </summary>
    internal double LatestEndTime { get; private set; }

    /// <summary>
    /// All transforms added during this sequence, in insertion order.
    /// </summary>
    private readonly List<Transform> sequenceTransforms = new List<Transform>();

    private Action<T>? onCompleteCallback;
    private Action<T>? onAbortCallback;

    internal TransformSequence(T target)
    {
        this.target = target;
        sequenceStartTime = target.Clock.CurrentTime;
        savedDelay = target.TimeUntilTransformsCanStart;
        LatestEndTime = sequenceStartTime + savedDelay;
        currentOffset = savedDelay;
    }

    /// <summary>
    /// Runs <paramref name="action"/> against the target, capturing any transforms it adds into this sequence.
    /// The action runs with the sequence's current time offset applied.
    /// </summary>
    internal TransformSequence<T> Append(Action<T> action)
    {
        target.TimeUntilTransformsCanStart = currentOffset;
        int countBefore = target.TransformCount;

        action(target);

        // Capture newly added transforms.
        target.CollectTransformsSince(countBefore, sequenceTransforms);

        // Update our latest end time.
        double latest = target.GetLatestTransformEndTime();
        if (latest > LatestEndTime) LatestEndTime = latest;

        // Reset target delay so subsequent independent chains don't carry our offset.
        target.TimeUntilTransformsCanStart = 0;
        return this;
    }

    /// <summary>
    /// Sets the sequence cursor to immediately after the latest transform in this sequence.
    /// Subsequent calls will start from there.
    /// </summary>
    public TransformSequence<T> Then(double extraDelay = 0)
    {
        currentOffset = LatestEndTime - sequenceStartTime + extraDelay;
        return this;
    }

    /// <summary>
    /// Adds an additional delay to the sequence cursor.
    /// </summary>
    public TransformSequence<T> Delay(double duration)
    {
        currentOffset += duration;
        return this;
    }

    /// <summary>
    /// Loops all transforms in this sequence infinitely.
    /// </summary>
    public TransformSequence<T> Loop(double pause = 0)
    {
        applyLoop(0, pause);
        return this;
    }

    /// <summary>
    /// Loops all transforms in this sequence a finite number of times.
    /// </summary>
    public TransformSequence<T> Loop(int count, double pause = 0)
    {
        applyLoop(count, pause);
        return this;
    }

    private void applyLoop(int count, double pause)
    {
        foreach (var t in sequenceTransforms)
        {
            t.IsLooping = true;
            t.LoopCount = count;
            t.LoopPause = pause;
        }
    }

    /// <summary>
    /// Registers a callback to be invoked when all transforms in this sequence have completed.
    /// </summary>
    public TransformSequence<T> OnComplete(Action<T> action)
    {
        onCompleteCallback = action;
        wireCallbacks();
        return this;
    }

    /// <summary>
    /// Registers a callback to be invoked when the sequence is aborted (transforms cleared before completion).
    /// </summary>
    public TransformSequence<T> OnAbort(Action<T> action)
    {
        onAbortCallback = action;
        wireCallbacks();
        return this;
    }

    /// <summary>
    /// Registers a callback to be invoked when the sequence either completes or is aborted.
    /// </summary>
    public TransformSequence<T> Finally(Action<T> action)
    {
        onCompleteCallback = action;
        onAbortCallback = action;
        wireCallbacks();
        return this;
    }

    private void wireCallbacks()
    {
        if (onCompleteCallback == null && onAbortCallback == null) return;

        var cb = new CallbackTransform<T>
        {
            StartTime = LatestEndTime,
            EndTime = LatestEndTime,
            Target = target,
            OnComplete = onCompleteCallback,
            OnAbort = onAbortCallback
        };
        target.AddTransform(cb);
    }

    /// <summary>
    /// Returns the underlying target drawable, ending the sequence fluent chain.
    /// </summary>
    public T AsDrawable() => target;
}
