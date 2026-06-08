@setlocal
@echo off

python %~dp0update_shaderc_sources.py --dir %~dp0shaderc --file %~dp0known_good.json

:: Android NDK 27+ need this policy set on shaderc (as well as other tools)
move /y %~dp0shaderc\CMakeLists.txt %~dp0shaderc\CMakeLists.tmp

setlocal enableDelayedExpansion
set p=
for /f "tokens=* delims=" %%a in (%~dp0shaderc\CMakeLists.tmp) do (
  if "!p!"=="cmake_minimum_required(VERSION 2.8.12)" echo cmake_policy(SET CMP0057 NEW^)>>%~dp0shaderc\CMakeLists.txt
  (echo %%a) >>%~dp0shaderc\CMakeLists.txt
  set p=%%a
)
del %~dp0shaderc\CMakeLists.tmp

:: Fix glslang install flag: replace generator expression with plain OFF
set THIRD_PARTY_CMAKE=%~dp0shaderc\third_party\CMakeLists.txt
if exist "%THIRD_PARTY_CMAKE%" (
    powershell -Command "(Get-Content '%THIRD_PARTY_CMAKE%') -replace 'set\(GLSLANG_ENABLE_INSTALL \$<NOT:\$\{SKIP_GLSLANG_INSTALL\}>\)', 'set(GLSLANG_ENABLE_INSTALL OFF)' | Set-Content '%THIRD_PARTY_CMAKE%'"
    echo Patched %THIRD_PARTY_CMAKE%: GLSLANG_ENABLE_INSTALL set to OFF
)

:: Fix SPIRV-Tools Xcode build system conflict: guard the shared library target
set SPIRV_SOURCE_CMAKE=%~dp0shaderc\third_party\spirv-tools\source\CMakeLists.txt
if exist "%SPIRV_SOURCE_CMAKE%" (
    python -c "import sys,re; path=r'%SPIRV_SOURCE_CMAKE%'; f=open(path); c=f.read(); f.close(); p=r'(# Always build \$\{SPIRV_TOOLS\}-shared\..*?)\n(if\(SPIRV_TOOLS_BUILD_STATIC\))'; r='if(NOT CMAKE_GENERATOR MATCHES \"Xcode\")\n'+r'\1'+'\nendif() # NOT Xcode generator\n'+r'\2'; nc,n=re.subn(p,r,c,flags=re.DOTALL); open(path,'w').write(nc) if n else None; print(f'Patched {n} occurrences')"
    echo Patched %SPIRV_SOURCE_CMAKE%: SPIRV-Tools-shared guarded against Xcode generator
)

