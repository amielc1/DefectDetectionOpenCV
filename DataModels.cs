

using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Series;
using OpenCvSharp;
using System.Collections.Generic;

namespace DefectDetectionDemo.Models
{
    // Mocking the original DefectItem
    public partial class DefectItem : ObservableObject
    {
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool isVisible;
        public string Name { get; set; }
        public string Layer { get; set; }
        public string GeneratedBy { get; set; }
        public double Area { get; set; }
        public double PhiPos { get; set; }
        public double ZPos { get; set; }
        public List<DataPoint> Points { get; set; }
        public DefectType Type { get; set; }
    }

    public enum DefectType { Rectangle, Circle, Polygon }

    // Mocking the original HistogramDomain
    public partial class HistogramDomain : ObservableObject
    {
        [ObservableProperty] private double start;
        [ObservableProperty] private double end;
        [ObservableProperty] private double color;
        [ObservableProperty] private int index;
        
        public HistogramDomain(double start, double end, double color)
        {
            Start = start;
            End = end;
            Color = color;
        }
    }

    // A wrapper to replace the custom 'Image' class from your original code
    public class AnalysisImage
    {
        public double[,] Data { get; set; }
        public ImageParameters ImageParameters { get; set; }
        
        // Helper specifically for the demo to hold the Bitmap for display
        public System.Windows.Media.Imaging.BitmapSource DisplayBitmap { get; set; } 

        public AnalysisImage(ImageParameters @params, double[,] data)
        {
            ImageParameters = @params;
            Data = data;
        }
    }

    public class ImageParameters
    {
        public double ResolutionX { get; set; } = 1.0;
        public double ResolutionY { get; set; } = 1.0;
    }
}