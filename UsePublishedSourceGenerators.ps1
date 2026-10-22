# Reverts Sakura.Framework back to using the published NuGet package for
# Sakura.Framework.SourceGenerators. Run this before committing.
# Run UseLocalSourceGenerators.ps1 to switch back to local.

$csproj = "Sakura.Framework/Sakura.Framework.csproj"
$content = Get-Content $csproj -Raw

$package = '<PackageReference Include="Sakura\.Framework\.SourceGenerators"'
# The package goes back where it was, after the native libraries, in name order.
$anchor = '(    <PackageReference Include="Sakura\.Framework\.NativeLibraries"[^\r\n]*\r?\n)'

if (($content -notmatch $package) -and ($content -notmatch $anchor)) {
    Write-Error "Couldn't find where to add the generator package in $csproj; nothing changed."
    exit 1
}

# Only the reference marked as an analyzer: the plain one beside the code fixes builds the generator before packing,
# and stays.
$content = $content -replace '\s*<ProjectReference Include="[^"]*Sakura\.Framework\.SourceGenerators\.csproj"[^>]*OutputItemType="Analyzer"[^>]*/>[ \t]*\r?\n', "`n"

# Remove empty ItemGroups left behind
$content = $content -replace '(?s)\s*<ItemGroup>\s*</ItemGroup>[ \t]*\r?\n', "`n"

if ($content -notmatch $package) {
    # The version the committed csproj has, which is the one this was switched away from.
    $version = $null
    $committed = git show "HEAD:$csproj" 2>$null | Out-String
    if ($committed -match '<PackageReference Include="Sakura\.Framework\.SourceGenerators" Version="([^"]+)"') {
        $version = $Matches[1]
    }

    if (-not $version) {
        $tag = git tag --list 'source-generators-*' --sort=-version:refname 2>$null | Select-Object -First 1
        if ($tag) { $version = $tag -replace '^source-generators-', '' }
    }

    if (-not $version) {
        Write-Host "No committed or tagged version found; using the latest published one."
        $version = '*'
    }

    $packageRef = @"
    <PackageReference Include="Sakura.Framework.SourceGenerators" Version="$version">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>

"@
    $content = ([regex]$anchor).Replace($content, "`$1$packageRef", 1)
}

Set-Content $csproj $content -NoNewline
Write-Host "Done. Sakura.Framework now uses the published Sakura.Framework.SourceGenerators NuGet package."
