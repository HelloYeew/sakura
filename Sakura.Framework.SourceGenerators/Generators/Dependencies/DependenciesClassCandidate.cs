// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.SourceGenerators.Generators.Dependencies;

/// <summary>
/// Data describing a single class that should have DI source code generated for it.
/// Built from Roslyn semantic analysis and handed to the code emitter.
/// </summary>
internal sealed class DependenciesClassCandidate
{
    /// <summary>
    /// Fully qualified type name, e.g. "global::Sakura.Framework.Graphics.Drawables.Drawable".
    /// </summary>
    public string FullyQualifiedName { get; set; } = string.Empty;

    /// <summary>
    /// Simple class name without namespace.
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// Namespace the class lives in, or null for the global namespace.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Enclosing type names (outermost first) for nested classes, e.g. ["CursorContainer"] for
    /// CursorContainer.DefaultCursor. Empty for top-level classes.
    /// Each entry is just the simple name (with type params if generic).
    /// </summary>
    public IReadOnlyList<string> EnclosingTypes { get; set; } = new List<string>();

    /// <summary>
    /// True when an ancestor type also implements IDependencyInjectionCandidate,
    /// meaning the generated method should use <c>override</c> instead of <c>virtual</c>
    /// and should call <c>base.RegisterForDependencyActivation(registry)</c>.
    /// </summary>
    public bool BaseTypeIsCandidate { get; set; }

    /// <summary>
    /// Any generic type parameters (e.g. ["T"] for MyClass&lt;T&gt;), empty if non-generic.
    /// </summary>
    public IReadOnlyList<string> TypeParameters { get; set; } = new List<string>();

    /// <summary>
    /// All fields and properties marked with [Resolved] declared directly on this type.
    /// </summary>
    public IReadOnlyList<ResolvedMemberData> ResolvedMembers { get; set; } = new List<ResolvedMemberData>();

    /// <summary>
    /// The [BackgroundDependencyLoader] method declared directly on this type, or null if absent.
    /// </summary>
    public BackgroundDependencyLoaderData? BackgroundLoader { get; set; }

    /// <summary>
    /// Class-level [Cached] attributes — each one says "cache <c>this</c> under CacheAsType".
    /// </summary>
    public IReadOnlyList<CachedClassData> CachedClassEntries { get; set; } = new List<CachedClassData>();

    /// <summary>
    /// Fields and properties on this type decorated with [Cached].
    /// </summary>
    public IReadOnlyList<CachedMemberData> CachedMembers { get; set; } = new List<CachedMemberData>();

    /// <summary>
    /// True if this type level needs any injection code at all.
    /// </summary>
    public bool HasInjection => ResolvedMembers.Count > 0 || BackgroundLoader != null;

    /// <summary>
    /// True if this type level needs a cache delegate.
    /// </summary>
    public bool HasCache => CachedClassEntries.Count > 0 || CachedMembers.Count > 0;
}

/// <summary>
/// A field or property marked with [Resolved].
/// </summary>
internal sealed class ResolvedMemberData
{
    /// <summary>
    /// Member name as it appears in source code.
    /// </summary>
    public string MemberName { get; set; } = string.Empty;

    /// <summary>
    /// Fully qualified type of the member, e.g. "global::Sakura.Framework.Platform.IWindow".
    /// </summary>
    public string FullyQualifiedTypeName { get; set; } = string.Empty;

    /// <summary>
    /// True if this is a field; false if it is a property.
    /// </summary>
    public bool IsField { get; set; }

    /// <summary>
    /// True when the member is marked <c>[Resolved(canBeNull: true)]</c>, meaning it should be
    /// resolved via <c>TryGet&lt;T&gt;</c> (null when missing) rather than <c>Get&lt;T&gt;</c>.
    /// </summary>
    public bool CanBeNull { get; set; }
}

/// <summary>
/// A [BackgroundDependencyLoader] method and the parameters it expects.
/// </summary>
internal sealed class BackgroundDependencyLoaderData
{
    /// <summary>
    /// Method name
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of parameters that should be resolved from the container
    /// </summary>
    public IReadOnlyList<BackgroundLoaderParameterData> Parameters { get; set; } = new List<BackgroundLoaderParameterData>();
}

/// <summary>
/// A single parameter of a [BackgroundDependencyLoader] method
/// </summary>
internal sealed class BackgroundLoaderParameterData
{
    /// <summary>
    /// Fully qualified type, e.g. "global::Sakura.Framework.AppHost"
    /// </summary>
    public string FullyQualifiedTypeName { get; set; } = string.Empty;

    /// <summary>
    /// True when the parameter is marked <c>[CanBeNull]</c> or declared as a nullable reference
    /// type, meaning it should be resolved via <c>TryGet&lt;T&gt;</c> rather than <c>Get&lt;T&gt;</c>.
    /// </summary>
    public bool CanBeNull { get; set; }
}

/// <summary>
/// A class-level [Cached] attribute — caches <c>this</c> under a specific type.
/// </summary>
internal sealed class CachedClassData
{
    /// <summary>
    /// The type to cache under. When the attribute has no CacheAs argument this equals
    /// the class's own fully qualified type name.
    /// </summary>
    public string CacheAsFullyQualifiedType { get; set; } = string.Empty;
}

/// <summary>
/// A field or property decorated with [Cached].
/// </summary>
internal sealed class CachedMemberData
{
    /// <summary>
    /// Member name as it appears in source code.
    /// </summary>
    public string MemberName { get; set; } = string.Empty;

    /// <summary>
    /// The type to cache under. When the attribute has no CacheAs argument this equals
    /// the member's declared type.
    /// </summary>
    public string CacheAsFullyQualifiedType { get; set; } = string.Empty;

    /// <summary>
    /// True if this is a field; false if it is a property.
    /// </summary>
    public bool IsField { get; set; }
}
