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
// The plain accessors are sequentially consistent, and they are the default: a per-site memory-order
// argument has to be right on x86 (where almost anything works) *and* on arm64 (where it does not).
//
// The _acquire / _release / _relaxed variants exist for the few places where the protocol is a single
// writer handing data to a single reader -- the SPSC ring's positions and the slot-state publication --
// and the argument is the textbook one. Each call site says which half of the pairing it is. On MSVC
// they are implemented exactly the way MSVC's own <atomic> implements those orders, so they are as
// weak there as on the C11 compilers, rather than quietly a full barrier on half the platforms.
//
// A seq-cst *load* on MSVC is a plain load followed by a barrier, again as MSVC's <atomic> does it. It
// used to be _InterlockedOr(slot, 0), which is a locked read-modify-write: it takes the cache line
// exclusive, so every reader on another thread -- the 1000 Hz manager polling state, the decode
// thread checking ring space -- stole the line from the device callback that owns it, on every read.
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

static inline uint64_t sakura_atomic_compare_exchange_u64(sakura_atomic_u64 *slot, uint64_t expected, uint64_t desired)
{
    uint64_t observed = expected;
    return atomic_compare_exchange_strong(&slot->v, &observed, desired) ? expected : observed;
}

static inline uint32_t sakura_atomic_load_u32_acquire(const sakura_atomic_u32 *slot) { return atomic_load_explicit(&((sakura_atomic_u32 *)slot)->v, memory_order_acquire); }
static inline void sakura_atomic_store_u32_release(sakura_atomic_u32 *slot, uint32_t value) { atomic_store_explicit(&slot->v, value, memory_order_release); }

static inline uint64_t sakura_atomic_load_u64_acquire(const sakura_atomic_u64 *slot) { return atomic_load_explicit(&((sakura_atomic_u64 *)slot)->v, memory_order_acquire); }
static inline uint64_t sakura_atomic_load_u64_relaxed(const sakura_atomic_u64 *slot) { return atomic_load_explicit(&((sakura_atomic_u64 *)slot)->v, memory_order_relaxed); }
static inline void sakura_atomic_store_u64_release(sakura_atomic_u64 *slot, uint64_t value) { atomic_store_explicit(&slot->v, value, memory_order_release); }

#else // SAKURA_ATOMIC_MSVC

#include <intrin.h>

typedef struct { volatile long v; } sakura_atomic_u32;
typedef struct { volatile long long v; } sakura_atomic_u64;

// The barrier MSVC's <atomic> puts after an acquire (or seq-cst) load and before a release store. On
// x86 and x64 the hardware already orders those, so it only has to stop the compiler; the arm targets
// need the real thing. The condition is the one MSVC's own xatomic.h uses.
#if defined(_M_ARM) || defined(_M_ARM64) || defined(_M_ARM64EC) || defined(_M_HYBRID_X86_ARM64)
#define SAKURA_ATOMIC_BARRIER() __dmb(0xB) // ISH: inner-shareable data memory barrier
#else
#define SAKURA_ATOMIC_BARRIER() _ReadWriteBarrier()
#endif

// Seq-cst stores stay _InterlockedExchange (xchg on x86, a barrier-wrapped exclusive on arm64), which
// is what makes a plain-load-plus-barrier seq-cst load correct: the C11 mapping on both families.
static inline uint32_t sakura_atomic_load_u32(const sakura_atomic_u32 *slot)
{
    uint32_t value = (uint32_t)__iso_volatile_load32((const volatile __int32 *)&slot->v);
    SAKURA_ATOMIC_BARRIER();
    return value;
}

static inline void sakura_atomic_store_u32(sakura_atomic_u32 *slot, uint32_t value) { _InterlockedExchange(&slot->v, (long)value); }
static inline uint32_t sakura_atomic_exchange_u32(sakura_atomic_u32 *slot, uint32_t value) { return (uint32_t)_InterlockedExchange(&slot->v, (long)value); }

static inline uint32_t sakura_atomic_compare_exchange_u32(sakura_atomic_u32 *slot, uint32_t expected, uint32_t desired)
{
    return (uint32_t)_InterlockedCompareExchange(&slot->v, (long)desired, (long)expected);
}

static inline uint32_t sakura_atomic_load_u32_acquire(const sakura_atomic_u32 *slot) { return sakura_atomic_load_u32(slot); }

static inline void sakura_atomic_store_u32_release(sakura_atomic_u32 *slot, uint32_t value)
{
    SAKURA_ATOMIC_BARRIER();
    __iso_volatile_store32((volatile __int32 *)&slot->v, (__int32)value);
}

static inline uint64_t sakura_atomic_compare_exchange_u64(sakura_atomic_u64 *slot, uint64_t expected, uint64_t desired)
{
    return (uint64_t)_InterlockedCompareExchange64(&slot->v, (long long)desired, (long long)expected);
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

// No weaker 64-bit spelling exists here, so the ordered variants are the seq-cst ones. Stronger than
// asked is always correct; this is the one target where it is also the only option.
static inline uint64_t sakura_atomic_load_u64_acquire(const sakura_atomic_u64 *slot) { return sakura_atomic_load_u64(slot); }
static inline uint64_t sakura_atomic_load_u64_relaxed(const sakura_atomic_u64 *slot) { return sakura_atomic_load_u64(slot); }
static inline void sakura_atomic_store_u64_release(sakura_atomic_u64 *slot, uint64_t value) { sakura_atomic_store_u64(slot, value); }

#else

static inline uint64_t sakura_atomic_load_u64(const sakura_atomic_u64 *slot)
{
    uint64_t value = (uint64_t)__iso_volatile_load64((const volatile __int64 *)&slot->v);
    SAKURA_ATOMIC_BARRIER();
    return value;
}

static inline void sakura_atomic_store_u64(sakura_atomic_u64 *slot, uint64_t value) { _InterlockedExchange64(&slot->v, (long long)value); }
static inline uint64_t sakura_atomic_fetch_add_u64(sakura_atomic_u64 *slot, uint64_t delta) { return (uint64_t)_InterlockedExchangeAdd64(&slot->v, (long long)delta); }

static inline uint64_t sakura_atomic_load_u64_acquire(const sakura_atomic_u64 *slot) { return sakura_atomic_load_u64(slot); }
static inline uint64_t sakura_atomic_load_u64_relaxed(const sakura_atomic_u64 *slot) { return (uint64_t)__iso_volatile_load64((const volatile __int64 *)&slot->v); }

static inline void sakura_atomic_store_u64_release(sakura_atomic_u64 *slot, uint64_t value)
{
    SAKURA_ATOMIC_BARRIER();
    __iso_volatile_store64((volatile __int64 *)&slot->v, (__int64)value);
}

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
