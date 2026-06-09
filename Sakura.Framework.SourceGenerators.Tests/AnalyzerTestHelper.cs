// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace Sakura.Framework.SourceGenerators.Tests;

/// <summary>
/// Shared utilities for running Roslyn analyzers and code fixes against in-memory C# source
/// and asserting on the produced diagnostics / fixed code.
/// </summary>
internal static class AnalyzerTestHelper
{
    /// <summary>
    /// Compiles <paramref name="sources"/> and runs <typeparamref name="TAnalyzer"/> against them.
    /// Returns all reported diagnostics from that analyzer.
    /// </summary>
    public static ImmutableArray<Diagnostic> GetDiagnostics<TAnalyzer>(
        params string[] sources) where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(sources);
        var analyzer = new TAnalyzer();

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer),
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty));

        // GetAnalyzerDiagnosticsAsync returns only analyzer-produced diagnostics (not compiler errors).
        return compilationWithAnalyzers
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asserts that running <typeparamref name="TAnalyzer"/> on <paramref name="sources"/>
    /// produces no diagnostics.
    /// </summary>
    public static void AssertNoDiagnostics<TAnalyzer>(params string[] sources)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var diagnostics = GetDiagnostics<TAnalyzer>(sources);
        if (diagnostics.Length > 0)
        {
            string messages = string.Join("\n", diagnostics.Select(d => d.ToString()));
            throw new AssertionException($"Expected no diagnostics but got:\n{messages}");
        }
    }

    /// <summary>
    /// Asserts exactly one diagnostic with <paramref name="expectedId"/> is reported,
    /// and returns it.
    /// </summary>
    public static Diagnostic AssertSingleDiagnostic<TAnalyzer>(
        string expectedId,
        params string[] sources) where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var diagnostics = GetDiagnostics<TAnalyzer>(sources)
            .Where(d => d.Id == expectedId)
            .ToList();

        if (diagnostics.Count == 0)
            throw new AssertionException($"Expected diagnostic '{expectedId}' but none was reported.");

        if (diagnostics.Count > 1)
            throw new AssertionException(
                $"Expected exactly one '{expectedId}' but got {diagnostics.Count}:\n" +
                string.Join("\n", diagnostics.Select(d => d.ToString())));

        return diagnostics[0];
    }

    /// <summary>
    /// Asserts that at least one diagnostic with <paramref name="expectedId"/> is reported.
    /// </summary>
    public static IReadOnlyList<Diagnostic> AssertDiagnostic<TAnalyzer>(
        string expectedId,
        params string[] sources) where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var diagnostics = GetDiagnostics<TAnalyzer>(sources)
            .Where(d => d.Id == expectedId)
            .ToList();

        if (diagnostics.Count == 0)
            throw new AssertionException($"Expected at least one '{expectedId}' diagnostic but none was reported.");

        return diagnostics;
    }

    /// <summary>
    /// Applies <typeparamref name="TCodeFix"/> for diagnostic <paramref name="diagnosticId"/>
    /// to the first source in <paramref name="sources"/> and returns the fixed source text.
    /// </summary>
    public static async Task<string> ApplyCodeFixAsync<TAnalyzer, TCodeFix>(
        string diagnosticId,
        params string[] sources)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        // Build workspace + project + document
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            name: "TestProject",
            assemblyName: "TestAssembly",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: getMetadataReferences());

        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        // Add all source files; the first one is the target for fixes.
        for (int i = 0; i < sources.Length; i++)
        {
            var docId = DocumentId.CreateNewId(projectId, debugName: $"Source{i}.cs");
            solution = solution.AddDocument(docId, $"Source{i}.cs", SourceText.From(sources[i]));
        }

        workspace.TryApplyChanges(solution);

        // Get diagnostics on the current solution
        var project = workspace.CurrentSolution.GetProject(projectId)!;
        var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
        var analyzer = new TAnalyzer();
        var withAnalyzers = compilation!.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var allDiagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
        var targetDiagnostics = allDiagnostics.Where(d => d.Id == diagnosticId).ToList();

        if (targetDiagnostics.Count == 0)
            throw new InvalidOperationException($"No '{diagnosticId}' diagnostic found to apply code fix to.");

        // Apply the first code fix
        var codeFix = new TCodeFix();
        Document? fixedDocument = null;

        foreach (var diagnostic in targetDiagnostics)
        {
            // Find the document that contains this diagnostic
            var diagLocation = diagnostic.Location;
            var sourceTree = diagLocation.SourceTree;
            if (sourceTree == null) continue;

            var document = workspace.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == sourceTree.FilePath ||
                                     d.Name == sourceTree.FilePath);

            // Fall back: find by syntax tree identity
            if (document == null)
            {
                foreach (var proj in workspace.CurrentSolution.Projects)
                {
                    foreach (var doc in proj.Documents)
                    {
                        var tree = await doc.GetSyntaxTreeAsync().ConfigureAwait(false);
                        if (tree == sourceTree)
                        {
                            document = doc;
                            break;
                        }
                    }
                    if (document != null) break;
                }
            }

            if (document == null) continue;

            var actions = new List<Microsoft.CodeAnalysis.CodeActions.CodeAction>();
            var fixContext = new CodeFixContext(
                document,
                diagnostic,
                (action, _) => actions.Add(action),
                CancellationToken.None);

            await codeFix.RegisterCodeFixesAsync(fixContext).ConfigureAwait(false);

            if (actions.Count > 0)
            {
                var operations = await actions[0]
                    .GetOperationsAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                foreach (var op in operations)
                    op.Apply(workspace, CancellationToken.None);

                // Re-fetch the document after the workspace was mutated
                fixedDocument = workspace.CurrentSolution.GetDocument(document.Id);
                break;
            }
        }

        if (fixedDocument == null)
            throw new InvalidOperationException("Code fix did not produce a changed document.");

        var sourceText = await fixedDocument.GetTextAsync().ConfigureAwait(false);
        return sourceText.ToString();
    }

    private static ImmutableArray<MetadataReference> getMetadataReferences()
    {
        var refs = new List<MetadataReference>();
        string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
        {
            if (File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }
        return refs.ToImmutableArray();
    }
}
