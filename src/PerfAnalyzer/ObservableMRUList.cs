using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace PerfAnalyzer {
  //http://stevemdev.wordpress.com/2012/10/06/wpf-mru-combobox/
  public class ObservableMRUList<T> : ObservableCollection<T>, IXmlSerializable {

    private int _maxSize = 10;
    private readonly IEqualityComparer<T> _itemComparer = EqualityComparer<T>.Default;

    public ObservableMRUList()
      : base() {
    }

    public ObservableMRUList(IEnumerable<T> collection)
      : base(collection) {

    }

    public ObservableMRUList(List<T> list)
      : base(list) {

    }

    public ObservableMRUList(int maxSize, IEqualityComparer<T> itemComparer)
      : base() {
      MaxEntrys = maxSize;
      _itemComparer = itemComparer;
    }

    public ObservableMRUList(IEnumerable<T> collection, int maxSize, IEqualityComparer<T> itemComparer)
      : base(collection) {

      MaxEntrys = maxSize;
      _itemComparer = itemComparer;
      RemoveOverflow();
    }

    public ObservableMRUList(List<T> list, int maxSize, IEqualityComparer<T> itemComparer)
      : base(list) {

      MaxEntrys = maxSize;
      _itemComparer = itemComparer;
    }

    public int MaxEntrys {
      get {
        return _maxSize;
      }

      set {
        _maxSize = value;
        RemoveOverflow();
      }
    }

    public new void Add(T item) {

      int indexOfMatch = this.IndexOf(item);

      if (indexOfMatch < 0) {
        base.Insert(0, item);
      } else {
        base.Move(indexOfMatch, 0);
      }

      RemoveOverflow();
    }

    public new bool Contains(T item) {
      return this.Contains(item, _itemComparer);
    }

    public new int IndexOf(T item) {

      int indexOfMatch = -1;

      if (_itemComparer != null) {
        for (int idx = 0; idx < this.Count; idx++) {
          if (_itemComparer.Equals(item, this[idx])) {
            indexOfMatch = idx;
            break;
          }
        }
      } else {
        indexOfMatch = base.IndexOf(item);
      }

      return indexOfMatch;

    }

    public new bool Remove(T item) {

      bool opResult = false;
      int targetIndex = IndexOf(item);

      if (targetIndex > -1) {
        RemoveAt(targetIndex);
        opResult = true;
      }

      return opResult;
    }

    public int IndexOf<T2>(T2 item) {

      for (int i = 0; i < Count; i++) {
        if (Object.Equals(this[i], item)) {
          return i;
        }
      }

      return -1;
    }

    public new bool Remove<T2>(T2 item) {

      bool opResult = false;
      int targetIndex = IndexOf(item);

      if (targetIndex > -1) {
        RemoveAt(targetIndex);
        opResult = true;
      }

      return opResult;
    }

    private void RemoveOverflow() {

      if (MaxEntrys > 0) {
        while (Count > MaxEntrys) {
          RemoveAt(Count - 1);
        }
      }
    }

    //we implement our own serialization because the default XML serializer used by the config system uses our Add function when it deserializes
    //our internal collection but our Add function always inserts the item at the start of the collection. This behaviour causes the list to
    //get reversed every time its loaded from the config file
    public void ReadXml(XmlReader reader) {
      var ds = new XmlSerializer(typeof(List<T>));

      //skip to either child element of the current element or to the next element if it has no children
      reader.Read();

      if (!ds.CanDeserialize(reader)) {
        //bail if theres no child elements that would contain the collection to read
        return;
      }

      foreach (var item in (List<T>)ds.Deserialize(reader)) {
        base.Add(item);
      }
    }

    public void WriteXml(XmlWriter writer) {

      var ds = new XmlSerializer(typeof(List<T>));

      ds.Serialize(writer, this.ToList());
    }

    public System.Xml.Schema.XmlSchema GetSchema() {
      return null;
    }
  }

}
