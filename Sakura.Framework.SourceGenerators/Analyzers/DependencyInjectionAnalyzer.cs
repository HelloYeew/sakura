// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable enable

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Sakura.Framework.SourceGenerators.Analyzers;

/// <summary>
/// Roslyn analyzer that reports misuse of Sakura DI attributes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyInjectionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MISSING_PARTIAL_ON_DI_CLASS = new DiagnosticDescriptor(
        id: "SFDI0001",
        title: "DI class must be partial",
        messageFormat: "'{0}' uses Sakura DI attributes but is not declared partial",
        category: "Sakura.DI",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Classes that use [Resolved], [Cached], or [BackgroundDependencyLoader] must be partial so the Sakura source generator can produce the RegisterForDependencyActivation method. Without partial, the reflection fallback is used, which is slower.",
        helpLinkUri: "https://github.com/HelloYeew/sakura/wiki");

    public static readonly DiagnosticDescriptor MISSING_PARTIAL_ON_ENCLOSING_CLASS = new DiagnosticDescriptor(
        id: "SFDI0002",
        title: "Enclosing class of a DI class must be partial",
        messageFormat: "Enclosing class '{0}' must be partial because it contains a Sakura DI class",
        category: "Sakura.DI",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When a nested class uses Sakura DI, all its enclosing classes must also be partial for the source generator to work.",
        helpLinkUri: "https://github.com/HelloYeew/sakura/wiki");

    public static readonly DiagnosticDescriptor RESOLVED_PROPERTY_HAS_NO_SETTER = new DiagnosticDescriptor(
        id: "SFDI0003",
        title: "[Resolved] property must have a setter",
        messageFormat: "Property '{0}' is marked [Resolved] but has no setter",
        category: "Sakura.DI",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The DI system injects [Resolved] values by calling the property setter. A property without a setter cannot be injected.",
        helpLinkUri: "https://github.com/HelloYeew/sakura/wiki");

    public static readonly DiagnosticDescriptor MULTIPLE_BACKGROUND_LOADERS = new DiagnosticDescriptor(
        id: "SFDI0004",
        title: "Only one [BackgroundDependencyLoader] method is allowed per class",
        messageFormat: "'{0}' declares more than one [BackgroundDependencyLoader] method; only the first will be invoked",
        category: "Sakura.DI",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The Sakura DI system only processes the first [BackgroundDependencyLoader] method found on a type. Additional methods with this attribute are silently ignored.",
        helpLinkUri: "https://github.com/HelloYeew/sakura/wiki");

    public static readonly DiagnosticDescriptor RESOLVED_ON_STATIC_MEMBER = new DiagnosticDescriptor(
        id: "SFDI0005",
        title: "[Resolved] cannot be applied to a static member",
        messageFormat: "'{0}' is static but is marked [Resolved]; dependency injection only works on instance members",
        category: "Sakura.DI",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[Resolved] requires an instance target. Static fields and properties cannot be injected by the DI system.",
        helpLinkUri: "https://github.com/HelloYeew/sakura/wiki");

    public static readonly DiagnosticDescriptor CACHED_PROPERTY_HAS_NO_GETTER = new DiagnosticDescriptor(
        id: "SFDI0006",
        title: "[Cached] property must have a getter",
        messageFormat: "Property '{0}' is marked [Cached] but has no getter",
        category: "Sakura.DI",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The DI system caches [Cached] property values by reading through the getter. A write-only property cannot be cached.",
        helpLinkUri: "https://github.com/HelloYeew/sakura/wiki");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            MISSING_PARTIAL_ON_DI_CLASS,
            MISSING_PARTIAL_ON_ENCLOSING_CLASS,
            RESOLVED_PROPERTY_HAS_NO_SETTER,
            MULTIPLE_BACKGROUND_LOADERS,
            RESOLVED_ON_STATIC_MEMBER,
            CACHED_PROPERTY_HAS_NO_GETTER);

    private const string resolved_fqn = "Sakura.Framework.Allocation.ResolvedAttribute";
    private const string cached_fqn = "Sakura.Framework.Allocation.CachedAttribute";
    private const string background_loader_fqn = "Sakura.Framework.Allocation.BackgroundDependencyLoaderAttribute";
    private const string candidate_interface_fqn = "Sakura.Framework.Allocation.IDependencyInjectionCandidate";

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(analyzeNamedType, SymbolKind.NamedType);
    }

    private static void analyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Only interested in classes that implement IDependencyInjectionCandidate.
        if (!implementsCandidate(type))
            return;

        bool hasDiAttr = hasAnyDiAttribute(type);
        if (!hasDiAttr)
            return; // nothing declared at this level — no rules to enforce here

        var declarations = type.DeclaringSyntaxReferences;
        if (declarations.IsEmpty)
            return;

        // Use the first declaration as the location anchor.
        var syntaxRef = declarations[0].GetSyntax(context.CancellationToken);
        if (syntaxRef is not ClassDeclarationSyntax classSyntax)
            return;

        // SFDI0001 — class with DI attrs is not partial
        bool isPartial = classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword);
        if (!isPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MISSING_PARTIAL_ON_DI_CLASS,
                classSyntax.Identifier.GetLocation(),
                type.Name));
        }

        // SFDI0002 — any enclosing class is not partial
        var parent = classSyntax.Parent;
        while (parent is ClassDeclarationSyntax enclosing)
        {
            if (!enclosing.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MISSING_PARTIAL_ON_ENCLOSING_CLASS,
                    enclosing.Identifier.GetLocation(),
                    enclosing.Identifier.Text));
            }
            parent = enclosing.Parent;
        }

        // Per-member checks
        int loaderCount = 0;

        foreach (var member in type.GetMembers())
        {
            // SFDI0004 — multiple [BackgroundDependencyLoader]
            if (member is IMethodSymbol method && hasAttribute(method, background_loader_fqn))
            {
                loaderCount++;
                if (loaderCount > 1)
                {
                    var loc = getFirstLocation(member, context);
                    context.ReportDiagnostic(Diagnostic.Create(
                        MULTIPLE_BACKGROUND_LOADERS,
                        loc,
                        type.Name));
                }
            }

            // SFDI0003, SFDI0005 — [Resolved] checks
            if (hasAttribute(member, resolved_fqn))
            {
                var loc = getFirstLocation(member, context);

                if (member is IPropertySymbol resolvedProp)
                {
                    // SFDI0005 — static
                    if (resolvedProp.IsStatic)
                        context.ReportDiagnostic(Diagnostic.Create(RESOLVED_ON_STATIC_MEMBER, loc, member.Name));

                    // SFDI0003 — no setter
                    if (resolvedProp.SetMethod == null)
                        context.ReportDiagnostic(Diagnostic.Create(RESOLVED_PROPERTY_HAS_NO_SETTER, loc, member.Name));
                }
                else if (member is IFieldSymbol resolvedField && resolvedField.IsStatic)
                {
                    // SFDI0005 — static field
                    context.ReportDiagnostic(Diagnostic.Create(RESOLVED_ON_STATIC_MEMBER, loc, member.Name));
                }
            }

            // SFDI0006 — [Cached] property without getter
            if (hasAttribute(member, cached_fqn))
            {
                if (member is IPropertySymbol cachedProp && cachedProp.GetMethod == null)
                {
                    var loc = getFirstLocation(member, context);
                    context.ReportDiagnostic(Diagnostic.Create(CACHED_PROPERTY_HAS_NO_GETTER, loc, member.Name));
                }
            }
        }
    }

    private static bool implementsCandidate(INamedTypeSymbol type)
        => type.AllInterfaces.Any(i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
             .Replace("global::", string.Empty) == candidate_interface_fqn);

    private static bool hasAnyDiAttribute(INamedTypeSymbol type)
    {
        // Class-level [Cached]
        if (type.GetAttributes().Any(a => isSakuraDiAttr(a)))
            return true;

        // Member-level DI attrs
        return type.GetMembers().Any(m =>
            m.GetAttributes().Any(a => isSakuraDiAttr(a)));
    }

    private static bool isSakuraDiAttr(AttributeData attr)
    {
        string? fqn = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                      .Replace("global::", string.Empty);
        return fqn == resolved_fqn || fqn == cached_fqn || fqn == background_loader_fqn;
    }

    private static bool hasAttribute(ISymbol symbol, string fqn)
        => symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
             .Replace("global::", string.Empty) == fqn);

    private static Location getFirstLocation(ISymbol symbol, SymbolAnalysisContext context)
    {
        var refs = symbol.DeclaringSyntaxReferences;
        if (refs.IsEmpty)
            return Location.None;

        var syntax = refs[0].GetSyntax(context.CancellationToken);
        return syntax switch
        {
            PropertyDeclarationSyntax prop => prop.Identifier.GetLocation(),
            VariableDeclaratorSyntax varDecl => varDecl.Identifier.GetLocation(),
            MethodDeclarationSyntax meth => meth.Identifier.GetLocation(),
            FieldDeclarationSyntax field => field.GetLocation(),
            _ => syntax.GetLocation(),
        };
    }
}
