// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using BenchmarkDotNet.Running;

namespace Sakura.Framework.Benchmarks;

internal class Program
{
    private static void Main(string[] args)
    {
        // No args: interactive picker
        // Command list:
        // dotnet run -c Release -- --filter "*UpdateLoop*"
        // dotnet run -c Release -- --filter "*" --job short
        // dotnet run -c Release -- --list flat
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
