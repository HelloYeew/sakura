// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sakura.Framework.SourceGenerators.Generators.Dependencies;

/// <summary>
/// Data describing a single class that should have DI source code generated for it.
/// Built from Roslyn semantic analysis and handed to the code emitter.
/// </summary>
internal sealed class DependenciesClassCandidate : IEquatable<DependenciesClassCandidate>
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

    public bool Equals(DependenciesClassCandidate? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return FullyQualifiedName == other.FullyQualifiedName &&
               ClassName == other.ClassName &&
               Namespace == other.Namespace &&
               BaseTypeIsCandidate == other.BaseTypeIsCandidate &&
               EnclosingTypes.SequenceEqual(other.EnclosingTypes) &&
               TypeParameters.SequenceEqual(other.TypeParameters) &&
               ResolvedMembers.SequenceEqual(other.ResolvedMembers) &&
               Equals(BackgroundLoader, other.BackgroundLoader) &&
               CachedClassEntries.SequenceEqual(other.CachedClassEntries) &&
               CachedMembers.SequenceEqual(other.CachedMembers);
    }

    public override bool Equals(object? obj) => obj is DependenciesClassCandidate other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + FullyQualifiedName.GetHashCode();
            hash = hash * 31 + ClassName.GetHashCode();
            hash = hash * 31 + (Namespace?.GetHashCode() ?? 0);
            hash = hash * 31 + BaseTypeIsCandidate.GetHashCode();
            foreach (string? e in EnclosingTypes) hash = hash * 31 + e.GetHashCode();
            foreach (string? t in TypeParameters) hash = hash * 31 + t.GetHashCode();
            foreach (var r in ResolvedMembers) hash = hash * 31 + r.GetHashCode();
            hash = hash * 31 + (BackgroundLoader?.GetHashCode() ?? 0);
            foreach (var c in CachedClassEntries) hash = hash * 31 + c.GetHashCode();
            foreach (var m in CachedMembers) hash = hash * 31 + m.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// A field or property marked with [Resolved].
/// </summary>
internal sealed class ResolvedMemberData : IEquatable<ResolvedMemberData>
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

    public bool Equals(ResolvedMemberData? other)
        => other != null &&
           MemberName == other.MemberName &&
           FullyQualifiedTypeName == other.FullyQualifiedTypeName &&
           IsField == other.IsField &&
           CanBeNull == other.CanBeNull;

    public override bool Equals(object? obj) => obj is ResolvedMemberData other && Equals(other);

    public override int GetHashCode()
        => unchecked((MemberName.GetHashCode() * 31 + FullyQualifiedTypeName.GetHashCode()) * 31 + IsField.GetHashCode() * 31 + CanBeNull.GetHashCode());
}

/// <summary>
/// A [BackgroundDependencyLoader] method and the parameters it expects.
/// </summary>
internal sealed class BackgroundDependencyLoaderData : IEquatable<BackgroundDependencyLoaderData>
{
    /// <summary>
    /// Method name
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of parameters that should be resolved from the container
    /// </summary>
    public IReadOnlyList<BackgroundLoaderParameterData> Parameters { get; set; } = new List<BackgroundLoaderParameterData>();

    public bool Equals(BackgroundDependencyLoaderData? other)
        => other != null &&
           MethodName == other.MethodName &&
           Parameters.SequenceEqual(other.Parameters);

    public override bool Equals(object? obj) => obj is BackgroundDependencyLoaderData other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = MethodName.GetHashCode();
            foreach (var p in Parameters) hash = hash * 31 + p.GetHashCode();
            return hash;
        }
    }
}

/// <summary>
/// A single parameter of a [BackgroundDependencyLoader] method
/// </summary>
internal sealed class BackgroundLoaderParameterData : IEquatable<BackgroundLoaderParameterData>
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

    public bool Equals(BackgroundLoaderParameterData? other)
        => other != null && FullyQualifiedTypeName == other.FullyQualifiedTypeName && CanBeNull == other.CanBeNull;

    public override bool Equals(object? obj) => obj is BackgroundLoaderParameterData other && Equals(other);

    public override int GetHashCode() => unchecked(FullyQualifiedTypeName.GetHashCode() * 31 + CanBeNull.GetHashCode());
}

/// <summary>
/// A class-level [Cached] attribute — caches <c>this</c> under a specific type.
/// </summary>
internal sealed class CachedClassData : IEquatable<CachedClassData>
{
    /// <summary>
    /// The type to cache under. When the attribute has no CacheAs argument this equals
    /// the class's own fully qualified type name.
    /// </summary>
    public string CacheAsFullyQualifiedType { get; set; } = string.Empty;

    public bool Equals(CachedClassData? other)
        => other != null && CacheAsFullyQualifiedType == other.CacheAsFullyQualifiedType;

    public override bool Equals(object? obj) => obj is CachedClassData other && Equals(other);

    public override int GetHashCode() => CacheAsFullyQualifiedType.GetHashCode();
}

/// <summary>
/// A field or property decorated with [Cached].
/// </summary>
internal sealed class CachedMemberData : IEquatable<CachedMemberData>
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

    public bool Equals(CachedMemberData? other)
        => other != null &&
           MemberName == other.MemberName &&
           CacheAsFullyQualifiedType == other.CacheAsFullyQualifiedType &&
           IsField == other.IsField;

    public override bool Equals(object? obj) => obj is CachedMemberData other && Equals(other);

    public override int GetHashCode()
        => unchecked((MemberName.GetHashCode() * 31 + CacheAsFullyQualifiedType.GetHashCode()) * 31 + IsField.GetHashCode());
}
