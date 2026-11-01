// WPF API surface used by PlotterNodes.Canvas (signatures as in .NET 10 WindowsDesktop).
// Stub bodies only; see ../README.md.
#pragma warning disable CS0067, CS0108, CS0114, CS1591, CS8625, CS0649
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace System.Windows.Threading
{
    public enum DispatcherPriority { Invalid = -1, Inactive = 0, SystemIdle = 1, ApplicationIdle = 2, ContextIdle = 3, Background = 4, Input = 5, Loaded = 6, Render = 7, DataBind = 8, Normal = 9, Send = 10 }

    public sealed class DispatcherOperation { }

    public sealed class Dispatcher
    {
        public DispatcherOperation BeginInvoke(Delegate method, params object[] args) { throw null; }
        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate method) { throw null; }
        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate method, object arg) { throw null; }
        public DispatcherOperation BeginInvoke(Delegate method, DispatcherPriority priority, params object[] args) { throw null; }
        public bool CheckAccess() { throw null; }
    }

    public abstract class DispatcherObject
    {
        public Dispatcher Dispatcher { get { throw null; } }
        public bool CheckAccess() { throw null; }
    }

    public class DispatcherTimer
    {
        public DispatcherTimer() { }
        public DispatcherTimer(DispatcherPriority priority) { }
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; set; }
        public event EventHandler Tick;
        public void Start() { }
        public void Stop() { }
    }
}

namespace System.Windows
{
    public class DependencyObject : DispatcherObject
    {
        public object GetValue(DependencyProperty dp) { throw null; }
        public void SetValue(DependencyProperty dp, object value) { }
    }

    public sealed class DependencyProperty { }

    public abstract class Freezable : DependencyObject
    {
        public void Freeze() { }
        public bool IsFrozen { get { throw null; } }
    }

    public abstract class Visual : DependencyObject { }

    public enum Visibility { Visible = 0, Hidden = 1, Collapsed = 2 }
    public enum HorizontalAlignment { Left = 0, Center = 1, Right = 2, Stretch = 3 }
    public enum VerticalAlignment { Top = 0, Center = 1, Bottom = 2, Stretch = 3 }
    public enum TextTrimming { None = 0, CharacterEllipsis = 1, WordEllipsis = 2 }
    public enum TextWrapping { WrapWithOverflow = 0, NoWrap = 1, Wrap = 2 }
    public enum WindowState { Normal = 0, Minimized = 1, Maximized = 2 }
    public enum WindowStyle { None = 0, SingleBorderWindow = 1, ThreeDBorderWindow = 2, ToolWindow = 3 }
    public enum ResizeMode { NoResize = 0, CanMinimize = 1, CanResize = 2, CanResizeWithGrip = 3 }
    public enum WindowStartupLocation { Manual = 0, CenterScreen = 1, CenterOwner = 2 }
    public enum GridUnitType { Auto = 0, Pixel = 1, Star = 2 }

    public struct Thickness
    {
        public Thickness(double uniformLength) { throw null; }
        public Thickness(double left, double top, double right, double bottom) { throw null; }
    }

    public struct CornerRadius
    {
        public CornerRadius(double uniformRadius) { throw null; }
    }

    public struct GridLength
    {
        public GridLength(double pixels) { throw null; }
        public GridLength(double value, GridUnitType type) { throw null; }
    }

    public struct FontWeight { }

    public static class FontWeights
    {
        public static FontWeight Normal { get { throw null; } }
        public static FontWeight SemiBold { get { throw null; } }
        public static FontWeight Bold { get { throw null; } }
    }

    public struct Point
    {
        public Point(double x, double y) { throw null; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class RoutedEventArgs : EventArgs
    {
        public bool Handled { get; set; }
        public object Source { get; set; }
    }

    public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);

    public class RoutedPropertyChangedEventArgs<T> : RoutedEventArgs
    {
        public T OldValue { get { throw null; } }
        public T NewValue { get { throw null; } }
    }

    public delegate void RoutedPropertyChangedEventHandler<T>(object sender, RoutedPropertyChangedEventArgs<T> e);

    public class ResourceDictionary : IDictionary
    {
        public Collection<ResourceDictionary> MergedDictionaries { get { throw null; } }
        public object this[object key] { get { throw null; } set { } }
        public bool IsFixedSize { get { throw null; } }
        public bool IsReadOnly { get { throw null; } }
        public ICollection Keys { get { throw null; } }
        public ICollection Values { get { throw null; } }
        public int Count { get { throw null; } }
        public bool IsSynchronized { get { throw null; } }
        public object SyncRoot { get { throw null; } }
        public void Add(object key, object value) { }
        public void Clear() { }
        public bool Contains(object key) { throw null; }
        public void CopyTo(Array array, int index) { }
        public IDictionaryEnumerator GetEnumerator() { throw null; }
        public void Remove(object key) { }
        IEnumerator IEnumerable.GetEnumerator() { throw null; }
    }

    public class UIElement : Visual
    {
        public Visibility Visibility { get; set; }
        public bool Focusable { get; set; }
        public bool IsEnabled { get; set; }
        public double Opacity { get; set; }
        public bool IsMouseCaptured { get { throw null; } }
        public InputBindingCollection InputBindings { get { throw null; } }
        public bool Focus() { throw null; }
        public event MouseButtonEventHandler MouseDown;
        public event MouseButtonEventHandler MouseUp;
        public event MouseButtonEventHandler MouseLeftButtonUp;
        public event KeyEventHandler PreviewKeyDown;
    }

    public class FrameworkElement : UIElement
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double MinWidth { get; set; }
        public double MinHeight { get; set; }
        public double ActualWidth { get { throw null; } }
        public double ActualHeight { get { throw null; } }
        public Thickness Margin { get; set; }
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public VerticalAlignment VerticalAlignment { get; set; }
        public object ToolTip { get; set; }
        public ResourceDictionary Resources { get; set; }
        public Cursor Cursor { get; set; }
        public object Tag { get; set; }
        public string Name { get; set; }
        public object DataContext { get; set; }
        public event RoutedEventHandler Loaded;
        public event RoutedEventHandler Unloaded;
    }

    public class FrameworkContentElement : DependencyObject
    {
        public string Name { get; set; }
        public object Tag { get; set; }
        public DependencyObject Parent { get { throw null; } }
    }

    public class Window : System.Windows.Controls.ContentControl
    {
        public string Title { get; set; }
        public bool ShowInTaskbar { get; set; }
        public bool Topmost { get; set; }
        public bool ShowActivated { get; set; }
        public WindowStartupLocation WindowStartupLocation { get; set; }
        public WindowState WindowState { get; set; }
        public WindowStyle WindowStyle { get; set; }
        public ResizeMode ResizeMode { get; set; }
        public Window Owner { get; set; }
        public bool Activate() { throw null; }
        public void Close() { }
        public void Show() { }
        public event EventHandler SourceInitialized;
        public event CancelEventHandler Closing;
        public event EventHandler Closed;
    }

    public class Application : DispatcherObject
    {
        public static Application Current { get { throw null; } }
        public Window MainWindow { get; set; }
    }
}

namespace System.Windows.Media
{
    public struct Color
    {
        public byte A { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public static Color FromRgb(byte r, byte g, byte b) { throw null; }
        public static Color FromArgb(byte a, byte r, byte g, byte b) { throw null; }
    }

    public sealed class Colors
    {
        public static Color White { get { throw null; } }
        public static Color Black { get { throw null; } }
    }

    public abstract class Brush : Freezable { }

    public sealed class SolidColorBrush : Brush
    {
        public SolidColorBrush() { }
        public SolidColorBrush(Color color) { }
        public Color Color { get; set; }
    }
}

namespace System.Windows.Media.Animation
{
    public abstract class Animatable : Freezable { }
}

namespace System.Windows.Media.Media3D
{
    public struct Point3D
    {
        public Point3D(double x, double y, double z) { throw null; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public static Point3D operator -(Point3D point, Vector3D vector) { throw null; }
        public static Point3D operator +(Point3D point, Vector3D vector) { throw null; }
        public static Vector3D operator -(Point3D point1, Point3D point2) { throw null; }
    }

    public struct Vector3D
    {
        public Vector3D(double x, double y, double z) { throw null; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Length { get { throw null; } }
        public void Normalize() { }
        public static Vector3D operator *(Vector3D vector, double scalar) { throw null; }
        public static Vector3D operator *(double scalar, Vector3D vector) { throw null; }
        public static Vector3D operator -(Vector3D vector) { throw null; }
    }
}

namespace System.Windows.Markup
{
    public class XamlReader
    {
        public static object Parse(string xamlText) { throw null; }
    }
}

namespace System.Windows.Interop
{
    public sealed class WindowInteropHelper
    {
        public WindowInteropHelper(Window window) { }
        public IntPtr Handle { get { throw null; } }
    }
}

namespace System.Windows.Input
{
    public enum Key { None = 0, A = 44, E = 48, F = 49, Escape = 13, F11 = 100 }

    [Flags]
    public enum ModifierKeys { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

    public enum MouseAction { None = 0, LeftClick = 1, RightClick = 2, MiddleClick = 3, WheelClick = 4, LeftDoubleClick = 5, RightDoubleClick = 6, MiddleDoubleClick = 7 }

    public enum MouseButton { Left = 0, Middle = 1, Right = 2, XButton1 = 3, XButton2 = 4 }

    public static class Keyboard
    {
        public static ModifierKeys Modifiers { get { throw null; } }
    }

    public sealed class Cursor { }

    public static class Cursors
    {
        public static Cursor Hand { get { throw null; } }
        public static Cursor Arrow { get { throw null; } }
    }

    public interface IInputElement { }

    public class InputEventArgs : RoutedEventArgs { }

    public class KeyEventArgs : InputEventArgs
    {
        public Key Key { get { throw null; } }
    }

    public delegate void KeyEventHandler(object sender, KeyEventArgs e);

    public class MouseEventArgs : InputEventArgs
    {
        public Point GetPosition(IInputElement relativeTo) { throw null; }
    }

    public class MouseButtonEventArgs : MouseEventArgs
    {
        public MouseButton ChangedButton { get { throw null; } }
        public int ClickCount { get { throw null; } }
    }

    public delegate void MouseButtonEventHandler(object sender, MouseButtonEventArgs e);

    public class RoutedCommand : ICommand
    {
        public event EventHandler CanExecuteChanged;
        bool ICommand.CanExecute(object parameter) { throw null; }
        void ICommand.Execute(object parameter) { }
    }

    public class InputGesture { }

    public class MouseGesture : InputGesture
    {
        public MouseGesture(MouseAction mouseAction) { }
        public MouseGesture(MouseAction mouseAction, ModifierKeys modifiers) { }
    }

    public class InputBinding : Freezable { }

    public class KeyBinding : InputBinding
    {
        public KeyBinding(ICommand command, Key key, ModifierKeys modifiers) { }
    }

    public class MouseBinding : InputBinding
    {
        public MouseBinding(ICommand command, MouseGesture gesture) { }
    }

    public sealed class InputBindingCollection : IList
    {
        public int Add(InputBinding inputBinding) { throw null; }
        int IList.Add(object value) { throw null; }
        public bool IsFixedSize { get { throw null; } }
        public bool IsReadOnly { get { throw null; } }
        public object this[int index] { get { throw null; } set { } }
        public int Count { get { throw null; } }
        public bool IsSynchronized { get { throw null; } }
        public object SyncRoot { get { throw null; } }
        public void Clear() { }
        public bool Contains(object value) { throw null; }
        public void CopyTo(Array array, int index) { }
        public IEnumerator GetEnumerator() { throw null; }
        public int IndexOf(object value) { throw null; }
        public void Insert(int index, object value) { }
        public void Remove(object value) { }
        public void RemoveAt(int index) { }
    }
}

namespace System.Windows.Controls
{
    public enum Orientation { Horizontal = 0, Vertical = 1 }
    public enum Dock { Left = 0, Top = 1, Right = 2, Bottom = 3 }
    public enum ScrollBarVisibility { Disabled = 0, Auto = 1, Hidden = 2, Visible = 3 }

    public class Control : FrameworkElement
    {
        public Brush Background { get; set; }
        public Brush Foreground { get; set; }
        public Brush BorderBrush { get; set; }
        public Thickness BorderThickness { get; set; }
        public Thickness Padding { get; set; }
        public double FontSize { get; set; }
        public FontWeight FontWeight { get; set; }
    }

    public class ContentControl : Control
    {
        public object Content { get; set; }
    }

    public class UIElementCollection : IList
    {
        public int Add(UIElement element) { throw null; }
        int IList.Add(object value) { throw null; }
        public bool IsFixedSize { get { throw null; } }
        public bool IsReadOnly { get { throw null; } }
        public object this[int index] { get { throw null; } set { } }
        public int Count { get { throw null; } }
        public bool IsSynchronized { get { throw null; } }
        public object SyncRoot { get { throw null; } }
        public void Clear() { }
        public bool Contains(object value) { throw null; }
        public void CopyTo(Array array, int index) { }
        public IEnumerator GetEnumerator() { throw null; }
        public int IndexOf(object value) { throw null; }
        public void Insert(int index, object value) { }
        public void Remove(object value) { }
        public void RemoveAt(int index) { }
    }

    public abstract class Panel : FrameworkElement
    {
        public Brush Background { get; set; }
        public UIElementCollection Children { get { throw null; } }
    }

    public class StackPanel : Panel
    {
        public Orientation Orientation { get; set; }
    }

    public class DockPanel : Panel
    {
        public bool LastChildFill { get; set; }
        public static void SetDock(UIElement element, Dock dock) { }
    }

    public class ColumnDefinition : FrameworkContentElement
    {
        public GridLength Width { get; set; }
    }

    public sealed class ColumnDefinitionCollection : IList<ColumnDefinition>
    {
        public ColumnDefinition this[int index] { get { throw null; } set { } }
        public int Count { get { throw null; } }
        public bool IsReadOnly { get { throw null; } }
        public void Add(ColumnDefinition value) { }
        public void Clear() { }
        public bool Contains(ColumnDefinition value) { throw null; }
        public void CopyTo(ColumnDefinition[] array, int index) { }
        public IEnumerator<ColumnDefinition> GetEnumerator() { throw null; }
        public int IndexOf(ColumnDefinition value) { throw null; }
        public void Insert(int index, ColumnDefinition value) { }
        public bool Remove(ColumnDefinition value) { throw null; }
        public void RemoveAt(int index) { }
        IEnumerator IEnumerable.GetEnumerator() { throw null; }
    }

    public class Grid : Panel
    {
        public ColumnDefinitionCollection ColumnDefinitions { get { throw null; } }
        public static void SetColumn(UIElement element, int value) { }
        public static void SetRow(UIElement element, int value) { }
    }

    public class Decorator : FrameworkElement
    {
        public virtual UIElement Child { get; set; }
    }

    public class Border : Decorator
    {
        public Brush Background { get; set; }
        public Brush BorderBrush { get; set; }
        public Thickness BorderThickness { get; set; }
        public CornerRadius CornerRadius { get; set; }
        public Thickness Padding { get; set; }
    }

    public class TextBlock : FrameworkElement
    {
        public string Text { get; set; }
        public Brush Foreground { get; set; }
        public FontWeight FontWeight { get; set; }
        public double FontSize { get; set; }
        public TextWrapping TextWrapping { get; set; }
        public TextTrimming TextTrimming { get; set; }
    }

    public class Button : System.Windows.Controls.Primitives.ButtonBase { }

    public class CheckBox : System.Windows.Controls.Primitives.ToggleButton { }

    public class ScrollViewer : ContentControl
    {
        public ScrollBarVisibility VerticalScrollBarVisibility { get; set; }
        public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; }
    }

    public sealed class ItemCollection : IList
    {
        public int Add(object newItem) { throw null; }
        public bool IsFixedSize { get { throw null; } }
        public bool IsReadOnly { get { throw null; } }
        public object this[int index] { get { throw null; } set { } }
        public int Count { get { throw null; } }
        public bool IsSynchronized { get { throw null; } }
        public object SyncRoot { get { throw null; } }
        public void Clear() { }
        public bool Contains(object value) { throw null; }
        public void CopyTo(Array array, int index) { }
        public IEnumerator GetEnumerator() { throw null; }
        public int IndexOf(object value) { throw null; }
        public void Insert(int index, object value) { }
        public void Remove(object value) { }
        public void RemoveAt(int index) { }
    }

    public class ItemsControl : Control
    {
        public ItemCollection Items { get { throw null; } }
        public IEnumerable ItemsSource { get; set; }
    }

    public class HeaderedItemsControl : ItemsControl
    {
        public object Header { get; set; }
    }

    public class MenuItem : HeaderedItemsControl
    {
        public bool IsCheckable { get; set; }
        public bool IsChecked { get; set; }
        public event RoutedEventHandler Click;
    }

    public abstract class MenuBase : ItemsControl { }

    public class ContextMenu : MenuBase
    {
        public UIElement PlacementTarget { get; set; }
        public System.Windows.Controls.Primitives.PlacementMode Placement { get; set; }
        public bool IsOpen { get; set; }
    }

    public class Separator : Control { }

    public class SelectionChangedEventArgs : RoutedEventArgs { }

    public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs e);

    public class ComboBox : System.Windows.Controls.Primitives.Selector { }

    public class Slider : System.Windows.Controls.Primitives.RangeBase
    {
        public bool IsMoveToPointEnabled { get; set; }
    }
}

namespace System.Windows.Controls.Primitives
{
    public enum PlacementMode { Absolute = 0, Relative = 1, Bottom = 2, Center = 3, Right = 4, AbsolutePoint = 5, RelativePoint = 6, Mouse = 7, MousePoint = 8, Left = 9, Top = 10, Custom = 11 }

    public abstract class ButtonBase : System.Windows.Controls.ContentControl
    {
        public bool IsPressed { get { throw null; } }
        public event RoutedEventHandler Click;
    }

    public class ToggleButton : ButtonBase
    {
        public bool? IsChecked { get; set; }
        public event RoutedEventHandler Checked;
        public event RoutedEventHandler Unchecked;
    }

    public abstract class Selector : System.Windows.Controls.ItemsControl
    {
        public int SelectedIndex { get; set; }
        public event System.Windows.Controls.SelectionChangedEventHandler SelectionChanged;
    }

    public abstract class RangeBase : System.Windows.Controls.Control
    {
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Value { get; set; }
        public event RoutedPropertyChangedEventHandler<double> ValueChanged;
    }
}
