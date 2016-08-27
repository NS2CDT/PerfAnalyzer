using Caliburn.Micro;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using PerformanceLog;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Reactive;
using System.Reactive.Linq;

namespace PerfAnalyzer {
  [Export(typeof(FrameTimeViewModel))]
  public class FrameTimeViewModel : Screen, IHandle<ProfileLog> {
    public LinearAxis LeftAxis { get; private set; }
    public LinearAxis BottomAxis { get; private set; }
    public LineSeries FrameTimeSeries { get; private set; }
    public LineSeries SelectedTimeSeries { get; private set; }
    public PlotRangeTracker Ranges { get; private set; }
    public PlotDownsampler DownSampler { get; private set; }

    [Import]
    public IEventAggregator Events { get; set; }

    [ImportingConstructor]
    public FrameTimeViewModel(IEventAggregator events) {
      Events = events;

      DisplayName = "Frame Timeline";

      Model = new PlotModel();
      Model.LegendBackground = OxyColor.FromArgb(200, 255, 255, 255);
      Model.LegendBorder = OxyColors.Black;
      Model.LegendPlacement = LegendPlacement.Outside;
      Model.LegendPosition = LegendPosition.LeftTop;
      Model.Title = "Frame Timeline";

      /*
            var timeLineAxis = new TimeSpanAxis() {
              MajorGridlineStyle = LineStyle.Solid,
              MinorGridlineStyle = LineStyle.Dot,
              IntervalLength = 30,
              MajorStep = 1,
              Position = AxisPosition.Bottom,
              AbsoluteMinimum = 0,
            };
      */
      BottomAxis = new LinearAxis() {
        AbsoluteMinimum = 0,
        Position = AxisPosition.Bottom,
       // MinorStep = 1,
        MajorStep = 1000*60,
      };

      BottomAxis.AxisChanged += TimeLineAxis_AxisChanged;
      BottomAxis.LabelFormatter = t => $"{t/60000}";

      Model.Axes.Add(BottomAxis);
      /*
      var dateTimeAxis = new DateTimeAxis {
        Position = AxisPosition.Bottom,
        IntervalType = DateTimeIntervalType.Seconds,
        MinorIntervalType = DateTimeIntervalType.Milliseconds,
        //IntervalLength = 50
      };

      plotModel.Axes.Add(dateTimeAxis);
      */

      LeftAxis = new LinearAxis() {
        AbsoluteMinimum = 0,
        Position = AxisPosition.Left,
        MinorStep = 1,
        MajorStep = 10,
        Title = "Time Ms",
      };
      Model.Axes.Add(LeftAxis);
      LeftAxis.Zoom(0, 40);

      FrameTimeSeries = new LineSeries {
        Title = "Frame",
        StrokeThickness = 1,
       // DataFieldX = nameof(FrameEntry.Time),
        //DataFieldY = nameof(FrameEntry.TimeTaken),
      };

      Model.Series.Add(FrameTimeSeries);

      SelectedTimeSeries = new LineSeries {
        Title = "Selected",
        StrokeThickness = 1,
        Color = OxyColors.Red,
      };

      DownSampler = new PlotDownsampler();

      Model.Series.Add(SelectedTimeSeries);
      DownSampler.AddSeries(SelectedTimeSeries, this.OnPropertyChanges(n => SelectedNode).Select(n => GetNodeDataPoints(n)));
      DownSampler.AddSeries(FrameTimeSeries, this.OnPropertyChanges(n => FrametimePoints));

      Ranges = PlotRangeTracker.Install(Model);

      Ranges.RangeCreated += Ranges_RangeCreated;
      Ranges.RangeMoved += Ranges_RangeCreated;

      PLog = new ProfileLog();
    }

    private void Ranges_RangeCreated(RectangleAnnotation range, double start, double end) {
      var frames = PLog.GetFramesInRange(start, end);

      NodeList = PLog.GetStatsForRange(frames.Start, frames.End).
                 OrderByDescending(p => p.AvgExclusiveTime).
                 ToList();
    }

    private void TimeLineAxis_AxisChanged(object sender, AxisChangedEventArgs e) {

      if (e.ChangeType == AxisChangeTypes.Zoom) {

      }
    }

    //handle new ProfileLog opened message
    public void Handle(ProfileLog newProfileLog) {
      PLog = newProfileLog;

      Ranges.ClearRanges();
      Model.Annotations.Clear();
      Ranges.ReinstallAnnotations();

      List<DataPoint> frames;

      if (PLog.Frames.Count != 0) {

        long start = PLog.StartTime;
        frames = PLog.Frames.Select(f => new DataPoint(f.EndTimeMS, f.Time)).ToList();

        var markedFrames = PLog.Frames.
                           Where(f => f.Markers.Any(m => m.Kind == MarkerKind.LUA_TRACESFLUSHED)).
                           Select(f => new {
                             Frame = f,
                             Marker = f.Markers.First(m => m.Kind == MarkerKind.LUA_TRACESFLUSHED)
                           }).
                           ToList();


        foreach (var item in markedFrames) {
          var annotation = new LineAnnotation() {
            X = item.Frame.EndTimeMS,
            Text = $"{item.Marker.Label} {item.Marker.UserValue}",
            ToolTip = "",
            Type = LineAnnotationType.Vertical,
          };
          Model.Annotations.Add(annotation);
        }


      } else {
        frames = new List<DataPoint>();
      }

      FrametimePoints = frames;

      LeftAxis.Zoom(0, 60);
      BottomAxis.ZoomAt(1, 0);
      Model.InvalidatePlot(true);
    }

    public PlotModel Model { get; set; }

    private ProfileLog _profileLog;

    public ProfileLog PLog {
      get {
        return _profileLog;
      }

      set {
        _profileLog = value;

        if (value.Frames.Count == 0) {
          Ranges.CanCreateRanges = false;
        } else {
          Ranges.CanCreateRanges = true;
        }

        NotifyOfPropertyChange();
        NodeList = PLog.NodeStats;
      }
    }

    private List<DataPoint> _rawFrametimePoints;

    public List<DataPoint> FrametimePoints {
      get {
        return _rawFrametimePoints;
      }

      set {
        _rawFrametimePoints = value;
        NotifyOfPropertyChange();
      }
    }

    public bool DownsampleGraph {
      get {
        return DownSampler.Downsample;
      }

      set {
        DownSampler.Downsample = value;
        NotifyOfPropertyChange();
      }
    }

    public int DownsampleLimit {
      get {
        return DownSampler.DownsampleLimit;
      }

      set {
        DownSampler.DownsampleLimit = value;
        NotifyOfPropertyChange();
      }
    }

    private List<DataPoint> GetNodeDataPoints(PerfNodeStats node) {
      if (node != null) {
        var frameStats = PLog.GetNodeFrameStats(node.Id);
        long start = PLog.StartTime;
        return frameStats.
               Select(f => new DataPoint(f.Frame.EndTimeMS, f.Time)).
               ToList();
      } else {
        return new List<DataPoint>();
      }
    }

    private PerfNodeStats _selectedNode;

    public PerfNodeStats SelectedNode {
      get {
        return _selectedNode;
      }

      set {
        _selectedNode = value;
        NotifyOfPropertyChange();
      }
    }

    private List<PerfNodeStats> _nodeList;

    public List<PerfNodeStats> NodeList {
      get {
        return _nodeList;
      }

      set {
        var sorts = _nodeList != null ? CollectionViewSource.GetDefaultView(_nodeList).SortDescriptions.ToList() : null;
        _nodeList = value;

        if (sorts != null) {
          foreach (var sort in sorts) {
            CollectionViewSource.GetDefaultView(value).SortDescriptions.Add(sort);
          }
        }
        CollectionViewSource.GetDefaultView(value).Refresh();
        NotifyOfPropertyChange();
      }
    }


  }
}
