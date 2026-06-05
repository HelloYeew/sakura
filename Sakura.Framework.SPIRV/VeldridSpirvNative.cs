// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;

namespace Sakura.Framework.SPIRV
{
    internal static unsafe class SakuraSpirvNative
    {
        private const string LibName = "libsakura-spirv";

        public static void SetupLibraryResolvers()
        {
            if (OperatingSystem.IsIOS())
            {
                NativeLibrary.SetDllImportResolver(
                    typeof(SakuraSpirvNative).Assembly,
                    (_, assembly, path) =>
                        NativeLibrary.Load("@rpath/sakura-spirv.framework/veldrid-spirv", assembly, path));
            }
        }

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern CompilationResult* CrossCompile(CrossCompileInfo* info);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern CompilationResult* CompileGlslToSpirv(GlslCompileInfo* info);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FreeResult(CompilationResult* result);
    }
}
