// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Runtime.ExceptionServices;

namespace Sakura.Framework.Extensions.ExceptionExtensions;

public static class ExceptionExtensions
{
    /// <summary>
    /// Rethrows the given <paramref name="exception"/> as if it was captured in the current context.
    /// This preserves the stack trace of <paramref name="exception"/>, and will not include the point of rethrow.
    /// </summary>
    /// <param name="exception">The captured exception.</param>
    public static void Rethrow(this Exception exception) => ExceptionDispatchInfo.Capture(exception).Throw();

    /// <summary>
    /// Flattens <paramref name="aggregateException"/> into a singular <see cref="Exception"/> if the <paramref name="aggregateException"/>
    /// contains only a single <see cref="Exception"/>. Otherwise, returns <paramref name="aggregateException"/>.
    /// </summary>
    /// <param name="aggregateException">The captured exception.</param>
    /// <returns>The highest level of flattening possible.</returns>
    public static Exception AsSingular(this AggregateException aggregateException)
    {
        if (aggregateException.InnerExceptions.Count != 1)
            return aggregateException;

        while (aggregateException.InnerExceptions.Count == 1)
        {
            if (aggregateException.InnerException is not AggregateException innerAggregate)
                return aggregateException.InnerException;

            aggregateException = innerAggregate;
        }

        return aggregateException;
    }
}
