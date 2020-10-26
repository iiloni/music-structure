import assert from "assert";
import { NumberType, getNumberTypeByName } from "./NumberType";
import * as log from "../../dev/log";

export default class HalfMatrix {
    static get NumberType() {
        return NumberType;
    }

    data;
    size;
    featureAmount;
    numberType;
    length;

    /**
     *
     * @param {*} size
     * @param {*} shape array of nested array sizes eg, ssm with 2 values per cell [2]
     * @param {*} type
     */
    constructor(options) {
        this.size = options.size;
        this.featureAmount = options.featureAmount || 1;
        this.numberType = getNumberTypeByName(options.numberType) || NumberType.FLOAT32;
        this.sampleDuration = options.sampleDuration || 1;
        this.length = ((this.size * this.size + this.size) / 2) * this.featureAmount;
        if (options.buffer) {
            this.data = new this.numberType.array(options.buffer);
            assert(this.length === this.data.length);
        } else {
            this.data = new this.numberType.array(this.length);
        }
    }

    static from(matrix, options) {
        if (!options) options = {};
        const featureAmount = options.featureAmount || matrix.featureAmount;
        const numberType = options.numberType || matrix.numberType;
        const sampleDuration = options.sampleDuration || matrix.sampleDuration;
        return new HalfMatrix({ size: matrix.size, featureAmount, numberType, sampleDuration });
    }

    getCellFromIndex(index) {}

    hasCell(x, y) {
        return x <= y && y < this.size && x >= 0 && y >= 0;
    }
    setValue(x, y, value) {
        this.data[((y * y + y) / 2) * this.featureAmount + x * this.featureAmount] = value;
    }

    getValue(x, y, f = 0) {
        return this.data[((y * y + y) / 2) * this.featureAmount + x * this.featureAmount + f];
    }
    getValues(x, y) {
        const values = new this.numberType.array(this.featureAmount);
        for (let o = 0; o < this.featureAmount; o++) {
            values[o] = this.data[((y * y + y) / 2) * this.featureAmount + x * this.featureAmount + o];
        }
        return values;
    }
    getValueMirrored(x, y) {
        if (x > y) {
            return this.data[((x * x + x) / 2) * this.featureAmount + y * this.featureAmount];
        } else {
            return this.data[((y * y + y) / 2) * this.featureAmount + x * this.featureAmount];
        }
    }
    getValuesNormalized(x, y) {
        const values = new this.numberType.array(this.featureAmount);
        for (let o = 0; o < this.featureAmount; o++) {
            values[o] =
                this.data[((y * y + y) / 2) * this.featureAmount + x * this.featureAmount + o] / this.numberType.scale;
        }
        return values;
    }
    getValueNormalized(x, y) {
        return this.data[((y * y + y) / 2) * this.featureAmount + x * this.featureAmount] / this.numberType.scale;
    }
    getValueNormalizedMirrored(x, y) {
        if (x > y) {
            return this.data[((x * x + x) / 2) * this.featureAmount + y * this.featureAmount] / this.numberType.scale;
        } else {
            return this.data[((y * y + y) / 2) * this.featureAmount + x * this.featureAmount] / this.numberType.scale;
        }
    }

    fillByIndex(callback) {
        let i = this.length;
        while (i--) {
            this.data[i] = callback(i);
        }
    }

    /**
     *
     * @param {*} callback dunction that returns number (compatible with DataType)
     */
    fill(callback) {
        for (let y = 0; y < this.size; y++) {
            const cellsBefore = ((y * y + y) / 2) * this.featureAmount;
            for (let x = 0; x < y + 1; x++) {
                this.data[cellsBefore + x * this.featureAmount] = callback(x, y);
            }
        }
    }

    fillFeatures(callback) {
        for (let y = 0; y < this.size; y++) {
            const cellsBefore = ((y * y + y) / 2) * this.featureAmount;
            for (let x = 0; x < y + 1; x++) {
                for (let f = 0; f < this.featureAmount; f++) {
                    this.data[cellsBefore + x * this.featureAmount + f] = callback(x, y, f);
                }
            }
        }
    }

    forEach(callback) {
        let i = this.length;
        while (i--) {
            callback(this.data[i], i);
        }
    }

    forEachCell(callback) {
        for (let y = 0; y < this.size; y++) {
            const cellsBefore = ((y * y + y) / 2) * this.featureAmount;

            for (let x = 0; x < y + 1; x++) {
                callback(x, y, this.data[cellsBefore + x]);
            }
        }
    }

    /**
     * Expect normalized number [0,1] and stores this as number of specified type, with 1 being scaled to the number's max
     * @param {*} callback function that returns number in range 0,1
     */
    fillNormalized(callback) {
        for (let y = 0; y < this.size; y++) {
            const cellsBefore = (y * y + y) / 2;
            for (let x = 0; x < y + 1; x++) {
                this.data[cellsBefore + x] = callback(x, y) * this.numberType.scale;
            }
        }
    }

    fillFeaturesNormalized(callback) {
        for (let y = 0; y < this.size; y++) {
            const cellsBefore = ((y * y + y) / 2) * this.featureAmount;
            for (let x = 0; x < y + 1; x++) {
                for (let f = 0; f < this.featureAmount; f++) {
                    this.data[cellsBefore + x * this.featureAmount + f] = callback(x, y, f) * this.numberType.scale;
                }
            }
        }
    }

    map(callback) {
        let i = this.length;
        while (i--) {
            this.data[i] = callback(this.data[i]);
        }
    }

    divide(number) {
        let i = this.length;
        while (i--) {
            this.data[i] /= number;
        }
    }
    multiply(number) {
        let i = this.length;
        while (i--) {
            this.data[i] *= number;
        }
    }
    add(number) {
        let i = this.length;
        while (i--) {
            this.data[i] += number;
        }
    }

    getBuffer() {
        return {
            buffer: this.data.buffer,
            size: this.size,
            numberType: this.numberType.name,
            featureAmount: this.featureAmount,
            sampleDuration: this.sampleDuration,
        };
    }

    getNestedArray() {
        const nestedArray = new Array(this.size);
        for (let y = 0; y < this.size; y++) {
            nestedArray[y] = new Array(this.size);
            for (let x = 0; x < this.size; x++) {
                nestedArray[y][x] = this.getValueMirrored(x, y);
            }
        }

        return nestedArray;
    }
}