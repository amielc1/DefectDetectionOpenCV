using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

using OpenCvSharp;
using OpenCvSharp.WpfExtensions; // חשוב להמרות
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace NdtImageProcessor
{
    // מחלקה לייצוג שורה בטבלה
    public class DefectItem
    {
        public int Id { get; set; }
        public double Area { get; set; }
        public string Status { get; set; } // Reject / Warning
    }

    public partial class MainWindow : System.Windows.Window
    {
        private Mat _originalImage; // התמונה המקורית (שחור לבן)
        private Mat _processedImage; // התמונה לתצוגה (צבעונית אחרי LUT)
        private bool _isImageLoaded = false;
        private PlotModel _plotModel;

        public MainWindow()
        {
            InitializeComponent();
            InitializePlot();
        }

        private void InitializePlot()
        {
            _plotModel = new PlotModel { Title = "" };
            _plotModel.PlotAreaBorderThickness = new OxyThickness(0);
            _plotModel.PlotMargins = new OxyThickness(0); // Ensure no margins around the plot
            
            // Remove axes for image display look
            _plotModel.Axes.Add(new LinearAxis 
            { 
                Position = AxisPosition.Bottom, 
                IsAxisVisible = false,
                MinimumPadding = 0,
                MaximumPadding = 0
            });
            _plotModel.Axes.Add(new LinearAxis 
            { 
                Position = AxisPosition.Left, 
                IsAxisVisible = false,
                StartPosition = 1, 
                EndPosition = 0,
                MinimumPadding = 0,
                MaximumPadding = 0
            });

            PlotView.Model = _plotModel;
        }

        // --- 1. טעינת תמונה וחישוב היסטוגרמה ---
        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "Image Files|*.png;*.jpg;*.bmp;*.tif";
            if (dlg.ShowDialog() == true)
            {
                // טעינה כ-Grayscale (קריטי ל-NDT)
                _originalImage = Cv2.ImRead(dlg.FileName, ImreadModes.Grayscale);
                _isImageLoaded = true;

                BtnClearRoi_Click(null, null);
                DrawHistogram();
                ApplyLut(); // החלת הצבעים הראשונית
            }
        }

        // --- 2. ציור היסטוגרמה ---
        private void DrawHistogram()
        {
            if (!_isImageLoaded) return;

            HistCanvas.Children.Clear();

            // חישוב היסטוגרמה ב-OpenCV
            Mat hist = new Mat();
            int[] channels = { 0 };
            int[] histSize = { 256 };
            Rangef[] ranges = { new Rangef(0, 256) };
            Cv2.CalcHist(new[] { _originalImage }, channels, null, hist, 1, histSize, ranges);

            // נרמול הגובה לגובה הקנבס
            double minVal, maxVal;
            Cv2.MinMaxLoc(hist, out minVal, out maxVal);
            float[] histData = new float[256];
            hist.GetArray(out histData);

            double canvasWidth = HistCanvas.ActualWidth;
            double canvasHeight = HistCanvas.ActualHeight;
            double barWidth = canvasWidth / 256;

            // ציור קווים עבור כל bin
            for (int i = 0; i < 256; i++)
            {
                double normalizedHeight = (histData[i] / maxVal) * canvasHeight;

                Line line = new Line
                {
                    X1 = i * barWidth,
                    Y1 = canvasHeight,
                    X2 = i * barWidth,
                    Y2 = canvasHeight - normalizedHeight,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1
                };
                HistCanvas.Children.Add(line);
            }
        }

        // טיפול בשינוי גודל חלון כדי לצייר מחדש היסטוגרמה
        private void HistCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isImageLoaded) DrawHistogram();
        }

        // --- 3. לוגיקה של LUT וספים (Thresholds) ---
        
        private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // --- תיקון: בדיקת תקינות ---
            // WPF מפעיל את האירוע הזה בזמן בניית החלון.
            // אם הסליידר השני עדיין לא נוצר, הוא יהיה null ואנחנו חייבים לעצור.
            if (SliderLow == null || SliderHigh == null) return;

            // מניעת חפיפה לוגית של הסליידרים
            if (SliderLow.Value > SliderHigh.Value)
            {
                if (sender == SliderLow) 
                {
                    SliderHigh.Value = SliderLow.Value;
                }
                else 
                {
                    SliderLow.Value = SliderHigh.Value;
                }
            }

            ApplyLut();
        }

        private void OnLutChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyLut();
        }

        private void ApplyLut()
        {
            if (!_isImageLoaded) return;

            int thLow = (int)SliderLow.Value;
            int thHigh = (int)SliderHigh.Value;

            // יצירת טבלת המרה צבעונית (3 ערוצים)
            // Mat בגודל 1x256, מסוג Vec3b (צבע)
            Mat lut = new Mat(1, 256, MatType.CV_8UC3);
            
            // השגת הצבעים שנבחרו ב-ComboBox
            Vec3b colLow = GetColorFromCombo(ComboColorLow);
            Vec3b colMid = GetColorFromCombo(ComboColorMid);
            Vec3b colHigh = GetColorFromCombo(ComboColorHigh);

            // מילוי הטבלה לפי הסליידרים
            for (int i = 0; i < 256; i++)
            {
                if (i <= thLow)
                    lut.Set(0, i, colLow);
                else if (i > thLow && i <= thHigh)
                    lut.Set(0, i, colMid);
                else
                    lut.Set(0, i, colHigh);
            }

            // המרה לצבע כדי שנוכל להחיל LUT צבעוני
            Mat colorSrc = new Mat();
            Cv2.CvtColor(_originalImage, colorSrc, ColorConversionCodes.GRAY2BGR);
            
            _processedImage = new Mat();
            Cv2.LUT(colorSrc, lut, _processedImage);

            // המרה לתצוגה ב-OxyPlot
            UpdatePlotImage(_processedImage);
        }

        private void UpdatePlotImage(Mat image)
        {
            // Convert Mat to byte array (PNG)
            Cv2.ImEncode(".png", image, out byte[] bytes);
            var oxyImage = new OxyImage(bytes);

            _plotModel.Annotations.Clear();
            
            // Position the image correctly in the plot coordinates
            // We want the image to span from (0,0) to (Width, Height)
            // ImageAnnotation X/Y is the center point.
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

            // Adjust axes to image size
            _plotModel.Axes[0].Minimum = 0;
            _plotModel.Axes[0].Maximum = image.Width;
            _plotModel.Axes[1].Minimum = 0;
            _plotModel.Axes[1].Maximum = image.Height;

            _plotModel.InvalidatePlot(true);
            
            // Set PlotView size to match image size so ScrollViewer works
            PlotView.Width = image.Width;
            PlotView.Height = image.Height;
            
            // Ensure RoiCanvas matches the image size for correct coordinate mapping
            RoiCanvas.Width = image.Width;
            RoiCanvas.Height = image.Height;
        }

        // עזר: המרת בחירת ComboBox לצבע OpenCV (BGR)
        private Vec3b GetColorFromCombo(ComboBox combo)
        {
            if (combo.SelectedIndex == 0) return new Vec3b(0, 0, 0);       // Black
            if (combo.SelectedIndex == 1) return new Vec3b(0, 0, 255);     // Red (OpenCV is BGR)
            if (combo.SelectedIndex == 2) return new Vec3b(0, 255, 0);     // Green
            return new Vec3b(128, 128, 128);
        }

        // --- ROI Selection Logic ---
        private bool _isDrawingRoi = false;
        private System.Windows.Point _roiStartPoint;
        private Rectangle _currentRoiVisual;
        private readonly List<Rectangle> _roiVisuals = new List<Rectangle>();
        private readonly List<OpenCvSharp.Rect> _selectedRois = new List<OpenCvSharp.Rect>();

        private void BtnClearRoi_Click(object sender, RoutedEventArgs e)
        {
            _selectedRois.Clear();
            foreach (var visual in _roiVisuals)
            {
                if (RoiCanvas.Children.Contains(visual))
                {
                    RoiCanvas.Children.Remove(visual);
                }
            }
            _roiVisuals.Clear();
            _currentRoiVisual = null;
        }

        private void RoiCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ChkRoiEnabled.IsChecked != true || !_isImageLoaded) return;

            _isDrawingRoi = true;
            _roiStartPoint = e.GetPosition(RoiCanvas);

            _currentRoiVisual = new Rectangle
            {
                Stroke = Brushes.Lime,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 2 })
            };

            Canvas.SetLeft(_currentRoiVisual, _roiStartPoint.X);
            Canvas.SetTop(_currentRoiVisual, _roiStartPoint.Y);
            RoiCanvas.Children.Add(_currentRoiVisual);
            
            RoiCanvas.CaptureMouse();
        }

        private void RoiCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDrawingRoi || _currentRoiVisual == null) return;

            var currentPoint = e.GetPosition(RoiCanvas);

            double x = Math.Min(_roiStartPoint.X, currentPoint.X);
            double y = Math.Min(_roiStartPoint.Y, currentPoint.Y);
            double w = Math.Abs(_roiStartPoint.X - currentPoint.X);
            double h = Math.Abs(_roiStartPoint.Y - currentPoint.Y);

            Canvas.SetLeft(_currentRoiVisual, x);
            Canvas.SetTop(_currentRoiVisual, y);
            _currentRoiVisual.Width = w;
            _currentRoiVisual.Height = h;
        }

        private void RoiCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isDrawingRoi) return;
            _isDrawingRoi = false;
            RoiCanvas.ReleaseMouseCapture();

            if (_currentRoiVisual == null || _currentRoiVisual.Width < 5 || _currentRoiVisual.Height < 5)
            {
                if (_currentRoiVisual != null)
                {
                    RoiCanvas.Children.Remove(_currentRoiVisual);
                    _currentRoiVisual = null;
                }
                return;
            }

            // Store the ROI in image coordinates
            double x = Canvas.GetLeft(_currentRoiVisual);
            double y = Canvas.GetTop(_currentRoiVisual);
            double w = _currentRoiVisual.Width;
            double h = _currentRoiVisual.Height;

            int imgW = _originalImage.Width;
            int imgH = _originalImage.Height;

            int roiX = (int)Math.Max(0, Math.Min(x, imgW - 1));
            int roiY = (int)Math.Max(0, Math.Min(y, imgH - 1));
            int roiW = (int)Math.Min(w, imgW - roiX);
            int roiH = (int)Math.Min(h, imgH - roiY);

            if (roiW > 5 && roiH > 5)
            {
                _selectedRois.Add(new OpenCvSharp.Rect(roiX, roiY, roiW, roiH));
                _roiVisuals.Add(_currentRoiVisual);
            }
            else
            {
                RoiCanvas.Children.Remove(_currentRoiVisual);
            }
            
            _currentRoiVisual = null;
        }

        private void BtnManualAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded) return;

            Mat original = _originalImage;
            Mat processed = _processedImage;
            List<OpenCvSharp.Rect> relativeRois = null;

            if (_selectedRois.Count > 0)
            {
                // Find bounding box of all ROIs
                int minX = _selectedRois.Min(r => r.X);
                int minY = _selectedRois.Min(r => r.Y);
                int maxX = _selectedRois.Max(r => r.X + r.Width);
                int maxY = _selectedRois.Max(r => r.Y + r.Height);

                OpenCvSharp.Rect combinedRect = new OpenCvSharp.Rect(minX, minY, maxX - minX, maxY - minY);
                
                // Ensure it's within image bounds
                combinedRect = combinedRect.Intersect(new OpenCvSharp.Rect(0, 0, _originalImage.Width, _originalImage.Height));

                original = new Mat(_originalImage, combinedRect);
                processed = new Mat(_processedImage, combinedRect);

                // Adjust ROIs to be relative to the cropped image
                relativeRois = _selectedRois.Select(r => 
                    new OpenCvSharp.Rect(r.X - combinedRect.X, r.Y - combinedRect.Y, r.Width, r.Height)).ToList();
            }

            var stepWindow = new AnalysisStepsWindow(
                original,
                processed,
                (int)SliderLow.Value,
                (int)SliderHigh.Value,
                ComboColorLow.SelectedIndex == 1,
                ComboColorMid.SelectedIndex == 1,
                ComboColorHigh.SelectedIndex == 1,
                relativeRois);
            stepWindow.Show();
        }

        private void BtnAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded) return;

            int thLow = (int)SliderLow.Value;
            int thHigh = (int)SliderHigh.Value;

            // יצירת מסכה בינארית (קנבס שחור) שתכיל את התוצאה הסופית
            Mat defectMask = new Mat(_originalImage.Size(), MatType.CV_8UC1, Scalar.Black);

            bool isLowRed = ComboColorLow.SelectedIndex == 1;
            bool isMidRed = ComboColorMid.SelectedIndex == 1;
            bool isHighRed = ComboColorHigh.SelectedIndex == 1;

            
            Mat cleanedImage = new Mat();

            // שלב 1: ניקוי ראשוני (Pre-processing)
            Cv2.MedianBlur(_originalImage, cleanedImage, 5);

            // שלב 2: בניית המסכה לפי סף (Thresholding)
            Mat tempMask = new Mat();

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

            // --- הוספה חדשה: ניקוי מורפולוגי (Morphological Closing) ---
            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(11, 11));
            Cv2.MorphologyEx(defectMask, defectMask, MorphTypes.Close, kernel);

            // --- ROI Filtering ---
            if (_selectedRois.Count > 0)
            {
                using (Mat roiMask = new Mat(defectMask.Size(), MatType.CV_8UC1, Scalar.Black))
                {
                    foreach (var roi in _selectedRois)
                    {
                        Cv2.Rectangle(roiMask, roi, Scalar.White, -1);
                    }
                    Cv2.BitwiseAnd(defectMask, roiMask, defectMask);
                }
            }

            // --- מציאת קונטורים ---
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            
            Cv2.FindContours(
                defectMask, 
                out contours, 
                out hierarchy, 
                RetrievalModes.External, 
                ContourApproximationModes.ApproxNone); 

            // הכנה לציור התוצאות
            Mat resultDisplay = _processedImage.Clone();
            List<DefectItem> defectsList = new List<DefectItem>();
            int idCounter = 1;

            foreach (var cnt in contours)
            {
                double area = Cv2.ContourArea(cnt);

                // סינון רעשים קטנים שנשארו (אם יש)
                if (area < 10) continue;

                // ציור נקודות כחולות על קו המתאר
                foreach (var point in cnt)
                {
                    Cv2.Circle(resultDisplay, point, 1, Scalar.Blue, -1);
                }

                // חישוב המלבן החוסם רק לצורך מיקום הטקסט
                OpenCvSharp.Rect rect = Cv2.BoundingRect(cnt);
                
                // הוספת טקסט מזהה
                Cv2.PutText(resultDisplay, $"#{idCounter}", new OpenCvSharp.Point(rect.X, rect.Y - 5),
                    HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1);

                defectsList.Add(new DefectItem
                {
                    Id = idCounter++,
                    Area = area,
                    Status = area > 500 ? "CRITICAL" : "Warning"
                });
            }

            // עדכון ממשק
            UpdatePlotImage(resultDisplay);
            ListDefects.ItemsSource = defectsList;

            MessageBox.Show($"Analysis Complete. Found {defectsList.Count} defects.", "NDT Result");
        }

        private void ImgDisplay_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (ImageScaleTransform == null) return;
            double factor = e.Delta > 0 ? 1.1 : (1.0 / 1.1);
            SetZoom(ImageScaleTransform.ScaleX * factor);
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded || ImageScaleTransform == null) return;
            SetZoom(ImageScaleTransform.ScaleX / 1.1);
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded || ImageScaleTransform == null) return;
            SetZoom(ImageScaleTransform.ScaleX * 1.1);
        }

        private void BtnZoomFit_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded || ImageScaleTransform == null) return;
            if (!TryGetImageSize(out double imgWidth, out double imgHeight)) return;

            double viewportWidth = ImageScrollViewer.ViewportWidth;
            double viewportHeight = ImageScrollViewer.ViewportHeight;

            if (viewportWidth <= 0 || double.IsNaN(viewportWidth))
            {
                viewportWidth = ImageScrollViewer.ActualWidth;
            }

            if (viewportHeight <= 0 || double.IsNaN(viewportHeight))
            {
                viewportHeight = ImageScrollViewer.ActualHeight;
            }

            if (viewportWidth <= 0 || viewportHeight <= 0) return;

            double scale = Math.Min(viewportWidth / imgWidth, viewportHeight / imgHeight);
            if (double.IsInfinity(scale) || scale <= 0) return;

            SetZoom(scale);
        }

        private void BtnZoomActual_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded || ImageScaleTransform == null) return;
            SetZoom(1.0);
        }

        private void SetZoom(double scale)
        {
            if (ImageScaleTransform == null) return;

            double clamped = Math.Max(0.05, Math.Min(scale, 20.0));
            ImageScaleTransform.ScaleX = clamped;
            ImageScaleTransform.ScaleY = clamped;
            UpdateZoomLevelText(clamped);
        }

        private void UpdateZoomLevelText(double scale)
        {
            if (TxtZoomLevel == null) return;
            TxtZoomLevel.Text = $"{scale * 100:0}%";
        }

        private bool TryGetImageSize(out double width, out double height)
        {
            width = 0;
            height = 0;

            if (_processedImage != null && !_processedImage.IsDisposed)
            {
                width = _processedImage.Width;
                height = _processedImage.Height;
                return true;
            }

            if (_originalImage != null && !_originalImage.IsDisposed)
            {
                width = _originalImage.Width;
                height = _originalImage.Height;
                return true;
            }

            return false;
        }
    }
}
