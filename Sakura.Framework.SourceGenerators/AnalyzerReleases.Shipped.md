; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SFDI0001 | Sakura.DI | Warning | DI class must be partial
SFDI0002 | Sakura.DI | Warning | Enclosing class of a DI class must be partial
SFDI0003 | Sakura.DI | Error   | [Resolved] property must have a setter
SFDI0004 | Sakura.DI | Warning | Only one [BackgroundDependencyLoader] method is allowed per class
SFDI0005 | Sakura.DI | Error   | [Resolved] cannot be applied to a static member
SFDI0006 | Sakura.DI | Error   | [Cached] property must have a getter
SFDI0007 | Sakura.DI | Warning | [Resolved(canBeNull: true)] member should be a nullable type
