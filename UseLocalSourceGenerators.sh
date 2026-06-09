#!/bin/bash
# Switches Sakura.Framework to use a local build of Sakura.Framework.SourceGenerators
# instead of the NuGet package. Run this when iterating on the source generators locally.
# Run UsePublishedSourceGenerators.sh to revert.

set -e
CSPROJ="Sakura.Framework/Sakura.Framework.csproj"

python3 - "$CSPROJ" <<'EOF'
import sys, re

path = sys.argv[1]
with open(path, 'r') as f:
    content = f.read()

# Remove the PackageReference block for SourceGenerators
content = re.sub(
    r'\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^/]*/>\s*\n'
    r'|'
    r'\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^>]*>.*?</PackageReference>\s*\n',
    '\n',
    content,
    flags=re.DOTALL
)

# Add ProjectReference if not already present
if 'Sakura.Framework.SourceGenerators.csproj' not in content:
    content = content.replace(
        '  <ItemGroup>\n    <None Include="..\icon.png"',
        '  <ItemGroup>\n'
        '    <ProjectReference Include="../Sakura.Framework.SourceGenerators/Sakura.Framework.SourceGenerators.csproj"\n'
        '                      ReferenceOutputAssembly="false"\n'
        '                      OutputItemType="Analyzer" />\n'
        '  </ItemGroup>\n'
        '  <ItemGroup>\n'
        '    <None Include="..\icon.png"'
    )

with open(path, 'w') as f:
    f.write(content)

print(f"Updated {path}")
EOF

echo "Done. Sakura.Framework now uses the local Sakura.Framework.SourceGenerators project."
echo "Run './UsePublishedSourceGenerators.sh' to revert before committing."
