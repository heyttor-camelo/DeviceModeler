using System;
using SW = System.Windows;
using WC = System.Windows.Controls;
using WM = System.Windows.Media;

namespace Modeler.Civil3D.Ui.Common;

/// <summary>Estilos WPF das janelas do HYDRO nos temas claro e escuro: botão, caixa de texto,
/// marcação, ComboBox (com template próprio: o do Windows ignora o fundo e o texto some no tema
/// escuro), linhas e cabeçalho da lista, menus de contexto e separadores. Adaptado do ToolStyles e do
/// painel do Viário (Civil3D2026Plugin).</summary>
internal static class HydroStyles
{
    public static void Apply(SW.ResourceDictionary r, HydroTheme t)
    {
        r.Clear();
        r.MergedDictionaries.Clear();
        r[typeof(WC.Button)] = ButtonStyle(t);

        var check = new SW.Style(typeof(WC.CheckBox));
        check.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        r[typeof(WC.CheckBox)] = check;

        var radio = new SW.Style(typeof(WC.RadioButton));
        radio.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        r[typeof(WC.RadioButton)] = radio;

        var text = new SW.Style(typeof(WC.TextBox));
        text.Setters.Add(new SW.Setter(WC.Control.BackgroundProperty, t.Input));
        text.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        text.Setters.Add(new SW.Setter(WC.Control.BorderBrushProperty, t.Line));
        text.Setters.Add(new SW.Setter(WC.TextBox.CaretBrushProperty, t.Text));
        text.Setters.Add(new SW.Setter(WC.Control.PaddingProperty, new SW.Thickness(3, 1, 3, 1)));
        r[typeof(WC.TextBox)] = text;

        var combo = new SW.Style(typeof(WC.ComboBox));
        combo.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        combo.Setters.Add(new SW.Setter(WC.Control.BackgroundProperty, t.Input));
        combo.Setters.Add(new SW.Setter(WC.Control.BorderBrushProperty, t.Line));
        combo.Setters.Add(new SW.Setter(WC.Control.MinHeightProperty, 22.0));
        if (Parse<WC.ControlTemplate>(ComboXaml(t)) is { } comboTemplate)
        {
            combo.Setters.Add(new SW.Setter(WC.Control.TemplateProperty, comboTemplate));
        }

        r[typeof(WC.ComboBox)] = combo;

        // a lista suspensa do ComboBox usa as cores de sistema "Window": redefinidas para o tema
        r[SW.SystemColors.WindowBrushKey] = t.Pane;
        r[SW.SystemColors.WindowFrameBrushKey] = t.Line;
        r[SW.SystemColors.WindowTextBrushKey] = t.Text;
        r[SW.SystemColors.HighlightBrushKey] = t.Selected;
        r[SW.SystemColors.HighlightTextBrushKey] = t.Text;
        r[SW.SystemColors.ControlBrushKey] = t.Back;
        r[SW.SystemColors.ControlTextBrushKey] = t.Text;

        var comboItem = new SW.Style(typeof(WC.ComboBoxItem));
        comboItem.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        comboItem.Setters.Add(new SW.Setter(WC.Control.BackgroundProperty, t.Pane));
        comboItem.Setters.Add(new SW.Setter(WC.Control.PaddingProperty, new SW.Thickness(6, 3, 6, 3)));
        var itemBorder = new SW.FrameworkElementFactory(typeof(WC.Border), "Bd");
        itemBorder.SetValue(WC.Border.BackgroundProperty, new SW.TemplateBindingExtension(WC.Control.BackgroundProperty));
        itemBorder.SetValue(WC.Border.PaddingProperty, new SW.TemplateBindingExtension(WC.Control.PaddingProperty));
        itemBorder.AppendChild(new SW.FrameworkElementFactory(typeof(WC.ContentPresenter)));
        var itemTemplate = new WC.ControlTemplate(typeof(WC.ComboBoxItem)) { VisualTree = itemBorder };
        itemTemplate.Triggers.Add(Trigger(SW.UIElement.IsMouseOverProperty, true, WC.Border.BackgroundProperty, t.Header, "Bd"));
        itemTemplate.Triggers.Add(Trigger(WC.ComboBoxItem.IsSelectedProperty, true, WC.Border.BackgroundProperty, t.Selected, "Bd"));
        comboItem.Setters.Add(new SW.Setter(WC.Control.TemplateProperty, itemTemplate));
        r[typeof(WC.ComboBoxItem)] = comboItem;

        var header = new SW.Style(typeof(WC.GridViewColumnHeader));
        header.Setters.Add(new SW.Setter(WC.Control.BackgroundProperty, t.Header));
        header.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        header.Setters.Add(new SW.Setter(WC.Control.BorderBrushProperty, t.Line));
        header.Setters.Add(new SW.Setter(WC.Control.PaddingProperty, new SW.Thickness(4, 2, 4, 2)));
        header.Setters.Add(new SW.Setter(WC.Control.HorizontalContentAlignmentProperty, SW.HorizontalAlignment.Left));
        r[typeof(WC.GridViewColumnHeader)] = header;

        // linha da lista com seleção nas cores do tema (o modelo do Windows ignora o tema escuro)
        var rowBorder = new SW.FrameworkElementFactory(typeof(WC.Border), "Bd");
        rowBorder.SetValue(WC.Border.BackgroundProperty, new SW.TemplateBindingExtension(WC.Control.BackgroundProperty));
        rowBorder.SetValue(WC.Border.PaddingProperty, new SW.Thickness(0, 1, 0, 1));
        var presenter = new SW.FrameworkElementFactory(typeof(WC.GridViewRowPresenter));
        presenter.SetValue(WC.GridViewRowPresenter.ColumnsProperty, new SW.TemplateBindingExtension(WC.GridView.ColumnCollectionProperty));
        presenter.SetValue(WC.ContentPresenter.ContentProperty, new SW.TemplateBindingExtension(WC.ContentControl.ContentProperty));
        rowBorder.AppendChild(presenter);
        var rowTemplate = new WC.ControlTemplate(typeof(WC.ListViewItem)) { VisualTree = rowBorder };
        rowTemplate.Triggers.Add(Trigger(SW.UIElement.IsMouseOverProperty, true, WC.Border.BackgroundProperty, t.Header, "Bd"));
        rowTemplate.Triggers.Add(Trigger(WC.Primitives.Selector.IsSelectedProperty, true, WC.Border.BackgroundProperty, t.Selected, "Bd"));
        var item = new SW.Style(typeof(WC.ListViewItem));
        item.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        item.Setters.Add(new SW.Setter(WC.Control.BackgroundProperty, WM.Brushes.Transparent));
        item.Setters.Add(new SW.Setter(WC.Control.TemplateProperty, rowTemplate));
        r[typeof(WC.ListViewItem)] = item;

        if (Parse<SW.ResourceDictionary>(MenuXaml(t)) is { } menus)
        {
            r.MergedDictionaries.Add(menus);
        }
    }

    private static SW.Style ButtonStyle(HydroTheme t)
    {
        var button = new SW.Style(typeof(WC.Button));
        button.Setters.Add(new SW.Setter(WC.Control.BackgroundProperty, t.Header));
        button.Setters.Add(new SW.Setter(WC.Control.ForegroundProperty, t.Text));
        button.Setters.Add(new SW.Setter(WC.Control.BorderBrushProperty, t.Line));
        button.Setters.Add(new SW.Setter(WC.Control.BorderThicknessProperty, new SW.Thickness(1)));
        button.Setters.Add(new SW.Setter(WC.Control.HorizontalContentAlignmentProperty, SW.HorizontalAlignment.Center));
        button.Setters.Add(new SW.Setter(WC.Control.VerticalContentAlignmentProperty, SW.VerticalAlignment.Center));
        // modelo próprio: o cromado padrão pinta o fundo de azul claro no hover e o texto claro some
        var border = new SW.FrameworkElementFactory(typeof(WC.Border), "Bd");
        border.SetValue(WC.Border.BackgroundProperty, new SW.TemplateBindingExtension(WC.Control.BackgroundProperty));
        border.SetValue(WC.Border.BorderBrushProperty, new SW.TemplateBindingExtension(WC.Control.BorderBrushProperty));
        border.SetValue(WC.Border.BorderThicknessProperty, new SW.TemplateBindingExtension(WC.Control.BorderThicknessProperty));
        border.SetValue(WC.Border.PaddingProperty, new SW.TemplateBindingExtension(WC.Control.PaddingProperty));
        border.SetValue(WC.Border.SnapsToDevicePixelsProperty, true);
        var content = new SW.FrameworkElementFactory(typeof(WC.ContentPresenter));
        content.SetValue(SW.FrameworkElement.HorizontalAlignmentProperty, new SW.TemplateBindingExtension(WC.Control.HorizontalContentAlignmentProperty));
        content.SetValue(SW.FrameworkElement.VerticalAlignmentProperty, new SW.TemplateBindingExtension(WC.Control.VerticalContentAlignmentProperty));
        content.SetValue(WC.ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(content);
        var template = new WC.ControlTemplate(typeof(WC.Button)) { VisualTree = border };
        template.Triggers.Add(Trigger(SW.UIElement.IsMouseOverProperty, true, WC.Border.BackgroundProperty, t.Selected, "Bd"));
        template.Triggers.Add(Trigger(WC.Primitives.ButtonBase.IsPressedProperty, true, WC.Border.BackgroundProperty, t.Line, "Bd"));
        var disabled = new SW.Trigger { Property = SW.UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new SW.Setter(SW.UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        button.Setters.Add(new SW.Setter(WC.Control.TemplateProperty, template));
        return button;
    }

    private static SW.Trigger Trigger(SW.DependencyProperty property, object value, SW.DependencyProperty target, object targetValue, string element)
    {
        var trigger = new SW.Trigger { Property = property, Value = value };
        trigger.Setters.Add(new SW.Setter(target, targetValue, element));
        return trigger;
    }

    private static T? Parse<T>(string xaml)
        where T : class
    {
        try
        {
            return (T)System.Windows.Markup.XamlReader.Parse(xaml);
        }
        catch (Exception)
        {
            // XAML recusado: fica o estilo do Windows (com as cores de sistema já trocadas)
            return null;
        }
    }

    private static string ComboXaml(HydroTheme t) => @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>
  <Grid SnapsToDevicePixels='True'>
    <ToggleButton x:Name='Toggle' Focusable='False' ClickMode='Press'
                  IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
      <ToggleButton.Template>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='Bd' Background='{INPUT}' BorderBrush='{LINE}' BorderThickness='1'>
            <Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,7,0'
                  Data='M 0 0 L 4 4 L 8 0' Stroke='{TEXT}' StrokeThickness='1.4'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{ACCENT}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{ACCENT}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter x:Name='Conteudo' IsHitTestVisible='False' Margin='6,2,24,2'
                      VerticalAlignment='Center' HorizontalAlignment='Left'
                      Content='{TemplateBinding SelectionBoxItem}'
                      ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                      ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}'
                      TextElement.Foreground='{TEXT}'/>
    <TextBox x:Name='PART_EditableTextBox' Visibility='Hidden' Margin='1,1,22,1' Padding='4,1,2,1'
             Background='{INPUT}' Foreground='{TEXT}' CaretBrush='{TEXT}' BorderThickness='0'
             VerticalContentAlignment='Center' IsReadOnly='{TemplateBinding IsReadOnly}'/>
    <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' Focusable='False'
           PopupAnimation='None' IsOpen='{TemplateBinding IsDropDownOpen}'>
      <Border Background='{PANE}' BorderBrush='{LINE}' BorderThickness='1'
              MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
              MaxHeight='{TemplateBinding MaxDropDownHeight}'>
        <ScrollViewer SnapsToDevicePixels='True' CanContentScroll='True'>
          <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/>
        </ScrollViewer>
      </Border>
    </Popup>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsEditable' Value='True'>
      <Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/>
      <Setter TargetName='Conteudo' Property='Visibility' Value='Hidden'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>"
        .Replace("{INPUT}", HydroTheme.Hex(t.Input)).Replace("{LINE}", HydroTheme.Hex(t.Line)).Replace("{TEXT}", HydroTheme.Hex(t.Text))
        .Replace("{ACCENT}", HydroTheme.Hex(t.Accent)).Replace("{PANE}", HydroTheme.Hex(t.Pane));

    /// <summary>ContextMenu, MenuItem (com submenu e marca) e Separator no tema: o cromado padrão
    /// (Aero2) tem cores fixas e os submenus ficavam ilegíveis no tema escuro.</summary>
    private static string MenuXaml(HydroTheme t) => @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='MBg' Color='{PANE}'/>
  <SolidColorBrush x:Key='MFg' Color='{TEXT}'/>
  <SolidColorBrush x:Key='MMuted' Color='{MUTED}'/>
  <SolidColorBrush x:Key='MLine' Color='{LINE}'/>
  <SolidColorBrush x:Key='MSel' Color='{SEL}'/>
  <SolidColorBrush x:Key='MGutter' Color='{HEADER}'/>
  <Style TargetType='ContextMenu'>
    <Setter Property='Background' Value='{StaticResource MBg}'/>
    <Setter Property='Foreground' Value='{StaticResource MFg}'/>
    <Setter Property='FontFamily' Value='Tahoma'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{StaticResource MLine}' BorderThickness='1' Padding='2' SnapsToDevicePixels='True'>
            <Grid>
              <Rectangle Width='24' HorizontalAlignment='Left' Fill='{StaticResource MGutter}' Margin='0,-2,0,-2'/>
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Background='{StaticResource MLine}' Margin='28,3,4,3' SnapsToDevicePixels='True'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='Foreground' Value='{StaticResource MFg}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' SnapsToDevicePixels='True'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='24'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='18'/>
              </Grid.ColumnDefinitions>
              <Path x:Name='Check' Data='M0,4 L3.5,7.5 L9.5,0.5' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.8'
                    Width='10' Height='8' Stretch='None' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
              <ContentPresenter x:Name='Icon' ContentSource='Icon' Width='18' Height='18' HorizontalAlignment='Center' VerticalAlignment='Center'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' Margin='6,4,12,4' VerticalAlignment='Center' RecognizesAccessKey='True'/>
              <Path x:Name='Arrow' Grid.Column='2' Data='M0,0 L4,4 L0,8 Z' Fill='{TemplateBinding Foreground}'
                    HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
              <Popup x:Name='PART_Popup' Grid.Column='0' Grid.ColumnSpan='3' Placement='Right' HorizontalOffset='-2' VerticalOffset='-3'
                     IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'
                     AllowsTransparency='True' Focusable='False' PopupAnimation='None'>
                <Border Background='{StaticResource MBg}' BorderBrush='{StaticResource MLine}' BorderThickness='1' Padding='2' SnapsToDevicePixels='True'>
                  <Grid>
                    <Rectangle Width='24' HorizontalAlignment='Left' Fill='{StaticResource MGutter}' Margin='0,-2,0,-2'/>
                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' Grid.IsSharedSizeScope='True'/>
                  </Grid>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='{StaticResource MSel}'/></Trigger>
            <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='Bd' Property='Background' Value='{StaticResource MSel}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{StaticResource MMuted}'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>"
        .Replace("{PANE}", HydroTheme.Hex(t.Pane)).Replace("{TEXT}", HydroTheme.Hex(t.Text)).Replace("{MUTED}", HydroTheme.Hex(t.Muted))
        .Replace("{LINE}", HydroTheme.Hex(t.Line)).Replace("{SEL}", HydroTheme.Hex(t.Selected)).Replace("{HEADER}", HydroTheme.Hex(t.Header));
}
