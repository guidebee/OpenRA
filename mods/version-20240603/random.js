// This is largely a reimplementation of a common C standard library random
// number generator, used by the likes of FreeBSD, GLIBC, etc... It is not
// exact - nor is it fully optimized.

// None of the underlying concepts used in this file are inventions of
// Ashley Newson.

// Citations:
// - https://github.com/freebsd/freebsd-src/blob/main/lib/libc/stdlib/random.c
// - https://sourceware.org/git/?p=glibc.git;a=blob;f=stdlib/random_r.c
// - https://www.mathstat.dal.ca/~selinger/random/

const UINT_MASK = 0xffffffff;

export default class Random {
    constructor(seed) {
        this.h = new Uint32Array(32);
        this.cr = 0;
        this.setSeed(seed);
    }

    getSeed() {
        return this.seed;
    }

    setSeed(seed) {
        if (seed === 0) {
            seed = (Math.random() * 0x100000000) & UINT_MASK;
        }
        this.seed = seed;

        const r = new Uint32Array(344);

        r[0] = seed;

        for (let i = 1; i < 31; i++) {
            r[i] = ((16807 * r[i-1]) % 2147483647) & UINT_MASK;
        }
        for (let i = 31; i < 34; i++) {
            r[i] = r[i-31];
        }
        for (let i = 34; i < 344; i++) {
            r[i] = (r[i-31] + r[i-3]) & UINT_MASK;
        }
        
        for (let i = 0; i < 32; i++) {
            this.h[i] = r[i+312];
        }
        this.cr = 0;
    }

    i32() {
        this.cr = (this.cr + 1) & 31;

        const lr = (this.cr + 1) & 31;

        const hr = (this.cr + 29) & 31;

        // This is a deviation from the normal way these RNGs work. Most similar
        // RNGs would instead discard the lowest bit before outputting, and only
        // output a u31. However, I'm just outputting the whole thing. For my
        // purposes, this is fine.
        return (this.h[this.cr] = (this.h[lr] + this.h[hr]) & UINT_MASK);
    }

    u32() {
        let n = this.i32();
        return n >= 0 ? n : (n + 0x100000000);
    }

    // This is a float with 32 bits of entropy, not actually a 32-bit float!
    // Returns a value between 0 and 1 inclusive.
    f32i() {
        return this.u32() / UINT_MASK;
    }

    // This is a float with 32 bits of entropy, not actually a 32-bit float!
    // Returns a value between 0 inclusive and 1 exclusive
    f32x() {
        return this.u32() / (0x100000000);
    }

    // Has biases for non-factors of 2^32
    pick(array) {
        return array[this.u32() % array.length];
    }

    // Has biases
    pickWeighted(array, weights) {
        const total = weights.reduce((acc, v) => (acc + v));
        const spin = this.f32x() * total;
        let i;
        let acc = 0;
        for (i = 0; i < weights.length; i++) {
            acc += weights[i];
            if (spin < acc) {
                return array[i];
            }
        }
        // This might be possible due to floating point precision loss
        // (in rare cases). Or we might have been given rubbish
        // weights. Return anything > 0.
        for (i = 0; i < weights.length; i++) {
            if (weights[i] > 0) {
                return array[i];
            }
        }
        // All <= 0!
        return this.pick(array);
    }

    // Has biases.
    shuffleInPlace(array, len) {
        len ??= array.length;
        for (let i = len; i > 1; i--) {
            const swap = this.u32() % i;
            const tmp = array[i-1];
            array[i-1] = array[swap];
            array[swap] = tmp;
        }
        return array;
    }
}
