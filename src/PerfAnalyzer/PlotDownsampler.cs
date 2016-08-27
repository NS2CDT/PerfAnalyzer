using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reactive.Linq;


namespace PerfAnalyzer {

  class DownsamplerSeries {
    public XYAxisSeries Series { get; set; }
    public PlotDownsampler Owner{ get; set; }
    public List<DataPoint> RawPoints { get; set; }
    public IObservable<List<DataPoint>> PointsSouce { get; set; }

    public DownsamplerSeries(PlotDownsampler owner, XYAxisSeries series, IObservable<List<DataPoint>> pointsSouce) {
      Series = series;
      PointsSouce = pointsSouce;
      Owner = owner;

      RawPoints = new List<DataPoint>();
      pointsSouce.ObserveOnDispatcher().Subscribe(p => OnNewPoints(p));
    }

    public void OnNewPoints(List<DataPoint> points) {
      RawPoints = points ?? new List<DataPoint>();
      RefreshPoints();
    }

    public void RefreshPoints() {
      if (Owner.Downsample) {
        Series.ItemsSource = PlotDownsampler.LargestTriangleThreeBuckets(RawPoints, Owner.DownsampleLimit);
      } else {
        Series.ItemsSource = RawPoints;
      }
      Series.PlotModel.InvalidatePlot(true);
    }

    public void ClearPoints() {
      RawPoints = new List<DataPoint>();
      Series.ItemsSource = RawPoints;
    }
  }

  public class PlotDownsampler {
    private Dictionary<XYAxisSeries, DownsamplerSeries> Series;

    public PlotDownsampler() {
      Series = new Dictionary<XYAxisSeries, DownsamplerSeries>();
    }

    public void AddSeries(XYAxisSeries series, IObservable<List<DataPoint>> points) {
      var record = new DownsamplerSeries(this, series, points);
      Series.Add(series, record);
    }

    public void ClearPoints() {
      foreach (var item in Series.Values) {
        item.ClearPoints();
      }
    }

    private int _downsampleLimit = 1000;

    public int DownsampleLimit {
      get { return _downsampleLimit; }
      set {
        _downsampleLimit = value;
        if (Downsample) {
          RefreshPoints();
        }
      }
    }

    private bool _downsample;

    public bool Downsample {
      get { return _downsample; }
      set {
        _downsample = value;
        RefreshPoints();
      }
    }

    private void RefreshPoints() {
      foreach (var item in Series.Values) {
        item.RefreshPoints();
      }
    }

    public static List<DataPoint> LargestTriangleThreeBuckets(List<DataPoint> data, int threshold) {
      int dataLength = data.Count;
      if (threshold >= dataLength || threshold == 0)
        return data; // Nothing to do

      List<DataPoint> sampled = new List<DataPoint>(threshold);

      // Bucket size. Leave room for start and end data points
      double every = (double)(dataLength - 2) / (threshold - 2);

      int a = 0;
      DataPoint maxAreaPoint = new DataPoint(0, 0);
      int nextA = 0;

      sampled.Add(data[a]); // Always add the first point

      for (int i = 0; i < threshold - 2; i++) {
        // Calculate point average for next bucket (containing c)
        double avgX = 0;
        double avgY = 0;
        int avgRangeStart = (int)(Math.Floor((i + 1) * every) + 1);
        int avgRangeEnd = (int)(Math.Floor((i + 2) * every) + 1);
        avgRangeEnd = avgRangeEnd < dataLength ? avgRangeEnd : dataLength;

        int avgRangeLength = avgRangeEnd - avgRangeStart;

        for (; avgRangeStart < avgRangeEnd; avgRangeStart++) {
          avgX += data[avgRangeStart].X; // * 1 enforces Number (value may be Date)
          avgY += data[avgRangeStart].Y;
        }
        avgX /= avgRangeLength;

        avgY /= avgRangeLength;

        // Get the range for this bucket
        int rangeOffs = (int)(Math.Floor((i + 0) * every) + 1);
        int rangeTo = (int)(Math.Floor((i + 1) * every) + 1);

        // Point a
        double pointAx = data[a].X; // enforce Number (value may be Date)
        double pointAy = data[a].Y;

        double maxArea = -1;

        for (; rangeOffs < rangeTo; rangeOffs++) {
          // Calculate triangle area over three buckets
          double area = Math.Abs((pointAx - avgX) * (data[rangeOffs].Y - pointAy) -
                                 (pointAx - data[rangeOffs].X) * (avgY - pointAy)
                            ) * 0.5;
          if (area > maxArea) {
            maxArea = area;
            maxAreaPoint = data[rangeOffs];
            nextA = rangeOffs; // Next a is this b
          }
        }

        sampled.Add(maxAreaPoint); // Pick this point from the bucket
        a = nextA; // This a is the next a (chosen b)
      }

      sampled.Add(data[dataLength - 1]); // Always add last

      return sampled;
    }
  }
}
