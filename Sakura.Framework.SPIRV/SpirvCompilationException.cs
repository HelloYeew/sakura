// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.SPIRV
{
    /// <summary>
    /// Represents errors that occur in the Sakura.Framework.SPIRV library.
    /// </summary>
    public class SpirvCompilationException : Exception
    {
        /// <summary>
        /// Constructs a new <see cref="SpirvCompilationException"/>.
        /// </summary>
        public SpirvCompilationException()
        {
        }

        /// <summary>
        /// Constructs a new <see cref="SpirvCompilationException"/> with the given message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public SpirvCompilationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructs a new <see cref="SpirvCompilationException"/> with the given message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public SpirvCompilationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
