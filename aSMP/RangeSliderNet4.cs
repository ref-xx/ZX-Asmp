using System;
using System.Drawing;
using System.Windows.Forms;

// Designer.cs dosyasındaki namespace ile aynısı olmalı:
namespace aSMP
{
    // Designer ile birleşebilmesi için 'partial' ekledik
    public partial class RangeSliderNet4 : Control
    {
        public enum SliderOrientation
        {
            Horizontal,
            Vertical
        }

        private int minValue = 0;
        private int maxValue = 100;
        private int lowerValue = 20;
        private int upperValue = 80;

        private bool draggingLower = false;
        private bool draggingUpper = false;

        private const int sliderWidth = 10;

        private SliderOrientation orientation = SliderOrientation.Horizontal;
        private Color sliderColor = Color.LightBlue;

        // Event tanımı
        public event EventHandler RangeChanged;

        // Propertyler (.NET 4.0 uyumlu)
        public int MinValue
        {
            get { return minValue; }
            set
            {
                minValue = value;
                Invalidate();
            }
        }

        public int MaxValue
        {
            get { return maxValue; }
            set
            {
                maxValue = value;
                Invalidate();
            }
        }

        public int LowerValue
        {
            get { return lowerValue; }
            set
            {
                int oldValue = lowerValue;
                lowerValue = Math.Max(minValue, Math.Min(value, upperValue));
                if (oldValue != lowerValue)
                {
                    OnRangeChanged(EventArgs.Empty);
                }
                Invalidate();
            }
        }

        public int UpperValue
        {
            get { return upperValue; }
            set
            {
                int oldValue = upperValue;
                upperValue = Math.Min(maxValue, Math.Max(value, lowerValue));
                if (oldValue != upperValue)
                {
                    OnRangeChanged(EventArgs.Empty);
                }
                Invalidate();
            }
        }

        public SliderOrientation Orientation
        {
            get { return orientation; }
            set
            {
                orientation = value;
                Invalidate();
            }
        }

        public Color SliderColor
        {
            get { return sliderColor; }
            set
            {
                sliderColor = value;
                Invalidate();
            }
        }

        public RangeSliderNet4()
        {
            // Designer.cs varsa InitializeComponent çağrılmalıdır, 
            // ama UserControl değil de Custom Control olduğu için şart değil,
            // yine de çift tanımlamayı önlemek için burayı sade tutuyoruz.

            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        }

        // ÖNEMLİ: Dispose metodunu buradan SİLDİK. 
        // Çünkü Designer.cs dosyasında zaten tanımlı.

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            if (orientation == SliderOrientation.Horizontal)
            {
                DrawHorizontalSlider(g);
            }
            else
            {
                DrawVerticalSlider(g);
            }
        }

        private void DrawHorizontalSlider(Graphics g)
        {
            int trackHeight = Height / 3;
            Rectangle trackRect = new Rectangle(sliderWidth / 2, Height / 2 - trackHeight / 2, Width - sliderWidth, trackHeight);

            using (Brush trackBrush = new SolidBrush(Color.Gray))
            {
                g.FillRectangle(trackBrush, trackRect);
            }

            float range = maxValue - minValue;
            if (range == 0) range = 1;

            int lowerX = (int)((lowerValue - minValue) / range * (Width - sliderWidth));
            int upperX = (int)((upperValue - minValue) / range * (Width - sliderWidth));

            using (Brush rangeBrush = new SolidBrush(sliderColor))
            {
                g.FillRectangle(rangeBrush, lowerX + sliderWidth / 2, trackRect.Top, upperX - lowerX, trackHeight);
            }

            using (Brush thumbBrush = new SolidBrush(Color.Blue))
            {
                g.FillRectangle(thumbBrush, lowerX, 0, sliderWidth, Height);
            }

            using (Brush thumbBrush = new SolidBrush(Color.Red))
            {
                g.FillRectangle(thumbBrush, upperX, 0, sliderWidth, Height);
            }
        }

        private void DrawVerticalSlider(Graphics g)
        {
            int trackWidth = Width / 4;
            Rectangle trackRect = new Rectangle(Width / 2 - trackWidth / 2, sliderWidth / 2, trackWidth, Height - sliderWidth);

            using (Brush trackBrush = new SolidBrush(Color.Gray))
            {
                g.FillRectangle(trackBrush, trackRect);
            }

            float range = maxValue - minValue;
            if (range == 0) range = 1;

            int lowerY = Height - sliderWidth - (int)((lowerValue - minValue) / range * (Height - sliderWidth));
            int upperY = Height - sliderWidth - (int)((upperValue - minValue) / range * (Height - sliderWidth));

            using (Brush rangeBrush = new SolidBrush(sliderColor))
            {
                g.FillRectangle(rangeBrush, trackRect.Left, upperY, trackWidth, lowerY - upperY);
            }

            using (Font textFont = new Font("Arial", 8, FontStyle.Bold))
            using (Brush textBrush = new SolidBrush(Color.White))
            {
                StringFormat stringFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                using (Brush thumbBrush = new SolidBrush(Color.LightGray))
                {
                    g.FillRectangle(thumbBrush, 0, lowerY, Width, sliderWidth);
                    g.DrawString(lowerValue.ToString(), textFont, textBrush, new RectangleF(0, lowerY, Width, sliderWidth), stringFormat);
                }

                using (Brush thumbBrush = new SolidBrush(Color.DarkGray))
                {
                    g.FillRectangle(thumbBrush, 0, upperY, Width, sliderWidth);
                    g.DrawString(upperValue.ToString(), textFont, textBrush, new RectangleF(0, upperY, Width, sliderWidth), stringFormat);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (orientation == SliderOrientation.Horizontal)
            {
                HandleHorizontalMouseDown(e);
            }
            else
            {
                HandleVerticalMouseDown(e);
            }
        }

        private void HandleHorizontalMouseDown(MouseEventArgs e)
        {
            float range = (float)(maxValue - minValue);
            if (range == 0) range = 1;

            int lowerX = (int)((lowerValue - minValue) / range * (Width - sliderWidth));
            int upperX = (int)((upperValue - minValue) / range * (Width - sliderWidth));

            Rectangle lowerThumbRect = new Rectangle(lowerX, 0, sliderWidth, Height);
            Rectangle upperThumbRect = new Rectangle(upperX, 0, sliderWidth, Height);

            if (lowerThumbRect.Contains(e.Location))
            {
                draggingLower = true;
            }
            else if (upperThumbRect.Contains(e.Location))
            {
                draggingUpper = true;
            }
        }

        private void HandleVerticalMouseDown(MouseEventArgs e)
        {
            float range = (float)(maxValue - minValue);
            if (range == 0) range = 1;

            int lowerY = Height - sliderWidth - (int)((lowerValue - minValue) / range * (Height - sliderWidth));
            int upperY = Height - sliderWidth - (int)((upperValue - minValue) / range * (Height - sliderWidth));

            Rectangle lowerThumbRect = new Rectangle(0, lowerY, Width, sliderWidth);
            Rectangle upperThumbRect = new Rectangle(0, upperY, Width, sliderWidth);

            if (lowerThumbRect.Contains(e.Location))
            {
                draggingLower = true;
            }
            else if (upperThumbRect.Contains(e.Location))
            {
                draggingUpper = true;
            }
            else if (e.Y > Height / 2)
            {
                draggingLower = true;
                HandleVerticalMouseMove(e);
            }
            else
            {
                draggingUpper = true;
                HandleVerticalMouseMove(e);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (orientation == SliderOrientation.Horizontal)
            {
                HandleHorizontalMouseMove(e);
            }
            else
            {
                HandleVerticalMouseMove(e);
            }
        }

        private void HandleHorizontalMouseMove(MouseEventArgs e)
        {
            float range = maxValue - minValue;
            if (draggingLower)
            {
                int newLowerValue = (int)((e.X / (float)(Width - sliderWidth)) * range) + minValue;
                LowerValue = Math.Max(minValue, Math.Min(newLowerValue, upperValue));
            }
            else if (draggingUpper)
            {
                int newUpperValue = (int)((e.X / (float)(Width - sliderWidth)) * range) + minValue;
                UpperValue = Math.Min(maxValue, Math.Max(newUpperValue, lowerValue));
            }
        }

        private void HandleVerticalMouseMove(MouseEventArgs e)
        {
            float range = maxValue - minValue;
            if (draggingLower)
            {
                int newLowerValue = maxValue - (int)((e.Y / (float)(Height - sliderWidth)) * range);
                LowerValue = Math.Max(minValue, Math.Min(newLowerValue, upperValue));
            }
            else if (draggingUpper)
            {
                int newUpperValue = maxValue - (int)((e.Y / (float)(Height - sliderWidth)) * range);
                UpperValue = Math.Min(maxValue, Math.Max(newUpperValue, lowerValue));
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            int increment = (e.Delta > 0) ? 8 : -8;

            if (draggingLower)
            {
                LowerValue = Math.Max(minValue, Math.Min(lowerValue + increment, upperValue));
            }
            else if (draggingUpper)
            {
                UpperValue = Math.Min(maxValue, Math.Max(upperValue + increment, lowerValue));
            }
            else
            {
                LowerValue = Math.Max(minValue, Math.Min(lowerValue + increment, upperValue - 1));
                UpperValue = Math.Min(maxValue, Math.Max(upperValue + increment, lowerValue + 1));
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            draggingLower = false;
            draggingUpper = false;
        }

        protected virtual void OnRangeChanged(EventArgs e)
        {
            if (RangeChanged != null)
            {
                RangeChanged(this, e);
            }
        }
    }
}