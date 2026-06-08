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
