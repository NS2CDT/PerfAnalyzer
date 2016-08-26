using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Caliburn.Micro;

namespace PerfAnalyzer {

  public class AppBootstrapper : BootstrapperBase{

    CompositionContainer container;

    public AppBootstrapper() {
        Initialize();
    }

    protected override void BuildUp(object instance) {
      container.SatisfyImportsOnce(instance);
    }

    protected override void OnStartup(object sender, StartupEventArgs e) {
      DisplayRootViewFor<ShellViewModel>();
    }

    protected override void Configure() {
      container = new CompositionContainer(new AggregateCatalog(AssemblySource.Instance.Select(x => new AssemblyCatalog(x)).OfType<ComposablePartCatalog>()));

      CompositionBatch batch = new CompositionBatch();

      batch.AddExportedValue<IWindowManager>(new TAWindowManager());
      batch.AddExportedValue<IEventAggregator>(new EventAggregator());
      batch.AddExportedValue(container);

      container.Compose(batch);

      ConventionManager.AddElementConvention<MenuItem>(ItemsControl.ItemsSourceProperty, "DataContext", "Click");

      ActionMessage.SetMethodBinding = SetMethodBinding;
    }

    protected override object GetInstance(Type service, string key) {

      var contract = string.IsNullOrEmpty(key) ? AttributedModelServices.GetContractName(service) : key;
      var exports = container.GetExportedValues<object>(contract);

      if(exports.Count() > 0)
        return exports.First();

      throw new Exception(string.Format("Could not locate any instances of contract {0}.", contract));
    }

    //modified to traverse through a contextmenu's PlacementTarget value
    public static void SetMethodBinding(ActionExecutionContext context) {
      DependencyObject currentElement = context.Source;

      while(currentElement != null) {
        if(Caliburn.Micro.Action.HasTargetSet(currentElement)) {
          var target = Message.GetHandler(currentElement);

          if(target != null) {
            var method = ActionMessage.GetTargetMethod(context.Message, target);
            if(method != null) {
              context.Method = method;
              context.Target = target;
              context.View = currentElement;
              return;
            }
          } else {
            context.View = currentElement;
            return;
          }
        }

        if(currentElement is ContextMenu) {
          currentElement = ((ContextMenu)currentElement).PlacementTarget ?? VisualTreeHelper.GetParent(currentElement);
        } else {
          currentElement = VisualTreeHelper.GetParent(currentElement);
        }
      }

      if(context.Source.DataContext != null) {
        var target = context.Source.DataContext;
        var method = ActionMessage.GetTargetMethod(context.Message, target);

        if(method != null) {
          context.Target = target;
          context.Method = method;
          context.View = context.Source;
        }
      }
    }
  }

  public class TAWindowManager : WindowManager {
    protected override Window EnsureWindow(object model, object view, bool isDialog) {
      Window window = base.EnsureWindow(model, view, isDialog);

      window.SizeToContent = SizeToContent.Manual;

      return window;
    }
  }
}
