#!/bin/bash
# Reverts Sakura.Framework back to using the published NuGet package for
# Sakura.Framework.SourceGenerators. Run this before committing.
# Run UseLocalSourceGenerators.sh to switch back to local.

set -e
CSPROJ="Sakura.Framework/Sakura.Framework.csproj"

python3 - "$CSPROJ" <<'EOF'
import sys, re

path = sys.argv[1]
with open(path, 'r') as f:
    content = f.read()

# Remove the ProjectReference block for SourceGenerators
content = re.sub(
    r'\s*<ProjectReference Include="\.\./Sakura\.Framework\.SourceGenerators/Sakura\.Framework\.SourceGenerators\.csproj"[^>]*/>\s*\n'
    r'|'
    r'\s*<ProjectReference Include="\.\./Sakura\.Framework\.SourceGenerators/Sakura\.Framework\.SourceGenerators\.csproj".*?</ProjectReference>\s*\n',
    '\n',
    content,
    flags=re.DOTALL
)

# Remove empty ItemGroups left behind
content = re.sub(r'\s*<ItemGroup>\s*</ItemGroup>\s*\n', '\n', content)

# Add PackageReference back if not already present
if 'Sakura.Framework.SourceGenerators' not in content:
    # Extract current version from git tags or default
    import subprocess
    try:
        tag = subprocess.check_output(
            ['git', 'tag', '--list', 'source-generators-*', '--sort=-version:refname'],
            text=True
        ).strip().splitlines()[0]
        version = tag.replace('source-generators-', '')
    except Exception:
        version = '*'

    content = content.replace(
        '    <PackageReference Include="Sakura.Framework.NativeLibraries"',
        '    <PackageReference Include="Sakura.Framework.SourceGenerators" Version="{}">\n'
        '      <PrivateAssets>all</PrivateAssets>\n'
        '      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n'
        '    </PackageReference>\n'
        '    <PackageReference Include="Sakura.Framework.NativeLibraries"'.format(version)
    )

with open(path, 'w') as f:
    f.write(content)

print(f"Updated {path}")
EOF

echo "Done. Sakura.Framework now uses the published Sakura.Framework.SourceGenerators NuGet package."
