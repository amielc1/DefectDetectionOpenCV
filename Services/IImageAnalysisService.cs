using OpenCvSharp;
using NdtImageProcessor.Models;

namespace NdtImageProcessor.Services;

/// <summary>
/// Service for performing NDT image analysis using OpenCV.
/// </summary>
public interface IImageAnalysisService
{
    /// <summary>
    /// Calculates the histogram of a grayscale image.
    /// </summary>
    float[] CalculateHistogram(Mat image);

    /// <summary>
    /// Applies a Look-Up Table (LUT) to an image based on provided thresholds and colors.
    /// </summary>
    Mat ApplyLut(Mat originalImage, int thLow, int thHigh, Vec3b colLow, Vec3b colMid, Vec3b colHigh);

    /// <summary>
    /// Performs full defect analysis on the image.
    /// </summary>
    Task<AnalysisResult> AnalyzeAsync(
        Mat originalImage, 
        Mat processedImage, 
        int thLow, 
        int thHigh, 
        bool isLowRed, 
        bool isMidRed, 
        bool isHighRed, 
        List<Rect> selectedRois, 
        IProgress<(int value, string status)> progress);
}
