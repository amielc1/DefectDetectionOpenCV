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

        public MainWindow()
        {
            InitializeComponent();
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

            // המרה לתצוגה ב-WPF
            ImgDisplay.Source = _processedImage.ToBitmapSource();
        }

        // עזר: המרת בחירת ComboBox לצבע OpenCV (BGR)
        private Vec3b GetColorFromCombo(ComboBox combo)
        {
            if (combo.SelectedIndex == 0) return new Vec3b(0, 0, 0);       // Black
            if (combo.SelectedIndex == 1) return new Vec3b(0, 0, 255);     // Red (OpenCV is BGR)
            if (combo.SelectedIndex == 2) return new Vec3b(0, 255, 0);     // Green
            return new Vec3b(128, 128, 128);
        }

        private void BtnManualAnalyze_Click(object sender, RoutedEventArgs e)
        {
            if (!_isImageLoaded) return;

            var stepWindow = new AnalysisStepsWindow(
                _originalImage,
                _processedImage,
                (int)SliderLow.Value,
                (int)SliderHigh.Value,
                ComboColorLow.SelectedIndex == 1,
                ComboColorMid.SelectedIndex == 1,
                ComboColorHigh.SelectedIndex == 1);
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
    // הערה: במקרה של "מרקם", לפעמים כדאי לשקול גם Cv2.Blur (ממוצע) במקום MedianBlur
    // כדי ליצור הבדלי בהירות בין הרקע לפגם. כרגע השארתי את ה-Median שבחרת.
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
    // הגדרת "מכחול" (Kernel) בגודל 9x9 כפי שסיכמנו, כדי להתגבר על רעש גס
    Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(11, 11));

    // ביצוע פעולת "סגירה" (הרחבה -> כיווץ) בבת אחת
    // זה יסתום את החורים השחורים בתוך הפלוס האדום
    Cv2.MorphologyEx(defectMask, defectMask, MorphTypes.Close, kernel);


    // --- מציאת קונטורים ---
    OpenCvSharp.Point[][] contours;
    HierarchyIndex[] hierarchy;
    
    // שימוש ב-ApproxNone כדי לקבל דיוק מקסימלי בקו המתאר
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
    ImgDisplay.Source = resultDisplay.ToBitmapSource();
    ListDefects.ItemsSource = defectsList;

    MessageBox.Show($"Analysis Complete. Found {defectsList.Count} defects.", "NDT Result");
}

private void ImgDisplay_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
{
    var transform = (System.Windows.Media.ScaleTransform)this.FindName("ImageScaleTransform");
    if (transform == null) return;

    if (e.Delta > 0)
    {
        transform.ScaleX *= 1.1;
        transform.ScaleY *= 1.1;
    }
    else
    {
        transform.ScaleX /= 1.1;
        transform.ScaleY /= 1.1;
    }
}
        // --- 4. מציאת פגמים אוטומטית (מעודכן) ---
/*private void BtnAnalyze_Click(object sender, RoutedEventArgs e)
{
    if (!_isImageLoaded) return;

    int thLow = (int)SliderLow.Value;
    int thHigh = (int)SliderHigh.Value;

    // יצירת מסכה בינארית רק לאזורים שהוגדרו כ"אדום" (Defect)
    Mat defectMask = new Mat(_originalImage.Size(), MatType.CV_8UC1, Scalar.Black);

    bool isLowRed = ComboColorLow.SelectedIndex == 1;
    bool isMidRed = ComboColorMid.SelectedIndex == 1;
    bool isHighRed = ComboColorHigh.SelectedIndex == 1;

    
    Mat cleanedImage = new Mat();

    // 2. הפעלת פילטר חציון (Median Blur)
    // המספר 5 הוא גודל ה"חלון" (Kernel Size). הוא חייב להיות אי-זוגי.
    // ככל שהמספר גדול יותר, הניקוי חזק יותר אך התמונה תהיה פחות חדה.
    Cv2.MedianBlur(_originalImage, cleanedImage, 5);
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

    // --- מציאת קונטורים ---
    OpenCvSharp.Point[][] contours;
    HierarchyIndex[] hierarchy;
    
    // שינוי 1: שימוש ב-ApproxNone כדי לקבל את כל הנקודות על ההיקף ולא רק קודקודים
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

        // סינון רעשים
        if (area < 10) continue;

        // שינוי 2: במקום ריבוע, ציור נקודות אדומות על כל קו המתאר
        foreach (var point in cnt)
        {
            // ציור עיגול מלא ברדיוס 1 פיקסל בצבע אדום
            // פרמטר אחרון -1 אומר "מלא את העיגול בצבע"
            Cv2.Circle(resultDisplay, point, 1, Scalar.Blue, -1);
        }

        // אופציונלי: עדיין נחשב את הריבוע רק כדי לדעת איפה למקם את הטקסט
        OpenCvSharp.Rect rect = Cv2.BoundingRect(cnt);
        
        // הוספת טקסט (ID) ליד הפגם
        Cv2.PutText(resultDisplay, $"#{idCounter}", new OpenCvSharp.Point(rect.X, rect.Y - 5),
            HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1); // טקסט לבן שיראה טוב על רקע כהה

        defectsList.Add(new DefectItem
        {
            Id = idCounter++,
            Area = area,
            Status = area > 500 ? "CRITICAL" : "Warning"
        });
    }

    // עדכון ממשק
    ImgDisplay.Source = resultDisplay.ToBitmapSource();
    ListDefects.ItemsSource = defectsList;

    MessageBox.Show($"Analysis Complete. Found {defectsList.Count} defects.", "NDT Result");
}*/
       
    }
}