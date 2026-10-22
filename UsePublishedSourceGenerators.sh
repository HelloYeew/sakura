#!/bin/bash
# Reverts Sakura.Framework back to using the published NuGet package for
# Sakura.Framework.SourceGenerators. Run this before committing.
# Run UseLocalSourceGenerators.sh to switch back to local.

set -e
CSPROJ="Sakura.Framework/Sakura.Framework.csproj"

python3 - "$CSPROJ" <<'EOF'
import sys, re, subprocess

path = sys.argv[1]
with open(path, 'r') as f:
    content = f.read()

package_reference = '<PackageReference Include="Sakura.Framework.SourceGenerators"'
# The package goes back where it was, after the native libraries, in name order.
anchor = re.search(r'    <PackageReference Include="Sakura\.Framework\.NativeLibraries"[^\n]*\n', content)

if package_reference not in content and not anchor:
    sys.exit(f"Couldn't find where to add the generator package in {path}; nothing changed.")

# Only the reference marked as an analyzer: the plain one beside the code fixes builds the generator before packing,
# and stays.
content = re.sub(
    r'\s*<ProjectReference Include="[^"]*Sakura\.Framework\.SourceGenerators\.csproj"[^>]*OutputItemType="Analyzer"[^>]*/>[ \t]*\n',
    '\n',
    content
)

# Remove empty ItemGroups left behind
content = re.sub(r'\s*<ItemGroup>\s*</ItemGroup>[ \t]*\n', '\n', content)

if package_reference not in content:
    def committed_version():
        # The version the committed csproj has, which is the one this was switched away from.
        try:
            committed = subprocess.check_output(['git', 'show', f'HEAD:{path}'], text=True, stderr=subprocess.DEVNULL)
            match = re.search(r'<PackageReference Include="Sakura\.Framework\.SourceGenerators" Version="([^"]+)"', committed)
            if match:
                return match.group(1)
        except Exception:
            pass

        try:
            tag = subprocess.check_output(
                ['git', 'tag', '--list', 'source-generators-*', '--sort=-version:refname'],
                text=True
            ).strip().splitlines()[0]
            return tag.replace('source-generators-', '')
        except Exception:
            print("No committed or tagged version found; using the latest published one.")
            return '*'

    anchor = re.search(r'    <PackageReference Include="Sakura\.Framework\.NativeLibraries"[^\n]*\n', content)
    content = (
        content[:anchor.end()]
        + '    <PackageReference Include="Sakura.Framework.SourceGenerators" Version="{}">\n'
          '      <PrivateAssets>all</PrivateAssets>\n'
          '      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n'
          '    </PackageReference>\n'.format(committed_version())
        + content[anchor.end():]
    )

with open(path, 'w') as f:
    f.write(content)

print(f"Updated {path}")
EOF

echo "Done. Sakura.Framework now uses the published Sakura.Framework.SourceGenerators NuGet package."
