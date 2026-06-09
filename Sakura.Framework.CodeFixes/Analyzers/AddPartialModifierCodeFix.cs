// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sakura.Framework.CodeFixes.Analyzers;

/// <summary>
/// Code fix that adds the <c>partial</c> modifier to a class declaration reported by
/// SFDI0001 (DI class missing partial) or SFDI0002 (enclosing class missing partial).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddPartialModifierCodeFix))]
[Shared]
public sealed class AddPartialModifierCodeFix : CodeFixProvider
{
    // Hardcoded to avoid a project reference back to SourceGenerators (it create a circular dependency).
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("SFDI0001", "SFDI0002");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            // The diagnostic location points to the class identifier token.
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var classDecl = node.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl == null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Add 'partial' modifier to '{classDecl.Identifier.Text}'",
                    createChangedDocument: ct => addPartialAsync(context.Document, classDecl, ct),
                    equivalenceKey: $"AddPartial_{classDecl.Identifier.Text}"),
                diagnostic);
        }
    }

    private static async Task<Document> addPartialAsync(
        Document document,
        ClassDeclarationSyntax classDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        // Build the new modifier list with 'partial' inserted at the end (before the class keyword).
        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newModifiers = classDecl.Modifiers.Add(partialToken);
        var newClassDecl = classDecl.WithModifiers(newModifiers);

        var newRoot = root.ReplaceNode(classDecl, newClassDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
