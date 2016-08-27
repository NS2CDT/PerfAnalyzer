using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PerfAnalyzer {
  public static class ObjectExtensions {

    public static IObservable<TProperty> OnPropertyChanges<T, TProperty>(this T source, Expression<Func<T, TProperty>> property) {
      return Observable.Create<TProperty>(o => {
        var propertyName = property.GetPropertyInfo().Name;
        var propDesc = TypeDescriptor.GetProperties(source)
                    .Cast<PropertyDescriptor>()
                    .Where(pd => string.Equals(pd.Name, propertyName, StringComparison.Ordinal))
                    .SingleOrDefault();
        if (propDesc == null) {
          o.OnError(new InvalidOperationException("Can not register change handler for this property."));
        }
        var propertySelector = property.Compile();

        EventHandler handler = delegate { o.OnNext(propertySelector(source)); };
        propDesc.AddValueChanged(source, handler);

        return Disposable.Create(() => propDesc.RemoveValueChanged(source, handler));
      });
    }

    /// <summary>
    /// Gets property information for the specified <paramref name="property"/> expression.
    /// </summary>
    /// <typeparam name="TSource">Type of the parameter in the <paramref name="property"/> expression.</typeparam>
    /// <typeparam name="TValue">Type of the property's value.</typeparam>
    /// <param name="property">The expression from which to retrieve the property information.</param>
    /// <returns>Property information for the specified expression.</returns>
    /// <exception cref="ArgumentException">The expression is not understood.</exception>
    public static PropertyInfo GetPropertyInfo<TSource, TValue>(this Expression<Func<TSource, TValue>> property) {
      if (property == null)
        throw new ArgumentNullException("property");

      var body = property.Body as MemberExpression;
      if (body == null)
        throw new ArgumentException("Expression is not a property", "property");

      var propertyInfo = body.Member as PropertyInfo;
      if (propertyInfo == null)
        throw new ArgumentException("Expression is not a property", "property");

      return propertyInfo;
    }
  }
}
