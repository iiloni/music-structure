import * as log from "../../dev/log";
import * as SSM from "../SSM";
import * as pathExtraction from "../pathExtraction";
import * as noveltyDetection from "../noveltyDetection";
import * as structure from "../structure";
import * as filter from "../filter";
import * as scapePlot from "../scapePlot";
import Matrix from "../dataStructures/Matrix";
import HalfMatrix from "../dataStructures/HalfMatrix";

addEventListener("message", (event) => {
    const data = event.data;
    const message = {};
    const matrixes = [];
    const graphs = [];

    const beatGraph = createBeatGraph(data, graphs);


    const { ssmPitch, ssmTimbre } = calculateSSM(data, 0.4);
    const ssmTimbrePitch = Matrix.combine(ssmPitch, ssmTimbre);
    matrixes.push({
        name: "Raw",
        buffer: ssmTimbrePitch.getBuffer(),
    });


    const ssmPitchSinglePitch = data.allPitches ? ssmPitch.getFirstFeatureMatrix() : ssmPitch;
    //matrixes.push({ name: "Pitch", buffer: ssmPitchSinglePitch.getBuffer() });
    const ssmPitchSinglePitchOffset1 = data.allPitches ? ssmPitch.getFeatureMatrix(1) : ssmPitch;
    //matrixes.push({ name: "Pitch Offset 1", buffer: ssmPitchSinglePitchOffset1.getBuffer() });

    //matrixes.push({ name: "Timbre", buffer: ssmTimbre.getBuffer() });

    // BLURRING
    const blurredTimbre = filter.gaussianBlur2DOptimized(ssmTimbre, 12);

    //matrixes.push({ name: "Blur T", buffer: blurredTimbre.getBuffer() });
    const blurredPitch = filter.gaussianBlur2DOptimized(ssmPitchSinglePitch, 12);
    //matrixes.push({ name: "Blur P", buffer: blurredPitch.getBuffer() });

    const timbreNoveltySmall = noveltyDetection.detect(ssmTimbre, 5);
    const timbreNoveltyMedium = noveltyDetection.detect(ssmTimbre, 20);
    const timbreNoveltyLarge = noveltyDetection.detect(ssmTimbre, 40);
    const pitchNoveltySmall = noveltyDetection.detect(ssmPitch, 5);
    const pitchNoveltyMedium = noveltyDetection.detect(ssmPitch, 20);
    const pitchNoveltyLarge = noveltyDetection.detect(ssmPitch, 40);
    graphs.push({
        name: "Timbre Novelty Blur Medium",
        buffer: timbreNoveltyMedium.buffer,
    });
    graphs.push({
        name: "Timbre Novelty Blur Large",
        buffer: timbreNoveltyLarge.buffer,
    });
    graphs.push({
        name: "Pitch Novelty Blur Medium",
        buffer: pitchNoveltyMedium.buffer,
    });
    graphs.push({
        name: "Pitch Novelty Blur Large",
        buffer: pitchNoveltyLarge.buffer,
    });

    const timbreNovelty = noveltyDetection.absoluteEuclideanColumnDerivative(ssmTimbre);
    graphs.push({ name: "Timbre Column Novelty", buffer: timbreNovelty.buffer });
    const blurredTimbreNovelty = noveltyDetection.absoluteEuclideanColumnDerivative(blurredTimbre);
    graphs.push({
        name: "Blur Timbre Column Novelty",
        buffer: blurredTimbreNovelty.buffer,
    });

    const pitchNovelty = noveltyDetection.absoluteEuclideanColumnDerivative(ssmPitchSinglePitch);
    graphs.push({ name: "Pitch Column Novelty", buffer: pitchNovelty.buffer });
    const blurredPitchNovelty = noveltyDetection.absoluteEuclideanColumnDerivative(blurredPitch);
    graphs.push({
        name: "Blur Pitch Column Novelty",
        buffer: blurredPitchNovelty.buffer,
    });

    // Enhance pitch SSM, diagonal smoothing, still contains 12 pitches
    let startTime = performance.now();
    const enhancedSSM = SSM.enhanceSSM(
        ssmPitch,
        { blurLength: data.enhanceBlurLength, tempoRatios: data.tempoRatios, strategy: 'linmed' },
        data.allPitches
    );
    matrixes.push({ name: "Enhanced", buffer: enhancedSSM.getBuffer() });
    log.debug("Enhance Time", performance.now() - startTime);




    // Make transposition invariant; take max of all pitches
    startTime = performance.now();
    let transpositionInvariant = SSM.makeTranspositionInvariant(enhancedSSM);
    log.debug("makeTranspositionInvariant Time", performance.now() - startTime);

    // Threshold the ssm to only show important paths
    startTime = performance.now();
    transpositionInvariant = SSM.rowColumnAutoThreshold(transpositionInvariant, data.thresholdPercentage);
    matrixes.push({
        name: "Transinv",
        buffer: transpositionInvariant.getBuffer(),
    });
    log.debug("autothreshold Time", performance.now() - startTime);


    
    /*const binaryTranspositionInvariant = SSM.binarize(transpositionInvariant, 0.2);
    matrixes.push({
        name: "Bin Transinv",
        buffer: binaryTranspositionInvariant.getBuffer(),
    });*/
    //showAllEnhancementMethods(ssmPitch, data, matrixes);

    const fullTranspositionInvariant = Matrix.fromHalfMatrix(transpositionInvariant);

    /*const longerDiagonalBlur = SSM.enhanceSSM(transpositionInvariant, {
        blurLength: 10,
        tempoRatios: data.tempoRatios,
    });*/
    const timeLagMatrix = Matrix.createTimeLagMatrix(fullTranspositionInvariant);
    matrixes.push({ name: "TL", buffer: timeLagMatrix.getBuffer() });

    const medianTimeLag = filter.median2D(timeLagMatrix, 32, 8, 2);
    matrixes.push({ name: "Med TL", buffer: medianTimeLag.getBuffer() });

    const priorLag = noveltyDetection.computePriorLagHalf(timeLagMatrix, 10);
    graphs.push({
        name: "Prior Lag Feature",
        buffer: priorLag.buffer,
    });
    const columnDensity = noveltyDetection.columnDensity(medianTimeLag);
    graphs.push({
        name: "Column Density",
        buffer: columnDensity.buffer,
    });
    const blurredBinaryTimeLagMatrix = filter.gaussianBlur2DOptimized(medianTimeLag, 2);
    matrixes.push({
        name: "Blur TL",
        buffer: blurredBinaryTimeLagMatrix.getBuffer(),
    });

    const structureFeatureNovelty = noveltyDetection.computeNoveltyFromTimeLag(blurredBinaryTimeLagMatrix);
    graphs.push({
        name: "Structure Feature Novelty",
        buffer: structureFeatureNovelty.buffer,
    });


    const normalizedMedianTimeLag = noveltyDetection.normalizeByColumnDensity(medianTimeLag);
    matrixes.push({
        name: "Norm TL",
        buffer: normalizedMedianTimeLag.getBuffer(),
    });
    const blurredBinaryTimeLagMatrixNorm = filter.gaussianBlur2DOptimized(normalizedMedianTimeLag, 2);
    matrixes.push({
        name: "Blur Norm TL",
        buffer: blurredBinaryTimeLagMatrixNorm.getBuffer(),
    });

    const structureFeatureNoveltyNorm = noveltyDetection.computeNoveltyFromTimeLag(blurredBinaryTimeLagMatrixNorm);
    graphs.push({
        name: "Structure Feature Novelty (Norm)",
        buffer: structureFeatureNoveltyNorm.buffer,
    });
    //const binaryTimeLagMatrix = SSM.binarize(medianTimeLag, 0.1);
    //matrixes.push({ name: "Bin TL", buffer: binaryTimeLagMatrix.getBuffer() });


    if (data.createScapePlot) {
        startTime = performance.now();
        const SP = scapePlot.create(fullTranspositionInvariant, data.sampleAmount, data.SPminSize, data.SPstepSize);
        log.debug("ScapePlot Time", performance.now() - startTime);

        // Anchorpoint selection for segment family similarity
        startTime = performance.now();
        const anchorNeighborhoodSize = 7 / data.SPstepSize;
        const anchorMinSize = Math.max(1, 7 - data.SPminSize);
        const { anchorPoints, anchorPointAmount } = scapePlot.sampleAnchorPointsMax(
            SP,
            250,
            anchorNeighborhoodSize,
            anchorMinSize,
            0.1
        );
        log.debug("anchorPoints Time", performance.now() - startTime);
        log.debug("AnchorPoint Amount: ", anchorPointAmount);

        // Optional visualization of anchorpoint locations
        //SP.multiply(0.9);
        for (let i = 0; i < anchorPointAmount; i++) {
            //SP.setValue(anchorPoints[i * 2], anchorPoints[i * 2 + 1], 1);
        }

        // Mapping colors by similarity to anchorpoints
        startTime = performance.now();
        const SPAnchorColor = scapePlot.mapColors(
            fullTranspositionInvariant,
            data.sampleAmount,
            data.SPminSize,
            data.SPstepSize,
            anchorPoints,
            anchorPointAmount
        );
        log.debug("colorMap Time", performance.now() - startTime);

        message.scapePlot = SP.getBuffer();
        message.scapePlotAnchorColor = SPAnchorColor.buffer;
    }

    const noveltyCombined = new Float32Array(timbreNoveltyMedium.length);
    for (let i = 0; i < noveltyCombined.length; i++) {
        noveltyCombined[i] = structureFeatureNoveltyNorm[i]; //+ timbreNoveltyMedium[i] * 2 + pitchNoveltyMedium[i] * 2
        //blurredPitchNovelty[i] * 0.5 +
        //blurredTimbreNovelty[i] * 0.5;
    }
    graphs.push({ name: "Combined Novelty", buffer: noveltyCombined.buffer });

    const smoothedCombined = filter.gaussianBlur1D(noveltyCombined, 1);
    graphs.push({
        name: "Smoothed Combined Novelty",
        buffer: smoothedCombined.buffer,
    });



    const structureSections = structure.createSectionsFromNovelty(smoothedCombined, data.sampleDuration);
    log.debug(structureSections);

    const structureCandidates = structure.computeStructureCandidates(transpositionInvariant, structureSections)
    const optimalStructure = structure.findOptimalDecomposition(structureCandidates);
    log.debug(optimalStructure)

    //visualizeKernel(data, matrixes);

    message.matrixes = matrixes;
    message.graphs = graphs;
    message.structureSections = structureSections;
    message.optimalStructure = optimalStructure;
    message.id = data.id;
    message.timestamp = new Date();

    postMessage(message);
});

export function timed(name, f) {
    const startTime = performance.now();
    const result = f();
    log.info(`${name} \t took`, Math.round(performance.now() - startTime));
    return result;
}

export function calculateSSM(data, threshold) {
    if (data.synthesized) {
        const ssmPitch = new HalfMatrix(data.synthesizedSSMPitch);
        const ssmTimbre = new HalfMatrix(data.synthesizedSSMTimbre);
        return { ssmPitch, ssmTimbre };
    } else {
        // Calculate raw SSM: pitchSSM with 12 pitches, timbreSSM
        const ssmPitch = timed("ssmPitch", () =>
            SSM.calculateSSM(data.pitchFeatures, data.sampleDuration, data.allPitches, threshold)
        );
        const ssmTimbre = timed("ssmTimbre", () =>
            SSM.calculateSSM(data.timbreFeatures, data.sampleDuration, false, threshold)
        );
        return { ssmPitch, ssmTimbre };
    }
}

export function createBeatGraph(data, graphs) {
    const beatAmount = data.beatsStartDuration.length;
    const beatDurationArray = new Float32Array(beatAmount);
    for (let i = 0; i < beatAmount; i++) {
        beatDurationArray[i] = data.beatsStartDuration[i][1];
    }
    const beatDurationGraph = {
        name: "Beat Duration",
        buffer: beatDurationArray.buffer,
        min: 0,
        max: 1,
    };
    graphs.push(beatDurationGraph);
    return beatDurationArray;
}

export function visualizeKernel(data, matrixes) {
    const kernel = noveltyDetection.createKernel(data.sampleAmount);
    const kernelMatrix = new HalfMatrix({
        size: data.sampleAmount,
        numberType: HalfMatrix.NumberType.FLOAT32,
    });
    kernelMatrix.data = kernel;
    kernelMatrix.normalize();
    matrixes.push({ name: "Kernel", buffer: kernelMatrix.getBuffer() });
}

export function showAllEnhancementMethods(ssmPitch, data, matrixes){
    const enhancedSSMLin = SSM.enhanceSSM(
        ssmPitch,
        { blurLength: data.enhanceBlurLength, tempoRatios: data.tempoRatios, strategy: 'linear' },
        data.allPitches
    );
    let transpositionInvariantLin = SSM.makeTranspositionInvariant(enhancedSSMLin);
    transpositionInvariantLin = SSM.rowColumnAutoThreshold(transpositionInvariantLin, data.thresholdPercentage);
    matrixes.push({ name: "transinv Lin", buffer: transpositionInvariantLin.getBuffer() });

    const enhancedSSMGauss = SSM.enhanceSSM(
        ssmPitch,
        { blurLength: data.enhanceBlurLength, tempoRatios: data.tempoRatios, strategy: 'gauss' },
        data.allPitches
    );
    let transpositionInvariantGauss = SSM.makeTranspositionInvariant(enhancedSSMGauss);
    transpositionInvariantGauss = SSM.rowColumnAutoThreshold(transpositionInvariantGauss, data.thresholdPercentage);
    matrixes.push({ name: "transinv Gauss", buffer: transpositionInvariantGauss.getBuffer() });

    const enhancedSSMOnedir = SSM.enhanceSSM(
        ssmPitch,
        { blurLength: data.enhanceBlurLength, tempoRatios: data.tempoRatios, strategy: 'onedir' },
        data.allPitches
    );
    let transpositionInvariantOnedir = SSM.makeTranspositionInvariant(enhancedSSMOnedir);
    transpositionInvariantOnedir = SSM.rowColumnAutoThreshold(transpositionInvariantOnedir, data.thresholdPercentage);
    matrixes.push({ name: "transinv Onedir", buffer: transpositionInvariantOnedir.getBuffer() });

    const enhancedSSMOnedirmed = SSM.enhanceSSM(
        ssmPitch,
        { blurLength: data.enhanceBlurLength, tempoRatios: data.tempoRatios, strategy: 'onedirmed' },
        data.allPitches
    );
    let transpositionInvariantOnedirmed = SSM.makeTranspositionInvariant(enhancedSSMOnedirmed);
    transpositionInvariantOnedirmed = SSM.rowColumnAutoThreshold(transpositionInvariantOnedirmed, data.thresholdPercentage);
    matrixes.push({ name: "transinv Onedirmed", buffer: transpositionInvariantOnedirmed.getBuffer() });

}