using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefectDetectionDemo.Logic;
using DefectDetectionDemo.Models;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Windows.Media.Imaging;

namespace DefectDetectionDemo.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private DefectDetectModel defectModel;
        [ObservableProperty] private BitmapSource currentImageDisplay;
        
        // Needed for DataGrid Binding
        [ObservableProperty] private HistogramDomain selectedDomain; 

        public MainViewModel()
        {
            DefectModel = new DefectDetectModel(OnImageProcessed);
        }

        private void OnImageProcessed(AnalysisImage processedImage)
        {
            CurrentImageDisplay = processedImage.DisplayBitmap;
        }

        [RelayCommand]
        public void LoadImage()
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.bmp" };
            if (dlg.ShowDialog() == true)
            {
                // Convert Image file to Double[,] for the algorithm
                using var mat = Cv2.ImRead(dlg.FileName, ImreadModes.Grayscale);
                int rows = mat.Rows;
                int cols = mat.Cols;
                double[,] data = new double[rows, cols];
                
                // Copy data safely
                for(int i=0; i<rows; i++)
                   for(int j=0; j<cols; j++)
                       data[i,j] = mat.At<byte>(i,j);

                var analysisImage = new AnalysisImage(new ImageParameters(), data);
                // Set initial display
                analysisImage.DisplayBitmap = mat.ToBitmapSource();
                
                CurrentImageDisplay = analysisImage.DisplayBitmap;
                DefectModel.InsertImage(analysisImage);
            }
        }

        [RelayCommand]
        public void AddCursor() => DefectModel.AddCursor();

        [RelayCommand]
        public void ApplyColors() => DefectModel.CreateDomainColoredImage();

        [RelayCommand]
        public void Detect() => DefectModel.DetectFlaws();
    }
}