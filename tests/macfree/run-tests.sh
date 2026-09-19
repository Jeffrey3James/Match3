#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

# Ubuntu/Debian prerequisites: mono-mcs libmono-system-net-http4.0-cil
mcs -langversion:7.2 -r:System.Net.Http.dll \
  -out:"$out/MacFreeCompatibilityTests.exe" \
  "$root/Assets/MacFree/Editor/UbaApi.cs" \
  "$root/Assets/MacFree/Editor/JsonUtil.cs" \
  "$root/Assets/MacFree/Editor/ErrorTranslator.cs" \
  "$root/Assets/MacFree/Editor/SetupPipeline.cs" \
  "$root/Assets/MacFree/Editor/ThirdParty/SimpleJSON.cs" \
  "$root/tests/macfree/PipelineDoubles.cs" \
  "$root/tests/macfree/UbaCompatibilityTests.cs"
mono "$out/MacFreeCompatibilityTests.exe"
