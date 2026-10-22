# Switches Sakura.Framework to use a local build of Sakura.Framework.SourceGenerators
# instead of the NuGet package. Run this when iterating on the source generators locally.
# Run UsePublishedSourceGenerators.ps1 to revert.

$csproj = "Sakura.Framework/Sakura.Framework.csproj"
$content = Get-Content $csproj -Raw

# The csproj references the generator project already, without OutputItemType, only so it builds before packing;
# that reference runs nothing. Only one marked as an analyzer runs the generator.
$analyzer = '<ProjectReference Include="[^"]*Sakura\.Framework\.SourceGenerators\.csproj"[^>]*OutputItemType="Analyzer"'
$package = '<PackageReference Include="Sakura\.Framework\.SourceGenerators"'
$anchor = '  <ItemGroup>\r?\n    <None Include="\.\.\\icon\.png"'

if (($content -match $analyzer) -and ($content -notmatch $package)) {
    Write-Host "$csproj already uses the local source generators."
    exit 0
}

if (($content -notmatch $analyzer) -and ($content -notmatch $anchor)) {
    Write-Error "Couldn't find where to add the generator reference in $csproj; nothing changed."
    exit 1
}

# Remove the PackageReference block for SourceGenerators
$content = $content -replace '(?s)\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^>]*>.*?</PackageReference>[ \t]*\r?\n', "`n"
$content = $content -replace '\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^>]*/>[ \t]*\r?\n', "`n"

if ($content -notmatch $analyzer) {
    $projectRef = @"
  <ItemGroup>
    <ProjectReference Include="../Sakura.Framework.SourceGenerators/Sakura.Framework.SourceGenerators.csproj"
                      ReferenceOutputAssembly="false"
                      OutputItemType="Analyzer" />
  </ItemGroup>
"@
    $content = ([regex]$anchor).Replace($content, "$projectRef`n  <ItemGroup>`n    <None Include=""..\icon.png""", 1)
}

Set-Content $csproj $content -NoNewline
Write-Host "Done. Sakura.Framework now uses the local Sakura.Framework.SourceGenerators project."
Write-Host "Run '.\UsePublishedSourceGenerators.ps1' to revert before committing."
