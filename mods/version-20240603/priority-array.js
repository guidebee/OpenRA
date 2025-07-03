export default class PriorityArray {
    // If using a base, the base must not be changed during the
    // lifetime of the over instance.
    constructor(size) {
        this.size = size;
        this.priorities = new Float32Array(size).fill(1.0);
        this.indexToHeap = new Uint32Array(size);
        this.heapOfIndices = new Uint32Array(size);
        for (let i = 0; i < this.size; i++) {
            this.indexToHeap[i] = i;
            this.heapOfIndices[i] = i;
        }
    }

    fill(value) {
        this.priorities.fill(value);
        return this;
    }

    get length() {
        return this.size;
    }

    getMaxIndex() {
        return this.heapOfIndices[0];
    }

    get(i) {
        return this.priorities[i];
    }
    set(i, value) {
        this.priorities[i] = value;
        this.bubble(this.indexToHeap[i]);
    }
    swap(h1, h2, i1, i2) {
        i1 ??= this.heapOfIndices[h1];
        i2 ??= this.heapOfIndices[h2];
        // {
        //     const tmp = this.priorities[i1];
        //     this.priorities[i1] = this.priorities[i2];
        //     this.priorities[i2] = tmp;
        // }
        {
            const tmp = this.indexToHeap[i1];
            this.indexToHeap[i1] = this.indexToHeap[i2];
            this.indexToHeap[i2] = tmp;
        }
        {
            const tmp = this.heapOfIndices[h1];
            this.heapOfIndices[h1] = this.heapOfIndices[h2];
            this.heapOfIndices[h2] = tmp;
        }
    }
    bubble(h) {
        if (!this.bubbleUp(h)) {
            this.bubbleDown(h);
        }
    }
    bubbleUp(h) {
        if (h === 0) {
            return false;
        }
        const i = this.heapOfIndices[h];
        const upH = ((h+1)>>>1)-1;
        const upI = this.heapOfIndices[upH];
        if (this.priorities[i] <= this.priorities[upI]) {
            return false;
        }
        this.swap(h, upH, i, upI);
        this.bubbleUp(upH);
        return true;
    }
    bubbleDown(h) {
        const i = this.heapOfIndices[h];
        const downH1 = ((h+1)<<1)-1;
        const downH2 = downH1+1;
        if (downH1 >= this.size) {
            return false;
        }
        const v = this.priorities[i];
        if (downH2 < this.size) {
            const downI1 = this.heapOfIndices[downH1];
            const downI2 = this.heapOfIndices[downH2];
            const v1 = this.priorities[downI1];
            const v2 = this.priorities[downI2];
            if (v >= v1 && v >= v2) {
                return false;
            }
            if (v1 >= v2) {
                this.swap(h, downH1, i, downI1);
                this.bubbleDown(downH1);
            } else {
                this.swap(h, downH2, i, downI2);
                this.bubbleDown(downH2);
            }
            return true;
        } else {
            // Just the left down one exists.
            const downI1 = this.heapOfIndices[downH1];
            const v1 = this.priorities[downI1];
            if (v >= v1) {
                return false;
            }
            this.swap(h, downH1, i, downI1);
            this.bubbleDown(downH1);
            return true;
        }
    }
}

// test
{
    const expect = [
        [4, 8],
        [5, 7],
        [7, 6],
        [1, 5],
        [6, 4],
        [2, 3],
        [3, 2],
        [0, 1],
    ];
    const pa = new PriorityArray(8);
    pa.set(0, 1);
    pa.set(1, 5);
    pa.set(2, 3);
    pa.set(3, 2);
    pa.set(4, 8);
    pa.set(5, 7);
    pa.set(6, 4);
    pa.set(7, 6);
    {
        const readback = [];
        for (let i = 0; i < 8; i++) {
            const index = pa.getMaxIndex();
            readback.push([index, pa.get(index)]);
            pa.set(index, 0);
        }
        if (JSON.stringify(readback) !== JSON.stringify(expect)) {
            console.error(readback);
            console.error(expect);
            throw "fail1";
        }
    }
    for (let i = 0; i < 8; i++) {
        pa.set(i, 5);
    }
    pa.set(0, 1);
    pa.set(1, 5);
    pa.set(2, 3);
    pa.set(3, 2);
    pa.set(4, 8);
    pa.set(5, 7);
    pa.set(6, 4);
    pa.set(7, 6);
    {
        const readback = [];
        for (let i = 0; i < 8; i++) {
            const index = pa.getMaxIndex();
            readback.push([index, pa.get(index)]);
            pa.set(index, 0);
        }
        if (JSON.stringify(readback) !== JSON.stringify(expect)) {
            console.error(readback);
            console.error(expect);
            throw "fail2";
        }
    }
}
