import AppKit
import Foundation
import Vision

guard CommandLine.arguments.count >= 2 else {
    fputs("Usage: macos-vision-ocr.swift <image-path>\n", stderr)
    exit(2)
}

let imagePath = CommandLine.arguments[1]
let imageUrl = URL(fileURLWithPath: imagePath)

guard let image = NSImage(contentsOf: imageUrl) else {
    fputs("Could not open image.\n", stderr)
    exit(3)
}

var proposedRect = CGRect(origin: .zero, size: image.size)
guard let cgImage = image.cgImage(forProposedRect: &proposedRect, context: nil, hints: nil) else {
    fputs("Could not create CGImage.\n", stderr)
    exit(4)
}

var recognizedLines: [String] = []
let request = VNRecognizeTextRequest { request, error in
    if let error {
        fputs(error.localizedDescription + "\n", stderr)
        return
    }

    let observations = request.results as? [VNRecognizedTextObservation] ?? []
    recognizedLines = observations.compactMap { observation in
        observation.topCandidates(1).first?.string
    }
}

request.recognitionLevel = .accurate
request.usesLanguageCorrection = true
request.recognitionLanguages = ["pt-BR", "en-US"]

let handler = VNImageRequestHandler(cgImage: cgImage, options: [:])

do {
    try handler.perform([request])
    print(recognizedLines.joined(separator: "\n"))
} catch {
    fputs(error.localizedDescription + "\n", stderr)
    exit(5)
}
