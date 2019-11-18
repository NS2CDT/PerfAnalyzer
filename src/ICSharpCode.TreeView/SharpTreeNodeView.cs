// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.ComponentModel;
using System.Collections.Specialized;

namespace ICSharpCode.TreeView
{
	public class SharpTreeNodeView : Control
	{
		static SharpTreeNodeView()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(SharpTreeNodeView),
			                                         new FrameworkPropertyMetadata(typeof(SharpTreeNodeView)));
        }

        public SharpTreeNodeView() {
            HorizontalAlignment = HorizontalAlignment.Left;
        }

		public static readonly DependencyProperty TextBackgroundProperty =
			DependencyProperty.Register("TextBackground", typeof(Brush), typeof(SharpTreeNodeView));

		public Brush TextBackground
		{
			get { return (Brush)GetValue(TextBackgroundProperty); }
			set { SetValue(TextBackgroundProperty, value); }
		}

		public SharpTreeNode Node
		{
			get { return DataContext as SharpTreeNode; }
		}

		public SharpTreeViewItem ParentItem { get; private set; }

		public static readonly DependencyProperty CellEditorProperty =
			DependencyProperty.Register("CellEditor", typeof(Control), typeof(SharpTreeNodeView),
			                            new FrameworkPropertyMetadata());

		public Control CellEditor {
			get { return (Control)GetValue(CellEditorProperty); }
			set { SetValue(CellEditorProperty, value); }
		}

		public SharpTreeView ParentTreeView
		{
			get { return ParentItem.ParentTreeView; }
		}

		internal LinesRenderer LinesRenderer { get; private set; }

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			LinesRenderer = Template.FindName("linesRenderer", this) as LinesRenderer;
			UpdateTemplate();
		}

		protected override void OnVisualParentChanged(DependencyObject oldParent)
		{
			base.OnVisualParentChanged(oldParent);
			ParentItem = this.FindAncestor<SharpTreeViewItem>();
			ParentItem.NodeView = this;
		}

		protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			base.OnPropertyChanged(e);
			if (e.Property == DataContextProperty) {
				UpdateDataContext(e.OldValue as SharpTreeNode, e.NewValue as SharpTreeNode);
                if (ParentItem != null) {
                    ((GridView)ParentTreeView?.View).Columns[0].Width = 0;
                    ((GridView)ParentTreeView?.View).Columns[0].Width = double.NaN;
                    InvalidateMeasure();
                }
            }
		}

		void UpdateDataContext(SharpTreeNode oldNode, SharpTreeNode newNode)
		{
			if (newNode != null) {
				newNode.PropertyChanged += Node_PropertyChanged;
				if (Template != null) {
					UpdateTemplate();
				}
			}
			if (oldNode != null) {
				oldNode.PropertyChanged -= Node_PropertyChanged;
			}
		}

		void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "IsEditing") {
				OnIsEditingChanged();
			} else if (e.PropertyName == "IsLast") {
				if (ParentTreeView.ShowLines) {
					foreach (var child in Node.VisibleDescendantsAndSelf()) {
						var container = ParentTreeView.ItemContainerGenerator.ContainerFromItem(child) as SharpTreeViewItem;
						if (container != null) {
							container.NodeView.LinesRenderer.InvalidateVisual();
						}
					}
				}
			} else if (e.PropertyName == "IsExpanded") {
				if (Node.IsExpanded)
					ParentTreeView.HandleExpanding(Node);
                LinesRenderer.InvalidateVisual();
			}
		}

		void OnIsEditingChanged()
		{
			var textEditorContainer = Template.FindName("textEditorContainer", this) as Border;
			if (Node.IsEditing) {
				if (CellEditor == null)
					textEditorContainer.Child = new EditTextBox() { Item = ParentItem };
				else
					textEditorContainer.Child = CellEditor;
			}
			else {
				textEditorContainer.Child = null;
			}
		}

		void UpdateTemplate()
		{
			var spacer = Template.FindName("spacer", this) as FrameworkElement;
			spacer.Width = CalculateIndent();

			var expander = Template.FindName("expander", this) as ToggleButton;
			if (ParentTreeView.Root == Node && !ParentTreeView.ShowRootExpander) {
				expander.Visibility = Visibility.Collapsed;
			}
			else {
				expander.ClearValue(VisibilityProperty);
			}
    }

        bool HasCheckBoxes = false;

        internal double CalculateIndent()
		{
            var checkboxExtra = (HasCheckBoxes ? 9 : 0);

			var result = (10 + checkboxExtra) * Node.Level;
			if (ParentTreeView.ShowRoot) {
				if (!ParentTreeView.ShowRootExpander) {
					if (ParentTreeView.Root != Node) {
						result -= 15;
					}
				}
			}
			else {
				result -= (10 + checkboxExtra);
			}
			if (result < 0) {
				Debug.WriteLine("Negative indent level detected for node " + Node);
				result = 0;
			}
			return result;
		}

        protected override Size MeasureOverride(Size arrangeBounds) {
            var child = VisualTreeHelper.GetChild(this, 0) as Panel;
            child.Measure(new Size(double.PositiveInfinity, arrangeBounds.Height));
         //   MinWidth = child.DesiredSize.Width;
            return new Size(child.DesiredSize.Width, child.DesiredSize.Height);
        }
    }
}
