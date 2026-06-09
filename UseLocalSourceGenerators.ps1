# Switches Sakura.Framework to use a local build of Sakura.Framework.SourceGenerators
# instead of the NuGet package. Run this when iterating on the source generators locally.
# Run UsePublishedSourceGenerators.ps1 to revert.

$csproj = "Sakura.Framework/Sakura.Framework.csproj"
$content = Get-Content $csproj -Raw

# Remove the PackageReference block for SourceGenerators
$content = $content -replace '(?s)\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^>]*>.*?</PackageReference>\s*\n', "`n"
$content = $content -replace '\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^/]*/>\s*\n', "`n"

# Add ProjectReference if not already present
if ($content -notmatch 'Sakura\.Framework\.SourceGenerators\.csproj') {
    $projectRef = @"
  <ItemGroup>
    <ProjectReference Include="../Sakura.Framework.SourceGenerators/Sakura.Framework.SourceGenerators.csproj"
                      ReferenceOutputAssembly="false"
                      OutputItemType="Analyzer" />
  </ItemGroup>
"@
    $content = $content -replace '  <ItemGroup>\r?\n    <None Include="\.\.\\icon\.png"', "$projectRef`n  <ItemGroup>`n    <None Include=""..\icon.png"""
}

Set-Content $csproj $content -NoNewline
Write-Host "Done. Sakura.Framework now uses the local Sakura.Framework.SourceGenerators project."
Write-Host "Run '.\UsePublishedSourceGenerators.ps1' to revert before committing."
