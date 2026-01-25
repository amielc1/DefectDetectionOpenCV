using CommunityToolkit.Mvvm.ComponentModel;
using DefectDetectionDemo.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions; // Required for Bitmap conversion
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DefectDetectionDemo.Logic
{
    public partial class DefectDetectModel : ObservableObject
    {
        #region PRIVATE MEMBERS

        private HeatMapSeries _colorHeatMapSeries;
        private List<LineSeries> _CursorsList;
        private LineSeries _selectedCursor;
        private double _histogramMax;
        private double _histogramMinX;
        private double _histogramMaxX;
        private OxyPalette _oxyPalette;
        private AnalysisImage _image;
        private int _binsNum;
        private readonly Action<AnalysisImage> _actionNotify;
        private const int COLOR_LEGEND_LENGTH = 1000;

        #endregion

        #region PROPERTIES

        [ObservableProperty] private PlotModel colorLegend;
        [ObservableProperty] private PlotModel histogram;
        [ObservableProperty] private ObservableCollection<HistogramDomain> histogramDomainsList;
        [ObservableProperty] private ObservableCollection<DefectItem> defectsList;
        [ObservableProperty] private double totalDefectArea;

        public int BinsNum
        {
            get => _binsNum;
            set
            {
                _binsNum = value;
                InitializeHistogram();
            }
        }

        #endregion

        public DefectDetectModel(Action<AnalysisImage> actionNotify)
        {
            _actionNotify = actionNotify;
            _CursorsList = new List<LineSeries>();
            BinsNum = 100;
            HistogramDomainsList = new ObservableCollection<HistogramDomain>();
            DefectsList = new ObservableCollection<DefectItem>();
            InitializeColorLegend();
        }



        public void InsertImage(AnalysisImage image)
        {
            _image = image;
            InitializeColorLegend();
            InitializeHistogram();
        }

        private void InitializeColorLegend()
        {
            _oxyPalette = OxyPalettes.Jet(COLOR_LEGEND_LENGTH);
            var model = new PlotModel
                { PlotAreaBorderColor = OxyColors.Transparent, Background = OxyColors.Transparent };

            // Setup Axes (Simplified for demo)
            model.Axes.Add(new LinearColorAxis { Position = AxisPosition.Right, Palette = _oxyPalette });

            // Dummy data for legend
            double[,] data = new double[COLOR_LEGEND_LENGTH, 2];
            for (int i = 0; i < COLOR_LEGEND_LENGTH; i++)
            {
                data[i, 0] = i;
                data[i, 1] = i;
            }

            _colorHeatMapSeries = new HeatMapSeries
            {
                Interpolate = false,
                RenderMethod = HeatMapRenderMethod.Bitmap,
                X0 = 0.5, X1 = COLOR_LEGEND_LENGTH - 0.5, Y0 = 0, Y1 = 1, Data = data
            };
            model.Series.Add(_colorHeatMapSeries);
            ColorLegend = model;
        }

        private void InitializeHistogram()
        {
            if (_image == null) return;
            _CursorsList.Clear();
            HistogramDomainsList.Clear();

            var model = new PlotModel { PlotAreaBorderColor = OxyColors.White, TextColor = OxyColors.White };

            // Axes
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom, Title = "Intensity", Key = "xAxis", TextColor = OxyColors.White,
                AxislineColor = OxyColors.White
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left, Title = "Count %", Key = "yAxis", TextColor = OxyColors.White,
                AxislineColor = OxyColors.White
            });

            // Event Hooks
            model.MouseMove += (s, e) => MouseMove(e);
            model.MouseUp += (s, e) => MouseUp(e);

            // Calculate Data
            List<double> pixelPowerList = new List<double>();
            int rows = _image.Data.GetLength(0);
            int cols = _image.Data.GetLength(1);
            for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                double val = _image.Data[i, j];
                if (!double.IsNaN(val)) pixelPowerList.Add(val);
            }

            if (pixelPowerList.Count == 0) return;

            _histogramMinX = pixelPowerList.Min();
            _histogramMaxX = pixelPowerList.Max();
            if (Math.Abs(_histogramMaxX - _histogramMinX) < 1e-9) _histogramMaxX = _histogramMinX + 1.0;

            // Draw Series
            var histogramSeries = new HistogramSeries
                { StrokeThickness = 1, FillColor = OxyColor.FromRgb(100, 100, 100), StrokeColor = OxyColors.White };
            histogramSeries.Items.AddRange(Collect(pixelPowerList, _histogramMinX, _histogramMaxX, _binsNum));
            model.Series.Add(histogramSeries);

            Histogram = model;

            // Init default cursors
            AddCursor(_histogramMinX);
            AddCursor(_histogramMaxX);
            HistogramDomainsList.Add(new HistogramDomain(_histogramMinX, _histogramMaxX, -1) { Index = 1 });
        }

        public void AddCursor()
        {
            lock (_CursorsList)
            {
                if (_CursorsList.Count < 2) return;
                double lastCursorX = _CursorsList[_CursorsList.Count - 2].Points[0].X;
                double endBoundaryX = _CursorsList[_CursorsList.Count - 1].Points[0].X;
                AddCursor((lastCursorX + endBoundaryX) / 2);
            }
        }

        private void AddCursor(double xPosition)
        {
            var cursor = new LineSeries
            {
                StrokeThickness = 3, Color = OxyColors.Red, Tag = "Cursor",
                Points = { new DataPoint(xPosition, 0), new DataPoint(xPosition, _histogramMax) }
            };
            cursor.MouseDown += (s, e) => MouseDown(e, cursor);

            lock (_CursorsList)
            {
                _CursorsList.Add(cursor);
                // Simple sort logic for demo (usually you insert at correct index)
                if (_CursorsList.Count > 2)
                {
                    var last = _CursorsList[^1];
                    _CursorsList[^1] = _CursorsList[^2];
                    _CursorsList[^2] = last;
                }
            }

            Histogram?.Series.Add(cursor);
            Histogram?.InvalidatePlot(true);

            // Logic to update domains list simplified for demo...
            if (_CursorsList.Count > 2 && HistogramDomainsList.Count > 0)
            {
                var lastDomain = HistogramDomainsList.Last();
                double originalEnd = lastDomain.End;
                lastDomain.End = xPosition;
                HistogramDomainsList.Add(new HistogramDomain(xPosition, originalEnd, -1)
                    { Index = HistogramDomainsList.Count + 1 });
            }
        }

        // Logic for Flaws (OpenCV)
        public void DetectFlaws()
        {
            if (_image?.Data == null) return;
            var data = _image.Data;
            int rows = data.GetLength(0);
            int cols = data.GetLength(1);

            DefectsList.Clear();

            // 1. הכנה לציור: יצירת תמונה צבעונית שנוכל לצייר עליה סימונים אדומים
            // אנחנו ממירים את הדאטה הגולמי לתמונה נראית לעין (Grayscale background)
            using Mat visualMat = new Mat(rows, cols, MatType.CV_8UC3);
            var indexer = visualMat.GetGenericIndexer<Vec3b>();

            // נרמול התמונה המקורית לרקע אפור (כדי שנראה את הפגמים על גבי התמונה)
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    // המרה פשוטה ל-Grayscale לצורך תצוגה
                    byte grayVal = (byte)((data[i, j] - _histogramMinX) / (_histogramMaxX - _histogramMinX) * 255);
                    indexer[i, j] = new Vec3b(grayVal, grayVal, grayVal);
                }
            }

            // 2. לוגיקת זיהוי הפגמים (המקורית)
            foreach (var domain in HistogramDomainsList)
            {
                // מדלגים על תחומים שאין להם "צבע" מוגדר (כלומר לא סומנו כפגמים)
                // או שהמשתמש השאיר אותם כ- (-1)
                if (domain.Color == -1) continue;

                // יצירת מסכה בינארית לזיהוי התחום הנוכחי
                using Mat mask = new Mat(rows, cols, MatType.CV_8UC1, Scalar.All(0));
                var maskIndexer = mask.GetGenericIndexer<byte>();

                for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    if (data[i, j] >= domain.Start && data[i, j] <= domain.End)
                        maskIndexer[i, j] = 255; // לבן = חשוד כפגם
                }

                // מציאת קווי מתאר (Contours)
                Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple);

                foreach (var contour in contours)
                {
                    double area = Cv2.ContourArea(contour);
                    if (area < 10) continue; // סינון רעשים קטנים

                    var rect = Cv2.BoundingRect(contour);

                    // הוספה לרשימת הנתונים (Data)
                    DefectsList.Add(new DefectItem
                    {
                        Name = $"Flaw_{DefectsList.Count + 1}",
                        Area = area,
                        Type = DefectType.Rectangle,
                        IsVisible = true,
                        Points = new List<DataPoint> { new DataPoint(rect.X, rect.Y) }
                    });

                    // --- תוספת: ציור ויזואלי על התמונה ---

                    // ציור מלבן אדום סביב הפגם
                    // Scalar(0, 0, 255) = Red in BGR
                    Cv2.Rectangle(visualMat, rect, new Scalar(0, 0, 255), 2);

                    // אופציונלי: כתיבת מספר הפגם ליד המלבן
                    Cv2.PutText(visualMat,
                        (DefectsList.Count).ToString(),
                        new OpenCvSharp.Point(rect.X, rect.Y - 5),
                        HersheyFonts.HersheySimplex,
                        0.5,
                        new Scalar(0, 255, 255), // Yellow text
                        1);
                }
            }

            TotalDefectArea = DefectsList.Sum(x => x.Area);

            // 3. עדכון התצוגה למשתמש
            var bmp = visualMat.ToBitmapSource();
            var resultImage = new AnalysisImage(_image.ImageParameters, _image.Data) { DisplayBitmap = bmp };

            // שליחת התמונה המעודכנת ל-View
            _actionNotify(resultImage);
        }

        // Apply Coloring
        public void CreateDomainColoredImage()
        {
            if (_image == null) return;
            var rows = _image.Data.GetLength(0);
            var cols = _image.Data.GetLength(1);

            // Prepare an RGB Mat for display
            using Mat colorMat = new Mat(rows, cols, MatType.CV_8UC3, Scalar.All(0));
            var indexer = colorMat.GetGenericIndexer<Vec3b>();

            // Normalize original data for visualization background (Gray)
            // In a real app, you might overlay colors on the original image

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    double val = _image.Data[i, j];
                    bool colored = false;

                    foreach (var domain in HistogramDomainsList)
                    {
                        // Check if value is in range AND has a valid color assigned
                        // Note: In your logic, 'Color' seemed to be a replacement value. 
                        // Here we treat 'Color' as an index for the Palette or a visualization flag.
                        // For this demo: If Color != -1, we paint it Red.
                        if (val >= domain.Start && val <= domain.End && domain.Color != -1)
                        {
                            indexer[i, j] = new Vec3b(0, 0, 255); // Red (BGR format in OpenCV)
                            colored = true;
                            break;
                        }
                    }

                    if (!colored)
                    {
                        // Gray scale fallback
                        byte gray = (byte)((val - _histogramMinX) / (_histogramMaxX - _histogramMinX) * 255);
                        indexer[i, j] = new Vec3b(gray, gray, gray);
                    }
                }
            }

            // Convert back to WPF Bitmap
            var bmp = colorMat.ToBitmapSource();

            // Return result via the image wrapper
            var resultImage = new AnalysisImage(_image.ImageParameters, _image.Data) { DisplayBitmap = bmp };
            _actionNotify(resultImage);
        }

        #region MOUSE & HELPER METHODS

        private void MouseDown(OxyMouseEventArgs e, LineSeries cursor)
        {
            if (cursor.Tag as string == "Cursor") _selectedCursor = cursor;
        }

        private void MouseUp(OxyMouseEventArgs e) => _selectedCursor = null;

        private void MouseMove(OxyMouseEventArgs e)
        {
            if (_selectedCursor == null) return;
            // Simplified movement logic
            double x = _selectedCursor.InverseTransform(e.Position).X;
            _selectedCursor.Points[0] = new DataPoint(x, 0);
            _selectedCursor.Points[1] = new DataPoint(x, _histogramMax);
            Histogram.InvalidatePlot(true);

            // Find which cursor this is and update domain (Simplified search)
            int idx = _CursorsList.IndexOf(_selectedCursor);
            if (idx > 0 && idx < HistogramDomainsList.Count)
            {
                HistogramDomainsList[idx - 1].End = x;
                HistogramDomainsList[idx].Start = x;
            }
        }

        private List<HistogramItem> Collect(IEnumerable<double> samples, double start, double end, int binCount)
        {
            var binBreaks = new List<double>();
            for (int i = 0; i <= binCount; i++) binBreaks.Add(start + ((end - start) / binCount * i));

            // Simplified Histogram collection
            int[] counts = new int[binCount];
            foreach (var s in samples)
            {
                int bin = (int)((s - start) / (end - start) * binCount);
                if (bin >= 0 && bin < binCount) counts[bin]++;
            }

            var items = new List<HistogramItem>();
            long total = counts.Sum();
            for (int i = 0; i < binCount; i++)
            {
                items.Add(new HistogramItem(binBreaks[i], binBreaks[i + 1], (double)counts[i] / total * 100,
                    counts[i]));
            }

            _histogramMax = items.Max(x => x.Area); // Area is actually height here in OxyPlot terms usually
            return items;
        }

        #endregion
    }
}