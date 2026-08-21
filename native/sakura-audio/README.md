# sakura-audio

The real-time mix engine behind the framework's SDL audio backend. Hand-written C, one translation
unit, and the only link dependency is `libm`.

The managed side (`Sakura.Framework/Audio/SdlEngine`) decodes with FFmpeg, resamples to the device
format, and fills this library's ring buffers from an ordinary background thread. This library owns
everything that runs on the device callback: the mixer graph, per-voice rate conversion, gain and
pan, low-pass inserts, peak metering, and draining those ring buffers.

This got make as an experiment to tried to eliminate all the latency that created from the managed mixer.

## Not platform-specific, and it must stay that way

Two consequences worth keeping in view:

- The Darwin cache variables in `CMakeLists.txt` (universal `x86_64;arm64`,
  `CMAKE_OSX_DEPLOYMENT_TARGET`) apply to the Apple leg only. They are still required there — a
  silently single-arch or `minos`-too-high dylib fails at *load*, not at build — and they say nothing
  about the Windows, Linux or Android legs, which verify their arch in CI instead.
- Windows means MSVC, so the C stays portable in the boring sense: no GNU extensions, and the atomics
  story chosen deliberately rather than discovered. `sakura_atomic.h` is C11 `<stdatomic.h>` where it
  exists and MSVC `_Interlocked*` intrinsics where it does not, with every operation sequentially
  consistent so there is no per-site memory-order argument to get wrong on arm64.

## The SDL boundary

The library needs exactly one thing from SDL: `SDL_PutAudioStreamData`, to hand mixed frames to the
device from inside the callback. Rather than link SDL3 — which would mean SDL3 headers and import
libraries in CI for a dozen targets — it is injected:

```c
sakura_audio_set_sdl_put(sdlPutAudioStreamDataPointer);
void *callback = sakura_audio_get_stream_callback();
stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, callback, engine);
```

That collapses the CI story to "compile one C file per RID", and it removes any way for the SDL3 this
linked against to differ from the SDL3 the managed side loaded. **Any future need for another SDL
call should be injected the same way rather than adding a link dependency.**

## Real-time discipline

Non-negotiable for anything reachable from the audio callback. Every one of these is a latency or
stutter bug if broken:

- no `malloc`/`free` — every node, voice, ring and scratch buffer is allocated at create time, and
  the audio thread only ever marks things retired for `sakura_audio_engine_maintain` to reclaim
- no locks — SPSC rings with atomic positions, and control through a lock-free command queue
- no calls into managed code, and no function pointers that could resolve into managed code
- no logging, no `printf`, no allocation-shaped error handling
- no unbounded loops — the node pool, the graph depth and the commands drained per callback are all
  capped by a configured constant
- **no FFT.** Spectrum reads are pulled, not pushed: the callback keeps a rolling window of recent
  mixed output per node, and `sakura_audio_node_read_spectrum` transforms it on the *calling* thread.
  Peak levels, being trivial, are computed in the callback into atomics.

The one deliberate exception is `timespec_get`, used to measure callback duration. A callback whose
cost is not measured is a callback whose latency budget is guesswork; C11's `timespec_get` needs no
platform header, which is why it and not `clock_gettime` or `QueryPerformanceCounter`.

## Threading

| Thread | Enters through | Notes |
|---|---|---|
| control | create/destroy, parameters, transport | non-blocking; graph and transport changes post a command |
| writer | `sakura_audio_stream_*` | one per voice, wait-free against the audio thread |
| audio | the callback from `sakura_audio_get_stream_callback` | SDL's, and never anything else |
| readers | `..._get_state`, `..._read_spectrum`, `..._get_stats` | any thread, never blocks the audio thread |

The seek path is the only part that needs the three of them to cooperate. A discard cannot be done by
the writer — it does not know where the audio thread's read cursor is, and moving it under a callback
that is mid-copy is how a seek becomes a click — so `sakura_audio_stream_flush_begin` posts the
request, the audio thread performs it, and the writer waits on
`sakura_audio_stream_flush_pending` before writing audio from the new position. The wait is the
caller's to do: it is the one place this library would otherwise need a sleep primitive, and there
isn't a portable one.

## Relationship to the managed mixer

`Sakura.Framework/Audio/SdlEngine` contains a complete managed mixer, and it is not dead code. It is
the reference this implementation is diffed against, and the fallback when the native library is
missing for a platform — an `AudioBackend.SDL` that degrades to managed mixing is much better than
one that throws. Where behaviour is shared, the managed version is the source of truth:

- **Filter coefficients are computed managed-side.** This library only applies them, so the
  cutoff-to-coefficient maths has one home and one set of tests.
- **The pan law is computed managed-side**, next to the BASS backend's, and arrives here as two
  per-side gains.
- Unity playback is bit-exact, not merely close: cubic Hermite at t = 0 collapses to the sample
  itself, so normal playback is not quietly low-passed and needs no bypass path.
- The peak window is measured in audio, not in reads — 16 segments of 64 frames, about 21 ms at
  48 kHz, the same order as the fixed window `BASS_ChannelGetLevel` reports over. A peak computed per
  callback into an atomic has exactly the bug this avoids: a reader that lands between callbacks
  reads zero.

## Build

Requires CMake >= 3.22 and any C11 compiler.

```sh
cd native/sakura-audio
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
```

This produces `build/libsakura-audio.dylib` (macOS, universal), `libsakura-audio.so` (Linux,
Android), or `sakura-audio.dll` (Windows). Configure with `-DCMAKE_SYSTEM_NAME=iOS` for the iOS
`.framework`, which CI assembles into an `xcframework`.

> The `build/` directory is machine-specific CMake output and is git-ignored — never commit it.

## Test

`tests/sakura_audio_test.c` is a plain C program against the same source the library ships, with no
test framework, so it runs anywhere the library builds:

```sh
ctest --test-dir build --output-on-failure
```

Its numbers are not baselines captured from this implementation — they are what the managed reference
mixer produces for the same input, worked out from first principles. If the two disagree, one of them
is wrong, and that is the whole reason the managed mixer was written first.

## Where the library goes

The .NET side loads it by name (`DllImport("libsakura-audio")`), so the built artifact has to sit
where the framework's native loader finds it, alongside the other natives:

```
Sakura.Framework.NativeLibraries/runtimes/<rid>/native/libsakura-audio.{dylib,so}
Sakura.Framework.NativeLibraries/runtimes/ios/native/sakura-audio.xcframework
```

The matching P/Invoke binding is `SakuraAudioNative.cs`. When a signature changes here, that file and
`SAKURA_AUDIO_ABI_VERSION` change with it — managed checks the version before anything else, so a
shipped library that disagrees with the assembly says so instead of corrupting a struct.
