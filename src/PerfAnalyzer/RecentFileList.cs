using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using System.Xml.Serialization;
using PerfAnalyzer.Properties;

namespace PerfAnalyzer {

  [Serializable]
  public class RecentFile {
    [NonSerialized]public int Number = 0;
    public string Filepath = "";

    [field:NonSerialized]
    [XmlIgnore()]
    public MenuItem MenuItem = null;


    public RecentFile(string filepath, int number = 0) {
      Filepath = filepath;
      Number = number;
    }

    public RecentFile() {
    }

    public string DisplayPath {
      get {
        return Path.Combine(
          Path.GetDirectoryName(Filepath),
          Path.GetFileNameWithoutExtension(Filepath));
      }
    }

    public bool FileExits {
      get {
        return File.Exists(Filepath);
      }
    }

    public override int GetHashCode() {
      return Filepath.GetHashCode();
    }

    public override bool Equals(object other) {
      if(other == null) return false;

      if(other is string) {
        return Filepath == (string)other;
      } else if(other is RecentFile) {
        return Filepath == (other as RecentFile).Filepath;
      }

      return false;
    }
  }

  public class RecentFileList: Separator {

    public ObservableMRUList<RecentFile> RecentFiles { get; private set; }

    public int MaxNumberOfFiles { get; set; }
    public int MaxPathLength { get; set; }
    public MenuItem FileMenu { get; private set; }

    public delegate string GetMenuItemTextDelegate(int index, string filepath);
    public GetMenuItemTextDelegate GetMenuItemTextHandler { get; set; }

    public event Action<string> OpenFile;

    Separator _Separator = null;
    List<MenuItem> MenuItems = null;

    public RecentFileList() {
      RecentFiles = Settings.Default.RecentLogs;

      MenuItems = new List<MenuItem>();

      MaxNumberOfFiles = 9;
      MaxPathLength = 50;

      this.Loaded += (s, e) => HookFileMenu();

      RecentFiles.CollectionChanged += RecentFilesChanged;
    }

    private bool RefreshList = true;

    void RecentFilesChanged(object sender, NotifyCollectionChangedEventArgs e) {
      RefreshList = true;
    }

    void HookFileMenu() {
      MenuItem parent = Parent as MenuItem;
      if(parent == null) throw new ApplicationException("Parent must be a MenuItem");

      if(FileMenu == parent) return;

      if(FileMenu != null) FileMenu.SubmenuOpened -= _FileMenu_SubmenuOpened;

      FileMenu = parent;
      FileMenu.SubmenuOpened += _FileMenu_SubmenuOpened;
    }


    public void RemoveFile(string filepath) {
      RecentFiles.Remove(new RecentFile(filepath));
    }

    public void InsertFile(string filepath) {
      RecentFiles.Add(new RecentFile(filepath));
    }

    void _FileMenu_SubmenuOpened(object sender, RoutedEventArgs e) {
      if(RefreshList) {
        SetMenuItems();
      }
    }

    void SetMenuItems() {
      RemoveMenuItems();

      InsertMenuItems();
    }

    void RemoveMenuItems() {
      if(_Separator != null) FileMenu.Items.Remove(_Separator);

      if(RecentFiles != null)
        foreach(RecentFile r in RecentFiles)
          if(r.MenuItem != null)
            FileMenu.Items.Remove(r.MenuItem);

      _Separator = null;
    }

    void InsertMenuItems() {
      if(RecentFiles == null) return;
      if(RecentFiles.Count == 0) return;

      int iMenuItem = FileMenu.Items.IndexOf(this);

      foreach(RecentFile r in RecentFiles) {
        string header = GetMenuItemText(r.Number + 1, r.Filepath, r.DisplayPath);

        r.MenuItem = new MenuItem { Header = header };
        r.MenuItem.Tag = r;
        r.MenuItem.Click += MenuItem_Click;

        FileMenu.Items.Insert(++iMenuItem, r.MenuItem);
      }

      _Separator = new Separator();
      FileMenu.Items.Insert(++iMenuItem, _Separator);
    }

    string GetMenuItemText(int index, string filepath, string displaypath) {
      GetMenuItemTextDelegate delegateGetMenuItemText = GetMenuItemTextHandler;
      if(delegateGetMenuItemText != null) return delegateGetMenuItemText(index, filepath);

      string shortPath = ShortenPathname(displaypath, MaxPathLength);

      return String.Format("{0}:  {1}", index, shortPath);
    }

    // This method is taken from Joe Woodbury's article at: http://www.codeproject.com/KB/cs/mrutoolstripmenu.aspx

    /// <summary>
    /// Shortens a pathname for display purposes.
    /// </summary>
    /// <param labelName="pathname">The pathname to shorten.</param>
    /// <param labelName="maxLength">The maximum number of characters to be displayed.</param>
    /// <remarks>Shortens a pathname by either removing consecutive components of a path
    /// and/or by removing characters from the end of the filename and replacing
    /// then with three elipses (...)
    /// <para>In all cases, the root of the passed path will be preserved in it's entirety.</para>
    /// <para>If a UNC path is used or the pathname and maxLength are particularly short,
    /// the resulting path may be longer than maxLength.</para>
    /// <para>This method expects fully resolved pathnames to be passed to it.
    /// (Use Path.GetFullPath() to obtain this.)</para>
    /// </remarks>
    /// <returns></returns>
    static public string ShortenPathname(string pathname, int maxLength) {
      if(pathname.Length <= maxLength)
        return pathname;

      string root = Path.GetPathRoot(pathname);
      if(root.Length > 3)
        root += Path.DirectorySeparatorChar;

      string[] elements = pathname.Substring(root.Length).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

      int filenameIndex = elements.GetLength(0) - 1;

      if(elements.GetLength(0) == 1) // pathname is just a root and filename
			{
        if(elements[0].Length > 5) // long enough to shorten
				{
          // if path is a UNC path, root may be rather long
          if(root.Length + 6 >= maxLength) {
            return root + elements[0].Substring(0, 3) + "...";
          } else {
            return pathname.Substring(0, maxLength - 3) + "...";
          }
        }
      } else if((root.Length + 4 + elements[filenameIndex].Length) > maxLength) // pathname is just a root and filename
			{
        root += "...\\";

        int len = elements[filenameIndex].Length;
        if(len < 6)
          return root + elements[filenameIndex];

        if((root.Length + 6) >= maxLength) {
          len = 3;
        } else {
          len = maxLength - root.Length - 3;
        }
        return root + elements[filenameIndex].Substring(0, len) + "...";
      } else if(elements.GetLength(0) == 2) {
        return root + "...\\" + elements[1];
      } else {
        int len = 0;
        int begin = 0;

        for(int i = 0; i < filenameIndex; i++) {
          if(elements[i].Length > len) {
            begin = i;
            len = elements[i].Length;
          }
        }

        int totalLength = pathname.Length - len + 3;
        int end = begin + 1;

        while(totalLength > maxLength) {
          if(begin > 0)
            totalLength -= elements[--begin].Length - 1;

          if(totalLength <= maxLength)
            break;

          if(end < filenameIndex)
            totalLength -= elements[++end].Length - 1;

          if(begin == 0 && end == filenameIndex)
            break;
        }

        // assemble final string

        for(int i = 0; i < begin; i++) {
          root += elements[i] + '\\';
        }

        root += "...\\";

        for(int i = end; i < filenameIndex; i++) {
          root += elements[i] + '\\';
        }

        return root + elements[filenameIndex];
      }
      return pathname;
    }

    void MenuItem_Click(object sender, EventArgs e) {
      MenuItem menuItem = sender as MenuItem;

      OnMenuClick(menuItem);
    }

    protected virtual void OnMenuClick(MenuItem menuItem) {
      string filepath = ((RecentFile)menuItem.Tag).Filepath;

      if(String.IsNullOrEmpty(filepath)) return;

      var dMenuClick = OpenFile;
      if(dMenuClick != null) dMenuClick(filepath);
    }
  }
}
