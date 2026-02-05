using System;
using System.Collections.Generic;
using System.Windows;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace NdtImageProcessor
{
    public partial class AnalysisStepsWindow : System.Windows.Window
    {
        private Mat _original;
        private Mat _current;
        private Mat _lutDisplay; // To show results on top of LUT colored image
        private List<OpenCvSharp.Rect> _rois;
        
        private int _thLow, _thHigh;
        private bool _isLowRed, _isMidRed, _isHighRed;

        public AnalysisStepsWindow(Mat original, Mat lutDisplay, int thLow, int thHigh, bool lowRed, bool midRed, bool highRed, List<OpenCvSharp.Rect> rois = null)
        {
            InitializeComponent();
            _original = original.Clone();
            _lutDisplay = lutDisplay.Clone();
            _rois = rois;

            if (_rois != null && _rois.Count > 0)
            {
                ApplyRoiMask(_original);
                ApplyRoiMask(_lutDisplay);
            }

            _current = _original.Clone();
            
            _thLow = thLow;
            _thHigh = thHigh;
            _isLowRed = lowRed;
            _isMidRed = midRed;
            _isHighRed = highRed;
            
            UpdateDisplay();
        }

        private void ApplyRoiMask(Mat target)
        {
            if (_rois == null || _rois.Count == 0) return;

            using (Mat mask = new Mat(target.Size(), MatType.CV_8UC1, Scalar.Black))
            {
                foreach (var roi in _rois)
                {
                    Cv2.Rectangle(mask, roi, Scalar.White, -1);
                }

                if (target.Channels() == 3)
                {
                    Mat masked = new Mat(target.Size(), target.Type(), Scalar.Black);
                    target.CopyTo(masked, mask);
                    masked.CopyTo(target);
                }
                else
                {
                    Cv2.BitwiseAnd(target, mask, target);
                }
            }
        }

        private void UpdateDisplay()
        {
            if (_current != null && !_current.IsDisposed)
            {
                ImgStepDisplay.Source = _current.ToBitmapSource();
            }
        }

        private void BtnBlur_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtBlurSize.Text, out int size) && size % 2 != 0 && size > 0)
            {
                Mat blurred = new Mat();
                Cv2.MedianBlur(_current, blurred, size);
                _current = blurred;
                UpdateDisplay();
            }
            else
            {
                MessageBox.Show("Kernel size must be a positive odd integer (e.g., 3, 5, 7).");
            }
        }

        private void BtnThreshold_Click(object sender, RoutedEventArgs e)
        {
            Mat defectMask = new Mat(_current.Size(), MatType.CV_8UC1, Scalar.Black);
            Mat tempMask = new Mat();

            if (_current.Channels() != 1)
            {
                MessageBox.Show("Thresholding requires a single-channel (grayscale) image. Please reset if you already ran contour detection.");
                return;
            }

            if (_isLowRed)
            {
                Cv2.InRange(_current, new Scalar(0), new Scalar(_thLow), tempMask);
                Cv2.BitwiseOr(defectMask, tempMask, defectMask);
            }
            if (_isMidRed)
            {
                Cv2.InRange(_current, new Scalar(_thLow), new Scalar(_thHigh), tempMask);
                Cv2.BitwiseOr(defectMask, tempMask, defectMask);
            }
            if (_isHighRed)
            {
                Cv2.InRange(_current, new Scalar(_thHigh), new Scalar(255), tempMask);
                Cv2.BitwiseOr(defectMask, tempMask, defectMask);
            }

            ApplyRoiMask(defectMask);
            
            _current = defectMask;
            UpdateDisplay();
        }

        private void BtnMorph_Click(object sender, RoutedEventArgs e)
        {
            if (_current.Channels() != 1)
            {
                MessageBox.Show("Morphology requires a binary mask (run Thresholding first).");
                return;
            }

            if (int.TryParse(TxtMorphSize.Text, out int size) && size > 0)
            {
                Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(size, size));
                Mat morphed = new Mat();
                Cv2.MorphologyEx(_current, morphed, MorphTypes.Close, kernel);
                _current = morphed;
                UpdateDisplay();
            }
            else
            {
                MessageBox.Show("Kernel size must be a positive integer.");
            }
        }

        private void BtnContours_Click(object sender, RoutedEventArgs e)
        {
            if (_current.Channels() != 1)
            {
                MessageBox.Show("Contour detection requires a binary mask (run Thresholding and Morphology first).");
                return;
            }

            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            
            Cv2.FindContours(_current, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
            
            Mat result = _lutDisplay.Clone();
            int count = 0;
            foreach (var cnt in contours)
            {
                double area = Cv2.ContourArea(cnt);
                if (area < 10) continue;

                count++;
                foreach (var pt in cnt)
                {
                    Cv2.Circle(result, pt, 1, Scalar.Blue, -1);
                }
                
                OpenCvSharp.Rect rect = Cv2.BoundingRect(cnt);
                Cv2.PutText(result, $"#{count}", new OpenCvSharp.Point(rect.X, rect.Y - 5),
                    HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1);
            }
            
            _current = result;
            UpdateDisplay();
            MessageBox.Show($"Found {count} defects.");
        }

        private void BtnFinal_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Process visualization complete. You can reset and try different parameters.");
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _current = _original.Clone();
            UpdateDisplay();
        }

        private void BtnBlurSizeUp_Click(object sender, RoutedEventArgs e)
        {
            TxtBlurSize.Text = AdjustOddValue(TxtBlurSize.Text, 2, 1, 99).ToString();
        }

        private void BtnBlurSizeDown_Click(object sender, RoutedEventArgs e)
        {
            TxtBlurSize.Text = AdjustOddValue(TxtBlurSize.Text, -2, 1, 99).ToString();
        }

        private void BtnMorphSizeUp_Click(object sender, RoutedEventArgs e)
        {
            TxtMorphSize.Text = AdjustValue(TxtMorphSize.Text, 1, 1, 99).ToString();
        }

        private void BtnMorphSizeDown_Click(object sender, RoutedEventArgs e)
        {
            TxtMorphSize.Text = AdjustValue(TxtMorphSize.Text, -1, 1, 99).ToString();
        }

        private static int AdjustOddValue(string text, int delta, int min, int max)
        {
            if (!int.TryParse(text, out int value))
            {
                value = min;
            }

            if (value % 2 == 0)
            {
                value += 1;
            }

            value += delta;
            if (value % 2 == 0)
            {
                value += delta > 0 ? 1 : -1;
            }

            if (value < min) value = min;
            if (value % 2 == 0) value += 1;
            if (value > max) value = max % 2 == 0 ? max - 1 : max;

            return value;
        }

        private static int AdjustValue(string text, int delta, int min, int max)
        {
            if (!int.TryParse(text, out int value))
            {
                value = min;
            }

            value += delta;
            if (value < min) value = min;
            if (value > max) value = max;

            return value;
        }
    }
}
