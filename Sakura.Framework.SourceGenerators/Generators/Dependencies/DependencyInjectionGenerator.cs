// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sakura.Framework.SourceGenerators.Generators.Dependencies;

/// <summary>
/// The source generator that produces a <c>partial class</c> implementing
/// <c>ISourceGeneratedDependencyActivator</c> for every class that:
/// <list type="bullet">
///   <item>Is (or inherits from) <c>IDependencyInjectionCandidate</c></item>
///   <item>Is declared with the <c>partial</c> modifier</item>
///   <item>Has at least one [Resolved], [Cached], or [BackgroundDependencyLoader] member declared directly on it</item>
/// </list>
/// The generated method registers inject and cache delegates so DI can run without any reflection
/// after the first activation.
/// </summary>
[Generator]
public sealed class DependencyInjectionGenerator : IIncrementalGenerator
{
    // Fully qualified attribute names used to recognize DI members.
    private const string resolved_attribute = "Sakura.Framework.Allocation.ResolvedAttribute";
    private const string can_be_null_attribute = "Sakura.Framework.Allocation.CanBeNullAttribute";
    private const string cached_attribute = "Sakura.Framework.Allocation.CachedAttribute";
    private const string background_loader_attribute = "Sakura.Framework.Allocation.BackgroundDependencyLoaderAttribute";
    private const string candidate_interface = "Sakura.Framework.Allocation.IDependencyInjectionCandidate";
    private const string source_generated_activator = "Sakura.Framework.Allocation.ISourceGeneratedDependencyActivator";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect all class declarations that have at least one DI attribute on any member.
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: isCandidate,
                transform: transform)
            .Where(c => c != null)
            .Select((c, _) => c!);

        context.RegisterSourceOutput(candidates, emit);
    }

    /// <summary>
    /// Syntax-only check that does this class declaration look like a DI candidate?
    /// We accept it if:
    /// <list type="bullet">
    ///  <item>it or any member carries one of our DI attributes or</item>
    ///  <item>it directly lists <c>IDependencyInjectionCandidate</c> in its base list
    ///      (this catches the root class — e.g. Drawable — that has no DI attributes itself
    ///       but needs to declare the virtual chain anchor). </item>
    /// </list>
    /// The class and all enclosing classes must also be partial.
    /// </summary>
    private static bool isCandidate(SyntaxNode node, System.Threading.CancellationToken _)
    {
        if (node is not ClassDeclarationSyntax classSyntax)
            return false;

        // The class and all enclosing classes must be partial.
        if (!allEnclosingArePartial(classSyntax))
            return false;

        // Accept if it carries a DI attribute anywhere.
        if (hasDiAttribute(classSyntax))
            return true;

        // Accept if it directly names IDependencyInjectionCandidate in its base list.
        // This is a syntax-only check so we just look for the simple name.
        if (classSyntax.BaseList != null)
        {
            foreach (var baseType in classSyntax.BaseList.Types)
            {
                var name = baseType.Type.ToString();
                if (name.Contains("IDependencyInjectionCandidate"))
                    return true;
            }
        }

        // Accept any partial class that has a base class (i.e. base list is non-empty and the
        // first entry is a class, not just an interface). The semantic transform will filter out
        // types whose base type does not implement IDependencyInjectionCandidate.
        // This is necessary so that relay types like `Container : Drawable` — which have no DI
        // attributes themselves — still get a pass-through override generated, so that subclasses
        // further down (e.g. `CursorZone : Container`) can safely emit their own override.
        if (classSyntax.BaseList != null && classSyntax.BaseList.Types.Count > 0)
            return true;

        return false;
    }

    private static bool allEnclosingArePartial(ClassDeclarationSyntax classSyntax)
    {
        foreach (var ancestor in classSyntax.AncestorsAndSelf().OfType<ClassDeclarationSyntax>())
        {
            if (!ancestor.Modifiers.Any(SyntaxKind.PartialKeyword))
                return false;
        }
        return true;
    }

    private static bool hasDiAttribute(ClassDeclarationSyntax classSyntax)
    {
        static bool matchesDiAttr(AttributeSyntax attr)
        {
            string name = attr.Name.ToString();
            return name.Contains("Resolved") || name.Contains("Cached") || name.Contains("BackgroundDependencyLoader");
        }

        // Class-level attributes.
        if (classSyntax.AttributeLists.SelectMany(al => al.Attributes).Any(matchesDiAttr))
            return true;

        // Member-level attributes.
        foreach (var member in classSyntax.Members)
        {
            if (member.AttributeLists.SelectMany(al => al.Attributes).Any(matchesDiAttr))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Semantic transform
    /// </summary>
    private static DependenciesClassCandidate? transform(
        GeneratorSyntaxContext ctx,
        System.Threading.CancellationToken cancellationToken)
    {
        var classSyntax = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classSyntax, cancellationToken);
        if (symbol == null) return null;

        // Must implement IDependencyInjectionCandidate (directly or via inheritance).
        if (!implementsInterface(symbol, candidate_interface))
            return null;

        // Determine whether a base type is also a candidate (drives virtual vs override).
        bool baseIsCandidate = symbol.BaseType != null &&
                               implementsInterface(symbol.BaseType, candidate_interface);

        // Collect [Resolved] members declared on this type only.
        var resolvedMembers = new List<ResolvedMemberData>();
        foreach (var member in symbol.GetMembers())
        {
            var resolvedAttr = getAttribute(member, resolved_attribute);
            if (resolvedAttr == null) continue;

            bool canBeNull = getResolvedCanBeNull(resolvedAttr);

            switch (member)
            {
                case IPropertySymbol prop:
                    resolvedMembers.Add(new ResolvedMemberData
                    {
                        MemberName = prop.Name,
                        FullyQualifiedTypeName = toGlobalName(prop.Type),
                        IsField = false,
                        CanBeNull = canBeNull,
                    });
                    break;

                case IFieldSymbol field:
                    resolvedMembers.Add(new ResolvedMemberData
                    {
                        MemberName = field.Name,
                        FullyQualifiedTypeName = toGlobalName(field.Type),
                        IsField = true,
                        CanBeNull = canBeNull,
                    });
                    break;
            }
        }

        // Collect [BackgroundDependencyLoader] method declared on THIS type.
        BackgroundDependencyLoaderData? loader = null;
        foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (!hasAttribute(member, background_loader_attribute)) continue;

            loader = new BackgroundDependencyLoaderData
            {
                MethodName = member.Name,
                Parameters = member.Parameters
                    .Select(p => new BackgroundLoaderParameterData
                    {
                        FullyQualifiedTypeName = toGlobalName(p.Type),
                        CanBeNull = loaderParameterCanBeNull(p),
                    })
                    .ToList(),
            };
            break; // only first, analyzer will flag multiples
        }

        // Collect class-level [Cached] attributes.
        var cachedClassEntries = new List<CachedClassData>();
        foreach (var attr in symbol.GetAttributes())
        {
            if (!isCachedAttribute(attr)) continue;

            // CacheAs argument is the first constructor arg, if present.
            string cacheAsType = getCacheAsType(attr) ?? toGlobalName(symbol);
            cachedClassEntries.Add(new CachedClassData { CacheAsFullyQualifiedType = cacheAsType });
        }

        // Collect member-level [Cached].
        var cachedMembers = new List<CachedMemberData>();
        foreach (var member in symbol.GetMembers())
        {
            foreach (var attr in member.GetAttributes())
            {
                if (!isCachedAttribute(attr)) continue;

                switch (member)
                {
                    case IPropertySymbol prop:
                    {
                        string cacheAsType = getCacheAsType(attr) ?? toGlobalName(prop.Type);
                        cachedMembers.Add(new CachedMemberData
                        {
                            MemberName = prop.Name,
                            CacheAsFullyQualifiedType = cacheAsType,
                            IsField = false,
                        });
                        break;
                    }
                    case IFieldSymbol field:
                    {
                        string cacheAsType = getCacheAsType(attr) ?? toGlobalName(field.Type);
                        cachedMembers.Add(new CachedMemberData
                        {
                            MemberName = field.Name,
                            CacheAsFullyQualifiedType = cacheAsType,
                            IsField = true,
                        });
                        break;
                    }
                }
            }
        }

        bool hasAnything = resolvedMembers.Count > 0 || loader != null ||
                           cachedClassEntries.Count > 0 || cachedMembers.Count > 0;

        // Directly implements the candidate interface, this is the virtual chain root.
        // Must always generate even with no DI attributes, so subclass overrides have a base to call.
        bool isDirectCandidate = symbol.Interfaces.Any(i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
             .Replace("global::", string.Empty) == candidate_interface);

        // Skip only if nothing to do at this level and not the chain root and no base to override.
        if (!hasAnything && !baseIsCandidate && !isDirectCandidate)
            return null;
        // Collect enclosing type names (outermost first) for nested classes.
        var enclosingTypes = new List<string>();
        var enclosing = symbol.ContainingType;
        while (enclosing != null)
        {
            string enclosingName = enclosing.Name;
            if (enclosing.TypeParameters.Length > 0)
                enclosingName += $"<{string.Join(", ", enclosing.TypeParameters.Select(tp => tp.Name))}>";
            enclosingTypes.Insert(0, enclosingName);
            enclosing = enclosing.ContainingType;
        }

        return new DependenciesClassCandidate
        {
            FullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClassName = symbol.Name,
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            EnclosingTypes = enclosingTypes,
            BaseTypeIsCandidate = baseIsCandidate,
            TypeParameters = symbol.TypeParameters.Select(tp => tp.Name).ToList(),
            ResolvedMembers = resolvedMembers,
            BackgroundLoader = loader,
            CachedClassEntries = cachedClassEntries,
            CachedMembers = cachedMembers,
        };
    }

    private static void emit(SourceProductionContext ctx, DependenciesClassCandidate candidate)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// This file was generated by Sakura.Framework.SourceGenerators.");
        sb.AppendLine("// Do not edit manually.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        // Open namespace block (if any).
        if (candidate.Namespace != null)
        {
            sb.AppendLine($"namespace {candidate.Namespace}");
            sb.AppendLine("{");
        }

        string indent = candidate.Namespace != null ? "    " : string.Empty;

        // Open enclosing partial class wrappers (outermost first) for nested classes.
        foreach (var enclosingName in candidate.EnclosingTypes)
        {
            sb.AppendLine($"{indent}partial class {enclosingName}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        // Build generic type parameter suffix.
        string typeParams = candidate.TypeParameters.Count > 0
            ? $"<{string.Join(", ", candidate.TypeParameters)}>"
            : string.Empty;

        // Class declaration — add ISourceGeneratedDependencyActivator.
        // If the base type is already a candidate it already declared the interface, so we only
        // add it when we're the first in the chain.
        string interfaceClause = candidate.BaseTypeIsCandidate
            ? string.Empty
            : $" : global::{source_generated_activator}";

        sb.AppendLine($"{indent}partial class {candidate.ClassName}{typeParams}{interfaceClause}");
        sb.AppendLine($"{indent}{{");

        string memberIndent = indent + "    ";
        string bodyIndent = memberIndent + "    ";

        // RegisterForDependencyActivation
        string modifier = candidate.BaseTypeIsCandidate ? "override" : "virtual";

        sb.AppendLine($"{memberIndent}public {modifier} void RegisterForDependencyActivation(");
        sb.AppendLine($"{memberIndent}    global::Sakura.Framework.Allocation.IDependencyActivatorRegistry registry)");
        sb.AppendLine($"{memberIndent}{{");

        // Guard: skip if already registered.
        sb.AppendLine($"{bodyIndent}if (registry.IsRegistered(typeof({candidate.FullyQualifiedName})))");
        sb.AppendLine($"{bodyIndent}    return;");
        sb.AppendLine();

        // Walk base first.
        if (candidate.BaseTypeIsCandidate)
        {
            sb.AppendLine($"{bodyIndent}base.RegisterForDependencyActivation(registry);");
            sb.AppendLine();
        }

        // registry.Register(blablabla) — single call with both inject and cache delegates
        sb.AppendLine($"{bodyIndent}registry.Register(");
        sb.AppendLine($"{bodyIndent}    typeof({candidate.FullyQualifiedName}),");
        sb.AppendLine();

        // Inject delegate (or null).
        if (candidate.HasInjection)
        {
            sb.AppendLine($"{bodyIndent}    // Inject: [Resolved] members + [BackgroundDependencyLoader]");
            sb.AppendLine($"{bodyIndent}    (target, deps) =>");
            sb.AppendLine($"{bodyIndent}    {{");
            sb.AppendLine($"{bodyIndent}        var self = ({candidate.FullyQualifiedName})target;");

            foreach (var resolved in candidate.ResolvedMembers)
            {
                string resolveCall = resolved.CanBeNull ? "TryGet" : "Get";
                sb.AppendLine($"{bodyIndent}        self.{resolved.MemberName} = deps.{resolveCall}<{resolved.FullyQualifiedTypeName}>();");
            }

            if (candidate.BackgroundLoader != null)
            {
                var loader = candidate.BackgroundLoader;
                if (loader.Parameters.Count == 0)
                {
                    sb.AppendLine($"{bodyIndent}        self.{loader.MethodName}();");
                }
                else
                {
                    string args = string.Join(", ", loader.Parameters.Select(p => $"deps.{(p.CanBeNull ? "TryGet" : "Get")}<{p.FullyQualifiedTypeName}>()"));
                    sb.AppendLine($"{bodyIndent}        self.{loader.MethodName}({args});");
                }
            }

            sb.AppendLine($"{bodyIndent}    }},");
        }
        else
        {
            sb.AppendLine($"{bodyIndent}    // No [Resolved] or [BackgroundDependencyLoader] at this type level.");
            sb.AppendLine($"{bodyIndent}    null,");
        }

        sb.AppendLine();

        // Cache delegate (or null).
        if (candidate.HasCache)
        {
            sb.AppendLine($"{bodyIndent}    // Cache: [Cached] members");
            sb.AppendLine($"{bodyIndent}    (target, parent) =>");
            sb.AppendLine($"{bodyIndent}    {{");
            sb.AppendLine($"{bodyIndent}        var self = ({candidate.FullyQualifiedName})target;");
            sb.AppendLine($"{bodyIndent}        var deps = new global::Sakura.Framework.Allocation.DependencyContainer(parent);");

            foreach (var entry in candidate.CachedClassEntries)
                sb.AppendLine($"{bodyIndent}        deps.CacheAs<{entry.CacheAsFullyQualifiedType}>(self);");

            foreach (var member in candidate.CachedMembers)
                sb.AppendLine($"{bodyIndent}        deps.CacheAs<{member.CacheAsFullyQualifiedType}>(self.{member.MemberName});");

            sb.AppendLine($"{bodyIndent}        return deps;");
            sb.AppendLine($"{bodyIndent}    }}");
        }
        else
        {
            sb.AppendLine($"{bodyIndent}    // No [Cached] members at this type level.");
            sb.AppendLine($"{bodyIndent}    null");
        }

        sb.AppendLine($"{bodyIndent});"); // close registry.Register(
        sb.AppendLine($"{memberIndent}}}"); // close RegisterForDependencyActivation

        sb.AppendLine($"{indent}}}"); // close class

        // Close enclosing partial class wrappers (innermost first).
        for (int i = candidate.EnclosingTypes.Count - 1; i >= 0; i--)
        {
            indent = indent.Length >= 4 ? indent.Substring(4) : string.Empty;
            sb.AppendLine($"{indent}}}"); // close enclosing class
        }

        if (candidate.Namespace != null)
            sb.AppendLine("}"); // close namespace

        // Use a stable hint name derived from the fully qualified name.
        string hintName = candidate.FullyQualifiedName
            .Replace("global::", string.Empty)
            .Replace('.', '_')
            .Replace('<', '_')
            .Replace('>', '_');

        ctx.AddSource($"{hintName}.DI.g.cs", sb.ToString());
    }

    private static bool implementsInterface(ITypeSymbol symbol, string interfaceFqn)
    {
        return symbol.AllInterfaces.Any(i =>
            i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
             .Replace("global::", string.Empty) == interfaceFqn);
    }

    private static bool hasAttribute(ISymbol symbol, string attributeFqn)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
             .Replace("global::", string.Empty) == attributeFqn);
    }

    private static AttributeData? getAttribute(ISymbol symbol, string attributeFqn)
    {
        return symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
             .Replace("global::", string.Empty) == attributeFqn);
    }

    /// <summary>
    /// Reads the CanBeNull state from a [Resolved] attribute, honoring both the positional
    /// constructor argument (<c>[Resolved(true)]</c>) and the named property (<c>[Resolved(CanBeNull = true)]</c>).
    /// </summary>
    private static bool getResolvedCanBeNull(AttributeData attr)
    {
        // Positional constructor argument: ResolvedAttribute(bool canBeNull).
        if (attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is bool ctorValue)
            return ctorValue;

        // Named argument: CanBeNull = true.
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "CanBeNull" && named.Value.Value is bool namedValue)
                return namedValue;
        }

        return false;
    }

    /// <summary>
    /// A loader parameter resolves optionally when it carries [CanBeNull] or is declared as a
    /// nullable reference type (e.g. <c>Foo?</c>).
    /// </summary>
    private static bool loaderParameterCanBeNull(IParameterSymbol parameter)
    {
        if (hasAttribute(parameter, can_be_null_attribute))
            return true;

        return parameter.Type.IsReferenceType &&
               parameter.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private static bool isCachedAttribute(AttributeData attr)
    {
        return attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty) == cached_attribute;
    }

    /// <summary>
    /// Returns the CacheAs type from a [Cached(...)] attribute's first constructor argument, or null.
    /// </summary>
    private static string? getCacheAsType(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length == 0)
            return null;
        if (attr.ConstructorArguments[0].Value is not ITypeSymbol typeArg)
            return null;
        return toGlobalName(typeArg);
    }

    private static string toGlobalName(ITypeSymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
