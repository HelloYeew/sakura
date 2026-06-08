#!/usr/bin/env bash

scriptPath="`dirname \"$0\"`"

python $scriptPath/update_shaderc_sources.py --dir $scriptPath/shaderc --file $scriptPath/known_good.json

# Fix glslang install flag: shaderc's third_party/CMakeLists.txt sets
# GLSLANG_ENABLE_INSTALL using a generator expression ($<NOT:...>) as a plain
# string, which evaluates to a non-empty (truthy) value and overrides any -D
# cache entry. Replace it with a proper boolean OFF so install rules are skipped.
THIRD_PARTY_CMAKE="$scriptPath/shaderc/third_party/CMakeLists.txt"
if [ -f "$THIRD_PARTY_CMAKE" ]; then
    sed -i.bak 's/set(GLSLANG_ENABLE_INSTALL \$<NOT:\${SKIP_GLSLANG_INSTALL}>)/set(GLSLANG_ENABLE_INSTALL OFF)/' "$THIRD_PARTY_CMAKE"
    rm -f "$THIRD_PARTY_CMAKE.bak"
    echo "Patched $THIRD_PARTY_CMAKE: GLSLANG_ENABLE_INSTALL set to OFF"
fi

# Fix SPIRV-Tools Xcode "new build system" conflict: build-version.inc is a
# custom command output shared by both SPIRV-Tools-shared and SPIRV-Tools-static,
# which the Xcode new build system forbids. Guard the shared library target so it
# is only built when not using the Xcode generator (iOS/macOS use Xcode generator).
SPIRV_SOURCE_CMAKE="$scriptPath/shaderc/third_party/spirv-tools/source/CMakeLists.txt"
if [ -f "$SPIRV_SOURCE_CMAKE" ]; then
    # Wrap "add_library(${SPIRV_TOOLS}-shared SHARED ...)" in an if(NOT CMAKE_GENERATOR MATCHES "Xcode") guard
    python3 - "$SPIRV_SOURCE_CMAKE" <<'EOF'
import sys, re

path = sys.argv[1]
with open(path) as f:
    content = f.read()

marker = '# Always build ${SPIRV_TOOLS}-shared.'
guard_start = 'if(NOT CMAKE_GENERATOR MATCHES "Xcode")\n'
guard_end = '\nendif() # NOT Xcode generator'

# Find the block: from the marker comment to just before "if(SPIRV_TOOLS_BUILD_STATIC)"
pattern = r'(# Always build \$\{SPIRV_TOOLS\}-shared\..*?)\n(if\(SPIRV_TOOLS_BUILD_STATIC\))'
replacement = guard_start + r'\1' + guard_end + r'\n\2'

new_content, count = re.subn(pattern, replacement, content, flags=re.DOTALL)
if count == 0:
    print(f"WARNING: pattern not found in {path}, skipping patch")
    sys.exit(0)

with open(path, 'w') as f:
    f.write(new_content)
print(f"Patched {path}: SPIRV-Tools-shared guarded against Xcode generator")
EOF
fi
