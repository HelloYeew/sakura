// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text;

namespace Sakura.Framework.SPIRV
{
    internal static class Util
    {
        internal static unsafe string GetString(byte* data, uint length)
        {
            if (data == null) { return null; }

            return Encoding.UTF8.GetString(data, (int)length);
        }

        internal static bool HasSpirvHeader(byte[] bytes)
        {
            return bytes.Length > 4
                && bytes[0] == 0x03
                && bytes[1] == 0x02
                && bytes[2] == 0x23
                && bytes[3] == 0x07;
        }
    }
}
