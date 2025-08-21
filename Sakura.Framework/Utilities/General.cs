// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Utilities;

public static class General
{
    /// <summary>
    /// Attempts to execute the given action multiple times until it succeeds or the specified number of attempts is reached.
    /// Useful for retrying operations that may fail due to transient issues, such as network requests or file operations.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="attempts">Number of attempts to execute the action before giving up.</param>
    /// <param name="throwOnFailure">Whether to throw an exception if the action fails after all attempts.</param>
    /// <typeparam name="TException">The type of exception to catch and retry on. If the action throws an exception of this type, it will be caught and retried.</typeparam>
    /// <returns>Whether the action succeeded within the specified number of attempts.</returns>
    public static bool AttemptWithRetryOnException<TException>(this Action action, int attempts = 10, bool throwOnFailure = true)
        where TException : Exception
    {
        while (true)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (e is not TException)
                    throw;

                if (attempts-- == 0)
                {
                    if (throwOnFailure)
                        throw;

                    return false;
                }

                Logger.Verbose($"Operation failed ({e.Message}). Retrying {attempts} more times...");
            }

            Thread.Sleep(250);
        }
    }
}
