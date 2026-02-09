using OpenCvSharp;
using NdtImageProcessor.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NdtImageProcessor.Services;

/// <summary>
/// Implementation of IImageAnalysisService using OpenCvSharp.
/// </summary>
public class ImageAnalysisService : IImageAnalysisService
{
    public float[] CalculateHistogram(Mat image)
    {
        if (image == null || image.IsDisposed) return new float[256];

        using Mat hist = new Mat();
        int[] channels = { 0 };
        int[] histSize = { 256 };
        Rangef[] ranges = { new Rangef(0, 256) };
        Cv2.CalcHist(new[] { image }, channels, null, hist, 1, histSize, ranges);

        float[] histData = new float[256];
        hist.GetArray(out histData);
        return histData;
    }

    public Mat ApplyLut(Mat originalImage, int thLow, int thHigh, Vec3b colLow, Vec3b colMid, Vec3b colHigh)
    {
        if (originalImage == null || originalImage.IsDisposed) return new Mat();

        using Mat lut = new Mat(1, 256, MatType.CV_8UC3);
        for (int i = 0; i < 256; i++)
        {
            if (i <= thLow)
                lut.Set(0, i, colLow);
            else if (i > thLow && i <= thHigh)
                lut.Set(0, i, colMid);
            else
                lut.Set(0, i, colHigh);
        }

        using Mat colorSrc = new Mat();
        Cv2.CvtColor(originalImage, colorSrc, ColorConversionCodes.GRAY2BGR);
        
        Mat processedImage = new Mat();
        Cv2.LUT(colorSrc, lut, processedImage);
        return processedImage;
    }

    public async Task<AnalysisResult> AnalyzeAsync(
        Mat originalImage, 
        Mat processedImage, 
        int thLow, 
        int thHigh, 
        bool isLowRed, 
        bool isMidRed, 
        bool isHighRed, 
        List<Rect> selectedRois, 
        IProgress<(int value, string status)> progress)
    {
        return await Task.Run(() => PerformAnalysis(originalImage, processedImage, thLow, thHigh, isLowRed, isMidRed, isHighRed, selectedRois, progress));
    }

    private AnalysisResult PerformAnalysis(
        Mat originalImage, 
        Mat processedImage, 
        int thLow, 
        int thHigh, 
        bool isLowRed, 
        bool isMidRed, 
        bool isHighRed, 
        List<Rect> selectedRois, 
        IProgress<(int value, string status)> progress)
    {
        progress?.Report((10, "Preprocessing image..."));
        using Mat defectMask = new Mat(originalImage.Size(), MatType.CV_8UC1, Scalar.Black);
        using Mat cleanedImage = new Mat();
        Cv2.MedianBlur(originalImage, cleanedImage, 5);

        progress?.Report((20, "Thresholding..."));
        using Mat tempMask = new Mat();
        if (isLowRed)
        {
            Cv2.InRange(cleanedImage, new Scalar(0), new Scalar(thLow), tempMask);
            Cv2.BitwiseOr(defectMask, tempMask, defectMask);
        }
        if (isMidRed)
        {
            Cv2.InRange(cleanedImage, new Scalar(thLow), new Scalar(thHigh), tempMask);
            Cv2.BitwiseOr(defectMask, tempMask, defectMask);
        }
        if (isHighRed)
        {
            Cv2.InRange(cleanedImage, new Scalar(thHigh), new Scalar(255), tempMask);
            Cv2.BitwiseOr(defectMask, tempMask, defectMask);
        }

        progress?.Report((40, "Morphological operations..."));
        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(11, 11));
        Cv2.MorphologyEx(defectMask, defectMask, MorphTypes.Close, kernel);

        if (selectedRois != null && selectedRois.Count > 0)
        {
            progress?.Report((50, "Applying ROI filtering..."));
            using Mat roiMask = new Mat(defectMask.Size(), MatType.CV_8UC1, Scalar.Black);
            foreach (var roi in selectedRois)
            {
                Cv2.Rectangle(roiMask, roi, Scalar.White, -1);
            }
            Cv2.BitwiseAnd(defectMask, roiMask, defectMask);
        }

        progress?.Report((60, "Finding contours..."));
        Cv2.FindContours(defectMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);

        progress?.Report((70, $"Found {contours.Length} potential defects. Filtering and annotating..."));
        Mat resultDisplay = processedImage.Clone();
        List<DefectItem> defectsList = new List<DefectItem>();
        int idCounter = 1;

        var filteredContours = contours.Where(c => Cv2.ContourArea(c) >= 10).ToList();
        int total = filteredContours.Count;

        for (int i = 0; i < total; i++)
        {
            var cnt = filteredContours[i];
            double area = Cv2.ContourArea(cnt);

            Cv2.DrawContours(resultDisplay, new[] { cnt }, -1, Scalar.Blue, 1);
            
            var rect = Cv2.BoundingRect(cnt);
            Cv2.PutText(resultDisplay, idCounter.ToString(), new Point(rect.X, rect.Y - 5), 
                HersheyFonts.HersheySimplex, 0.5, Scalar.Yellow, 1);

            defectsList.Add(new DefectItem(idCounter++, area, area > 500 ? "Reject" : "Warning"));
        }

        progress?.Report((100, "Analysis complete."));
        return new AnalysisResult(resultDisplay, defectsList);
    }
}
