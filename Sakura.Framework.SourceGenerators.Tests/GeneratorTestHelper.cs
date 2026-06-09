// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Sakura.Framework.SourceGenerators.Tests;

/// <summary>
/// Shared utilities for running Roslyn source generators against in-memory C# source
/// and asserting on the produced output.
/// </summary>
internal static class GeneratorTestHelper
{
    /// <summary>
    /// Compiles <paramref name="sources"/> into a <see cref="CSharpCompilation"/>,
    /// runs <typeparamref name="TGenerator"/> against it, and returns the driver result.
    /// </summary>
    public static GeneratorDriverRunResult RunGenerator<TGenerator>(
        params string[] sources) where TGenerator : IIncrementalGenerator, new()
    {
        var compilation = CreateCompilation(sources);
        var generator = new TGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        return driver.GetRunResult();
    }

    /// <summary>
    /// Creates a <see cref="CSharpCompilation"/> from the given source strings,
    /// referencing the same assemblies the test process itself uses.
    /// </summary>
    public static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var syntaxTrees = sources.Select(s =>
            CSharpSyntaxTree.ParseText(s, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)));

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: getMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Returns the generated source for a file whose hint name ends with <paramref name="hintNameSuffix"/>.
    /// Throws <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    public static string GetGeneratedSource(GeneratorDriverRunResult result, string hintNameSuffix)
    {
        var match = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.EndsWith(hintNameSuffix, StringComparison.OrdinalIgnoreCase));

        if (match.SourceText == null)
        {
            var available = result.Results
                .SelectMany(r => r.GeneratedSources)
                .Select(s => s.HintName);
            throw new InvalidOperationException($"No generated file ending with '{hintNameSuffix}' found. Available: {string.Join(", ", available)}");
        }

        return match.SourceText.ToString();
    }

    /// <summary>
    /// Returns all generated source files as a dictionary of hintName → source text.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetAllGeneratedSources(GeneratorDriverRunResult result)
        => result.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString());

    /// <summary>
    /// Asserts that the generator produced no diagnostics (errors or warnings).
    /// </summary>
    public static void AssertNoDiagnostics(GeneratorDriverRunResult result)
    {
        var diagnostics = result.Results
            .SelectMany(r => r.Diagnostics)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToList();

        if (diagnostics.Count > 0)
        {
            string messages = string.Join("\n", diagnostics.Select(d => d.ToString()));
            throw new AssertionException($"Generator produced unexpected diagnostics:\n{messages}");
        }
    }

    /// <summary>
    /// Asserts that the compiled output (original + generated) has no compilation errors.
    /// </summary>
    public static void AssertNoCompilationErrors(CSharpCompilation original, GeneratorDriverRunResult result)
    {
        // Apply generated sources back onto the compilation and check for errors.
        var updatedCompilation = original;
        foreach (var genResult in result.Results)
        {
            foreach (var generated in genResult.GeneratedSources)
                updatedCompilation = updatedCompilation.AddSyntaxTrees(generated.SyntaxTree);
        }

        var errors = updatedCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            string messages = string.Join("\n", errors.Select(d => d.ToString()));
            throw new AssertionException($"Compilation errors after generation:\n{messages}");
        }
    }

    private static ImmutableArray<MetadataReference> getMetadataReferences()
    {
        // Include the runtime assemblies the test process is using, plus the
        // framework and netstandard references needed for our attribute stubs.
        var refs = new List<MetadataReference>();

        // Core .NET runtime
        string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
        {
            if (File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        return refs.ToImmutableArray();
    }
}
