// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#ifndef SAKURA_ATOMIC_H
#define SAKURA_ATOMIC_H

// The handful of atomic operations sakura-audio needs, wrapped so that the choice is made here
// rather than discovered on whichever platform happens to get built first.
//
// Two branches, split on the compiler rather than on what it happens to offer:
//
//   * MSVC gets the _Interlocked* intrinsics. Whether a given MSVC exposes <stdatomic.h> depends on
//     its version and on /experimental:c11atomics, and the intrinsics are always there and always
//     correct -- so the Windows leg does not get to vary with the toolchain the runner installed.
//   * everything else (clang, gcc, clang-cl) gets C11 <stdatomic.h>.
//
// Every operation here is sequentially consistent. That is stronger than most of the call sites
// need, and it is deliberate: the alternative is a per-site memory-order argument that has to be
// right on x86 (where almost anything works) *and* on arm64 (where it does not), for the sake of a
// few dozen operations per audio callback. The MSVC branch cannot express anything weaker than a
// full barrier through _Interlocked* anyway, so a relaxed variant would only be relaxed on half the
// platforms we ship — which is worse than not having one.
//
// Nothing in here allocates, locks, or calls out, so all of it is safe on the audio callback.

#include <stdint.h>
#include <string.h>

#if defined(_MSC_VER) && !defined(__clang__)
#define SAKURA_ATOMIC_MSVC 1
#elif defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L && !defined(__STDC_NO_ATOMICS__)
#define SAKURA_ATOMIC_C11 1
#else
#error "sakura-audio needs either C11 atomics or MSVC _Interlocked intrinsics."
#endif

#ifdef SAKURA_ATOMIC_C11

#include <stdatomic.h>

typedef struct { _Atomic uint32_t v; } sakura_atomic_u32;
typedef struct { _Atomic uint64_t v; } sakura_atomic_u64;

static inline uint32_t sakura_atomic_load_u32(const sakura_atomic_u32 *slot) { return atomic_load(&((sakura_atomic_u32 *)slot)->v); }
static inline void sakura_atomic_store_u32(sakura_atomic_u32 *slot, uint32_t value) { atomic_store(&slot->v, value); }
static inline uint32_t sakura_atomic_exchange_u32(sakura_atomic_u32 *slot, uint32_t value) { return atomic_exchange(&slot->v, value); }

// Returns the value the slot held, so a caller can tell success (previous == expected) from the
// value that beat it. Matches the shape of _InterlockedCompareExchange rather than C11's bool.
static inline uint32_t sakura_atomic_compare_exchange_u32(sakura_atomic_u32 *slot, uint32_t expected, uint32_t desired)
{
    uint32_t observed = expected;
    return atomic_compare_exchange_strong(&slot->v, &observed, desired) ? expected : observed;
}

static inline uint64_t sakura_atomic_load_u64(const sakura_atomic_u64 *slot) { return atomic_load(&((sakura_atomic_u64 *)slot)->v); }
static inline void sakura_atomic_store_u64(sakura_atomic_u64 *slot, uint64_t value) { atomic_store(&slot->v, value); }
static inline uint64_t sakura_atomic_fetch_add_u64(sakura_atomic_u64 *slot, uint64_t delta) { return atomic_fetch_add(&slot->v, delta); }

#else // SAKURA_ATOMIC_MSVC

#include <intrin.h>

typedef struct { volatile long v; } sakura_atomic_u32;
typedef struct { volatile long long v; } sakura_atomic_u64;

// _InterlockedOr with 0 is the portable MSVC spelling of a full-barrier load: unlike a volatile
// read it is ordered on arm64 as well as on x86.
static inline uint32_t sakura_atomic_load_u32(const sakura_atomic_u32 *slot) { return (uint32_t)_InterlockedOr((volatile long *)&slot->v, 0); }
static inline void sakura_atomic_store_u32(sakura_atomic_u32 *slot, uint32_t value) { _InterlockedExchange(&slot->v, (long)value); }
static inline uint32_t sakura_atomic_exchange_u32(sakura_atomic_u32 *slot, uint32_t value) { return (uint32_t)_InterlockedExchange(&slot->v, (long)value); }

static inline uint32_t sakura_atomic_compare_exchange_u32(sakura_atomic_u32 *slot, uint32_t expected, uint32_t desired)
{
    return (uint32_t)_InterlockedCompareExchange(&slot->v, (long)desired, (long)expected);
}

#if defined(_M_IX86)

// 32-bit x86 is the one MSVC target where _InterlockedCompareExchange64 is the *only* 64-bit
// interlocked intrinsic available -- there is no _InterlockedOr64, _InterlockedExchange64 or
// _InterlockedExchangeAdd64 -- so the other three are built out of it. Same semantics, a
// compare-and-swap loop where the other architectures get a single instruction.
//
// Not optional: ring positions, frame counters and epochs are all int64, so 64-bit atomicity is
// load-bearing on every target this library builds for, x86 included.

static inline uint64_t sakura_atomic_load_u64(const sakura_atomic_u64 *slot)
{
    // Comparing zero against zero reads the slot without changing it, which is the only atomic
    // 64-bit read x86 offers: a plain read of eight bytes can tear.
    return (uint64_t)_InterlockedCompareExchange64((volatile long long *)&slot->v, 0, 0);
}

static inline void sakura_atomic_store_u64(sakura_atomic_u64 *slot, uint64_t value)
{
    long long previous = (long long)sakura_atomic_load_u64(slot);

    for (;;)
    {
        long long observed = _InterlockedCompareExchange64(&slot->v, (long long)value, previous);

        if (observed == previous)
            return;

        previous = observed;
    }
}

static inline uint64_t sakura_atomic_fetch_add_u64(sakura_atomic_u64 *slot, uint64_t delta)
{
    long long previous = (long long)sakura_atomic_load_u64(slot);

    for (;;)
    {
        long long observed = _InterlockedCompareExchange64(&slot->v, previous + (long long)delta, previous);

        if (observed == previous)
            return (uint64_t)previous;

        previous = observed;
    }
}

#else

static inline uint64_t sakura_atomic_load_u64(const sakura_atomic_u64 *slot) { return (uint64_t)_InterlockedOr64((volatile long long *)&slot->v, 0); }
static inline void sakura_atomic_store_u64(sakura_atomic_u64 *slot, uint64_t value) { _InterlockedExchange64(&slot->v, (long long)value); }
static inline uint64_t sakura_atomic_fetch_add_u64(sakura_atomic_u64 *slot, uint64_t delta) { return (uint64_t)_InterlockedExchangeAdd64(&slot->v, (long long)delta); }

#endif

#endif

// Signed and floating-point accessors over the same storage. Audio state is naturally int64 frame
// counts and float levels, and bit-punning them through the integer primitives keeps the number of
// distinct atomic types at two.

static inline int64_t sakura_atomic_load_i64(const sakura_atomic_u64 *slot) { return (int64_t)sakura_atomic_load_u64(slot); }
static inline void sakura_atomic_store_i64(sakura_atomic_u64 *slot, int64_t value) { sakura_atomic_store_u64(slot, (uint64_t)value); }
static inline int64_t sakura_atomic_add_i64(sakura_atomic_u64 *slot, int64_t delta) { return (int64_t)sakura_atomic_fetch_add_u64(slot, (uint64_t)delta) + delta; }

static inline float sakura_atomic_load_f32(const sakura_atomic_u32 *slot)
{
    uint32_t bits = sakura_atomic_load_u32(slot);
    float value;
    memcpy(&value, &bits, sizeof(value));
    return value;
}

static inline void sakura_atomic_store_f32(sakura_atomic_u32 *slot, float value)
{
    uint32_t bits;
    memcpy(&bits, &value, sizeof(bits));
    sakura_atomic_store_u32(slot, bits);
}

static inline double sakura_atomic_load_f64(const sakura_atomic_u64 *slot)
{
    uint64_t bits = sakura_atomic_load_u64(slot);
    double value;
    memcpy(&value, &bits, sizeof(value));
    return value;
}

static inline void sakura_atomic_store_f64(sakura_atomic_u64 *slot, double value)
{
    uint64_t bits;
    memcpy(&bits, &value, sizeof(bits));
    sakura_atomic_store_u64(slot, bits);
}

#endif // SAKURA_ATOMIC_H
