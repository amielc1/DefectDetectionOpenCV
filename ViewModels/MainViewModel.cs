using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NdtImageProcessor.Models;
using NdtImageProcessor.Services;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace NdtImageProcessor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IImageAnalysisService _analysisService;
    private Mat? _originalImage;
    private Mat? _processedImage;
    private PlotModel _plotModel;

    [ObservableProperty]
    private bool _isImageLoaded;

    [ObservableProperty]
    private double _sliderLow = 50;

    [ObservableProperty]
    private double _sliderHigh = 200;

    [ObservableProperty]
    private int _comboColorLowIndex = 0;

    [ObservableProperty]
    private int _comboColorMidIndex = 2;

    [ObservableProperty]
    private int _comboColorHighIndex = 0;

    [ObservableProperty]
    private bool _roiEnabled;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private double _analysisProgress;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ObservableCollection<DefectItem> _defects = new();

    public PlotModel PlotModel => _plotModel;

    public MainViewModel(IImageAnalysisService analysisService)
    {
        _analysisService = analysisService;
        _plotModel = CreatePlotModel();
    }

    private PlotModel CreatePlotModel()
    {
        var model = new PlotModel { Title = "" };
        model.PlotAreaBorderThickness = new OxyThickness(0);
        model.PlotMargins = new OxyThickness(0);
        
        model.Axes.Add(new LinearAxis 
        { 
            Position = AxisPosition.Bottom, 
            IsAxisVisible = false,
            MinimumPadding = 0,
            MaximumPadding = 0
        });
        model.Axes.Add(new LinearAxis 
        { 
            Position = AxisPosition.Left, 
            IsAxisVisible = false,
            StartPosition = 1, 
            EndPosition = 0,
            MinimumPadding = 0,
            MaximumPadding = 0
        });
        return model;
    }

    [RelayCommand]
    private void LoadImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.bmp;*.tif"
        };
        if (dlg.ShowDialog() == true)
        {
            _originalImage?.Dispose();
            _originalImage = Cv2.ImRead(dlg.FileName, ImreadModes.Grayscale);
            IsImageLoaded = true;
            UpdateLut();
        }
    }

    [RelayCommand]
    private void UpdateLut()
    {
        if (_originalImage == null) return;

        var colLow = GetColorFromIndex(ComboColorLowIndex);
        var colMid = GetColorFromIndex(ComboColorMidIndex);
        var colHigh = GetColorFromIndex(ComboColorHighIndex);

        _processedImage?.Dispose();
        _processedImage = _analysisService.ApplyLut(_originalImage, (int)SliderLow, (int)SliderHigh, colLow, colMid, colHigh);

        UpdatePlotImage(_processedImage);
    }

    private void UpdatePlotImage(Mat image)
    {
        Cv2.ImEncode(".png", image, out byte[] bytes);
        var oxyImage = new OxyImage(bytes);

        _plotModel.Annotations.Clear();
        
        double centerX = image.Width / 2.0;
        double centerY = image.Height / 2.0;

        _plotModel.Annotations.Add(new ImageAnnotation
        {
            ImageSource = oxyImage,
            X = new PlotLength(centerX, PlotLengthUnit.Data),
            Y = new PlotLength(centerY, PlotLengthUnit.Data),
            Width = new PlotLength(image.Width, PlotLengthUnit.Data),
            Height = new PlotLength(image.Height, PlotLengthUnit.Data),
            HorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
            VerticalAlignment = OxyPlot.VerticalAlignment.Middle
        });

        _plotModel.Axes[0].Minimum = 0;
        _plotModel.Axes[0].Maximum = image.Width;
        _plotModel.Axes[1].Minimum = 0;
        _plotModel.Axes[1].Maximum = image.Height;

        _plotModel.InvalidatePlot(true);
    }

    private Vec3b GetColorFromIndex(int index)
    {
        return index switch
        {
            0 => new Vec3b(0, 0, 0),       // Black
            1 => new Vec3b(0, 0, 255),     // Red
            2 => new Vec3b(0, 255, 0),     // Green
            _ => new Vec3b(128, 128, 128)  // Gray
        };
    }

    [RelayCommand]
    private async Task Analyze(List<Rect> rois)
    {
        if (_originalImage == null || _processedImage == null) return;

        IsAnalyzing = true;
        AnalysisProgress = 0;
        StatusText = "Starting analysis...";
        Defects.Clear();

        var progress = new Progress<(int value, string status)>(p =>
        {
            AnalysisProgress = p.value;
            StatusText = p.status;
        });

        try
        {
            var result = await _analysisService.AnalyzeAsync(
                _originalImage, 
                _processedImage, 
                (int)SliderLow, 
                (int)SliderHigh, 
                ComboColorLowIndex == 1, 
                ComboColorMidIndex == 1, 
                ComboColorHighIndex == 1, 
                rois, 
                progress);

            UpdatePlotImage(result.DisplayImage);
            foreach (var defect in result.Defects)
            {
                Defects.Add(defect);
            }
            
            MessageBox.Show($"Analysis Complete. Found {result.Defects.Count} defects.", "NDT Result");
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Analysis failed: {ex.Message}", "Error");
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    partial void OnSliderLowChanged(double value) => UpdateLut();
    partial void OnSliderHighChanged(double value) => UpdateLut();
    partial void OnComboColorLowIndexChanged(int value) => UpdateLut();
    partial void OnComboColorMidIndexChanged(int value) => UpdateLut();
    partial void OnComboColorHighIndexChanged(int value) => UpdateLut();
}
