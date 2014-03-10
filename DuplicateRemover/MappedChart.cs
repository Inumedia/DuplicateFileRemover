using System;
using Utilities;
using System.Windows.Forms.DataVisualization.Charting;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;

namespace DuplicateRemover
{
    public class MappedChart : Chart
    {
        public double[] values;

        double newMaximum;
        double maximum;
        int valueLength;

        LockFreeQueue<DataPoint> queuedPoints;

        public double this[int i]
        {
            get
            {
                return values[i];
            }
            set
            {
                values[i] = value;
                queuedPoints.Push(new DataPoint(i, value));

                UpdateData();//BeginInvoke(new Action<int, double>(UpdateData), i, value);
            }
        }
        Series mainSeries;
        ChartArea mainArea;
        //Chart target;

        int writing = 0, waiting = 0;
        Stopwatch watch = new Stopwatch();

        public MappedChart(string chartName, string xAxis, string yAxis, int length = 32, Color? primaryColor = null)
        {
            values = new double[length];
            valueLength = length;
            queuedPoints = new LockFreeQueue<DataPoint>();
            CreateChart(chartName, xAxis, yAxis, primaryColor ?? Color.FromArgb(44, 139, 221), length);
        }

        void CreateChart(string chartName, string xaxis, string yaxis, Color primaryColor, int length)
        {
            Name = chartName;
            BackColor = Control.DefaultBackColor;
            Palette = ChartColorPalette.BrightPastel;
            //PaletteCustomColors = 

            mainArea = new ChartArea();
            mainArea.Name = "mainArea";
            mainArea.BackColor = Control.DefaultBackColor;
            ChartAreas.Add(mainArea);

            mainSeries = new Series("mainSeries");
            mainSeries.XValueType = ChartValueType.Int32;
            mainSeries.ChartType = SeriesChartType.Area;
            mainSeries.ChartArea = "mainArea";
            mainSeries.MarkerStyle = MarkerStyle.Circle;
            mainSeries.MarkerSize = 0;
            mainSeries.BorderWidth = 2;
            //srs.Color = ;
            mainSeries.Color = Color.FromArgb((int)(primaryColor.A * .64d), primaryColor.R, primaryColor.G, primaryColor.B);
            mainSeries.MarkerColor = primaryColor;
            mainSeries.BorderColor = primaryColor;
            mainArea.AxisX = new Axis()
            {
                Maximum = length,
                Minimum = 0,
                LabelStyle = new LabelStyle()
                {
                    TruncatedLabels = true
                },
                IntervalOffsetType = DateTimeIntervalType.Number,
                IntervalType = DateTimeIntervalType.Number,
                IsMarksNextToAxis = true,
                IsStartedFromZero = true,
                TextOrientation = TextOrientation.Stacked,
                Name = xaxis
            };
            /*area.AxisY = new Axis()
            {
                MaximumAutoSize = float.NaN
            };*/
            mainArea.AxisY.Maximum = 2000;

            for (int i = 0; i < length; ++i)
                mainSeries.Points.AddXY(i, 0);

            Series.Add(mainSeries);
            //srs.AxisLabel = xaxis;
            //ChartArea area = new ChartArea("mainChart");

            //ChartAreas.Add(
        }

        void UpdateData()
        {
            if (InvokeRequired)
            {
                if (waiting == 1 || Interlocked.CompareExchange(ref writing, 1, 0) == 1) return;

                BeginInvoke(new Action(UpdateData));
                return;
            }

            DataPoint point;
            watch.Restart();
            int count = 0;
            while (queuedPoints.Pop(out point) && watch.ElapsedMilliseconds < 500)
            {
                UpdateData(point);
                ++count;
            }
            this.Invalidate();
            watch.Stop();
            writing = 0;
            if (count != 0)
                waiting = 1;
        }

        protected override void OnPostPaint(ChartPaintEventArgs e)
        {
            waiting = 0;
            UpdateData();
            waiting = 0;
            base.OnPostPaint(e);
        }

        void UpdateData(int index, double value)
        {
            double adjusted = value + (value * 0.05);
            if (index == 0)
                newMaximum = value;
            else
                newMaximum = Math.Max(adjusted, newMaximum);
            if (index == valueLength - 1)
                maximum = Math.Min(maximum, newMaximum);
            else
                maximum = Math.Max(adjusted, maximum);
            mainArea.AxisY.Maximum = maximum;
            mainSeries.Points[index] = new DataPoint(index, value);
        }

        void UpdateData(DataPoint point)
        {
            int index = (int)point.XValue;
            if (point.YValues.Length != 1) return;
            double value = point.YValues[0];
            UpdateData(index, value);
        }
    }
}
