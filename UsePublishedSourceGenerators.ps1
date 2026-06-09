# Reverts Sakura.Framework back to using the published NuGet package for
# Sakura.Framework.SourceGenerators. Run this before committing.
# Run UseLocalSourceGenerators.ps1 to switch back to local.

$csproj = "Sakura.Framework/Sakura.Framework.csproj"
$content = Get-Content $csproj -Raw

# Remove the ProjectReference block for SourceGenerators
$content = $content -replace '(?s)\s*<ProjectReference Include="\.\./Sakura\.Framework\.SourceGenerators/Sakura\.Framework\.SourceGenerators\.csproj"[^>]*/>\s*\n', "`n"

# Remove empty ItemGroups left behind
$content = $content -replace '(?s)\s*<ItemGroup>\s*</ItemGroup>\s*\n', "`n"

# Add PackageReference back if not already present
if ($content -notmatch 'Sakura\.Framework\.SourceGenerators') {
    # Try to get latest version from git tags
    try {
        $tag = git tag --list 'source-generators-*' --sort=-version:refname 2>$null | Select-Object -First 1
        $version = $tag -replace '^source-generators-', ''
        if (-not $version) { $version = '*' }
    } catch {
        $version = '*'
    }

    $packageRef = @"
    <PackageReference Include="Sakura.Framework.SourceGenerators" Version="$version">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
"@
    $content = $content -replace '    <PackageReference Include="Sakura\.Framework\.NativeLibraries"', "$packageRef`n    <PackageReference Include=`"Sakura.Framework.NativeLibraries`""
}

Set-Content $csproj $content -NoNewline
Write-Host "Done. Sakura.Framework now uses the published Sakura.Framework.SourceGenerators NuGet package."
