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

# The csproj references the generator project already, without OutputItemType, only so it builds before packing;
# that reference runs nothing. Only one marked as an analyzer runs the generator.
analyzer = re.compile(r'<ProjectReference Include="[^"]*Sakura\.Framework\.SourceGenerators\.csproj"[^>]*OutputItemType="Analyzer"')
package = re.compile(
    r'\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^>]*/>[ \t]*\n'
    r'|'
    r'\s*<PackageReference Include="Sakura\.Framework\.SourceGenerators"[^>]*>.*?</PackageReference>[ \t]*\n',
    flags=re.DOTALL
)

if analyzer.search(content) and not package.search(content):
    print(f"{path} already uses the local source generators.")
    sys.exit(0)

anchor = '  <ItemGroup>\n    <None Include="..\\icon.png"'
if not analyzer.search(content) and anchor not in content:
    sys.exit(f"Couldn't find where to add the generator reference in {path}; nothing changed.")

content = package.sub('\n', content)

if not analyzer.search(content):
    content = content.replace(
        anchor,
        '  <ItemGroup>\n'
        '    <ProjectReference Include="../Sakura.Framework.SourceGenerators/Sakura.Framework.SourceGenerators.csproj"\n'
        '                      ReferenceOutputAssembly="false"\n'
        '                      OutputItemType="Analyzer" />\n'
        '  </ItemGroup>\n'
        + anchor,
        1
    )

with open(path, 'w') as f:
    f.write(content)

print(f"Updated {path}")
EOF

echo "Done. Sakura.Framework now uses the local Sakura.Framework.SourceGenerators project."
echo "Run './UsePublishedSourceGenerators.sh' to revert before committing."
