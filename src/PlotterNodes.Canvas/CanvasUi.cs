using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace Plotter.Canvas
{
    /// <summary>
    /// Small WPF building blocks for the canvas window and the node, built in code (the package has
    /// no compiled XAML). The control templates are parsed from a XAML string at run time.
    /// </summary>
    internal static class CanvasUi
    {
        internal static readonly Color ToolbarColor = Color.FromRgb(0x2A, 0x2A, 0x2A);
        internal static readonly Color PanelColor = Color.FromRgb(0x33, 0x33, 0x33);
        internal static readonly Color TextColor = Color.FromRgb(0xE6, 0xE6, 0xE6);
        internal static readonly Color SubtleTextColor = Color.FromRgb(0xA8, 0xA8, 0xA8);
        internal static readonly Color AccentColor = Color.FromRgb(0x38, 0xAB, 0xDF);
        internal static readonly Color DynamoBackgroundColor = Color.FromRgb(0xF9, 0xF9, 0xF9);

        private const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='CanvasText' Color='#E6E6E6'/>
  <SolidColorBrush x:Key='CanvasSubtle' Color='#A8A8A8'/>
  <SolidColorBrush x:Key='CanvasButton' Color='#3C3C3C'/>
  <SolidColorBrush x:Key='CanvasButtonHover' Color='#4A4A4A'/>
  <SolidColorBrush x:Key='CanvasButtonPressed' Color='#575757'/>
  <SolidColorBrush x:Key='CanvasAccent' Color='#38ABDF'/>
  <SolidColorBrush x:Key='CanvasBorder' Color='#5A5A5A'/>

  <Style x:Key='CanvasButtonBase' TargetType='{x:Type ButtonBase}'>
    <Setter Property='Foreground' Value='{StaticResource CanvasText}'/>
    <Setter Property='Background' Value='{StaticResource CanvasButton}'/>
    <Setter Property='BorderBrush' Value='{StaticResource CanvasBorder}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='9,3'/>
    <Setter Property='Margin' Value='2,0'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
  </Style>

  <Style TargetType='{x:Type Button}' BasedOn='{StaticResource CanvasButtonBase}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type Button}'>
          <Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='3' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Chrome' Property='Background' Value='{StaticResource CanvasButtonHover}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='Chrome' Property='Background' Value='{StaticResource CanvasButtonPressed}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='{x:Type ToggleButton}' BasedOn='{StaticResource CanvasButtonBase}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ToggleButton}'>
          <Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='3' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Chrome' Property='Background' Value='{StaticResource CanvasButtonHover}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Chrome' Property='Background' Value='{StaticResource CanvasAccent}'/>
              <Setter TargetName='Chrome' Property='BorderBrush' Value='{StaticResource CanvasAccent}'/>
              <Setter Property='Foreground' Value='#FFFFFF'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='{x:Type CheckBox}'>
    <Setter Property='Foreground' Value='{StaticResource CanvasText}'/>
    <Setter Property='Margin' Value='0,4'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Focusable' Value='False'/>
  </Style>
  <Style TargetType='{x:Type TextBlock}'>
    <Setter Property='Foreground' Value='{StaticResource CanvasText}'/>
    <Setter Property='FontSize' Value='12'/>
  </Style>
  <Style TargetType='{x:Type Slider}'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsMoveToPointEnabled' Value='True'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
  </Style>
  <Style TargetType='{x:Type ComboBox}'>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Focusable' Value='False'/>
  </Style>
</ResourceDictionary>";

        private static ResourceDictionary styles;

        /// <summary>The control styles, parsed once.</summary>
        internal static ResourceDictionary Styles
        {
            get
            {
                if (styles == null)
                {
                    styles = (ResourceDictionary)XamlReader.Parse(StylesXaml);
                }
                return styles;
            }
        }

        internal static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        internal static Button Button(string text, string tooltip, Action click)
        {
            var button = new Button { Content = text, ToolTip = tooltip };
            button.Click += (s, e) => click();
            return button;
        }

        internal static ToggleButton Toggle(string text, string tooltip, bool isChecked, Action<bool> changed)
        {
            var toggle = new ToggleButton { Content = text, ToolTip = tooltip, IsChecked = isChecked };
            toggle.Checked += (s, e) => changed(true);
            toggle.Unchecked += (s, e) => changed(false);
            return toggle;
        }

        internal static CheckBox Check(string text, string tooltip, bool isChecked, Action<bool> changed)
        {
            var check = new CheckBox { Content = text, ToolTip = tooltip, IsChecked = isChecked };
            check.Checked += (s, e) => changed(true);
            check.Unchecked += (s, e) => changed(false);
            return check;
        }

        internal static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, 14, 0, 4),
            };
        }

        internal static TextBlock Note(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brush(SubtleTextColor),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 2),
            };
        }

        /// <summary>A labeled slider with its value on the right.</summary>
        internal static FrameworkElement Slider(string label, string tooltip, double min, double max, double value,
            Func<double, string> format, Action<double> changed, out Slider slider)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = tooltip };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var s = new Slider { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)) };
            var text = new TextBlock
            {
                Text = format(s.Value),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = Brush(SubtleTextColor),
            };
            s.ValueChanged += (o, e) =>
            {
                text.Text = format(e.NewValue);
                changed(e.NewValue);
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(s, 1);
            Grid.SetColumn(text, 2);
            grid.Children.Add(name);
            grid.Children.Add(s);
            grid.Children.Add(text);
            slider = s;
            return grid;
        }

        /// <summary>A labeled combo box.</summary>
        internal static FrameworkElement Combo(string label, string tooltip, IList<string> items, int index,
            Action<int> changed, out ComboBox combo)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = tooltip };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var box = new ComboBox { ItemsSource = items, SelectedIndex = index };
            box.SelectionChanged += (o, e) =>
            {
                if (box.SelectedIndex >= 0)
                {
                    changed(box.SelectedIndex);
                }
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(box, 1);
            grid.Children.Add(name);
            grid.Children.Add(box);
            combo = box;
            return grid;
        }

        /// <summary>A row of color swatches; the selected one gets the accent border.</summary>
        internal static FrameworkElement Swatches(string label, string tooltip, IList<KeyValuePair<string, string>> colors,
            string current, Action<string> changed)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = tooltip };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var borders = new List<Border>();
            foreach (var pair in colors)
            {
                var hex = pair.Value;
                var swatch = new Border
                {
                    Width = 22,
                    Height = 18,
                    Margin = new Thickness(0, 0, 5, 0),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(2),
                    Background = Brush(ParseColor(hex, Colors.White)),
                    ToolTip = pair.Key,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = hex,
                };
                borders.Add(swatch);
                swatch.MouseLeftButtonUp += (o, e) =>
                {
                    foreach (var b in borders)
                    {
                        b.BorderBrush = Brush(string.Equals((string)b.Tag, hex, StringComparison.OrdinalIgnoreCase) ? AccentColor : Color.FromRgb(0x5A, 0x5A, 0x5A));
                    }
                    changed(hex);
                };
                swatch.BorderBrush = Brush(string.Equals(hex, current, StringComparison.OrdinalIgnoreCase) ? AccentColor : Color.FromRgb(0x5A, 0x5A, 0x5A));
                row.Children.Add(swatch);
            }
            Grid.SetColumn(name, 0);
            Grid.SetColumn(row, 1);
            grid.Children.Add(name);
            grid.Children.Add(row);
            return grid;
        }

        internal static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return fallback;
            }
            var s = hex.Trim().TrimStart('#');
            uint value;
            if ((s.Length == 6 || s.Length == 8) && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                if (s.Length == 6)
                {
                    return Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
                }
                return Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            }
            return fallback;
        }

        internal static string Percent(double value)
        {
            return (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        internal static string Number(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
