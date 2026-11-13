import json, re

import os

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "theme")
t = json.load(open(os.path.join(root, "veil-tokens.json"), encoding="utf-8"))

def key(name):  # "bg.canvas" -> "bg.canvas" (dots are legal in x:Key)
    return name

lines = []
a = lines.append
a('<!-- GENERATED from veil-tokens.json by the VEIL theme tooling; edit the JSON, not this file. A unit test checks they agree. -->')
a('<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
a('  <ResourceDictionary.MergedDictionaries />')
a('')
a('  <!-- Colours -->')
for k, v in t["colors"].items():
    a(f'  <Color x:Key="Color.{k}">{v}</Color>')
a('')
a('  <!-- Brushes (one per colour token) -->')
for k in t["colors"]:
    a(f'  <SolidColorBrush x:Key="Brush.{k}" Color="{{StaticResource Color.{k}}}" />')
a('')
a('  <!-- Spacing (device-independent pixels) -->')
for k, v in t["spacing"].items():
    a(f'  <x:Double x:Key="Space.{k}">{v}</x:Double>')
a('')
a('  <!-- Uniform padding / margins for properties that need a Thickness -->')
for k, v in t["spacing"].items():
    a(f'  <Thickness x:Key="Pad.{k}">{v}</Thickness>')
a('')
a('  <!-- Corner radii -->')
for k, v in t["radius"].items():
    a(f'  <CornerRadius x:Key="Radius.{k}">{v}</CornerRadius>')
a('')
a('  <!-- Typography. Sizes are points in the tokens; Avalonia uses device-independent pixels (pt x 96/72). -->')
a(f'  <FontFamily x:Key="Font.Family">{t["font"]["family"]}, {t["font"]["familyFallback"]}</FontFamily>')
a(f'  <FontFamily x:Key="Font.Mono">{t["font"]["mono"]}, {t["font"]["monoFallback"]}</FontFamily>')
for k, v in t["font"]["sizePt"].items():
    px = round(v * 96 / 72, 2)
    a(f'  <x:Double x:Key="Font.Size.{k}">{px}</x:Double>')
a('')
a('  <!-- Motion (milliseconds). Consumers must skip all of it when the reduced-motion preference is on. -->')
for k, v in t["motion"].items():
    if isinstance(v, (int, float)) and not isinstance(v, bool):
        a(f'  <x:Double x:Key="Motion.{k}">{v}</x:Double>')
a('')
a('  <!-- Component metrics -->')
for k, v in t["component"].items():
    if isinstance(v, (int, float)) and not isinstance(v, bool):
        a(f'  <x:Double x:Key="Component.{k}">{v}</x:Double>')
a('</ResourceDictionary>')
open(os.path.join(root, "VeilTokens.axaml"), "w", encoding="utf-8").write("\n".join(lines) + "\n")

# ---- Styles (kept separate so tokens stay a pure data file) -------------------------------------------------------
styles = '''<!-- VEIL control styles. Violet is for fills, borders and glow only; text uses Brush.text.* (see docs/08_DESIGN_TOKENS.md). -->
<Styles xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <Style Selector="Window, UserControl">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.base}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.text.primary}" />
    <Setter Property="FontFamily" Value="{DynamicResource Font.Family}" />
    <Setter Property="FontSize" Value="{DynamicResource Font.Size.body}" />
  </Style>

  <!-- Typography helpers -->
  <Style Selector="TextBlock.title"><Setter Property="FontSize" Value="{DynamicResource Font.Size.title}" /><Setter Property="FontWeight" Value="SemiBold" /></Style>
  <Style Selector="TextBlock.display"><Setter Property="FontSize" Value="{DynamicResource Font.Size.display}" /><Setter Property="FontWeight" Value="SemiBold" /><Setter Property="Foreground" Value="{DynamicResource Brush.accent.lilac}" /></Style>
  <Style Selector="TextBlock.caption"><Setter Property="FontSize" Value="{DynamicResource Font.Size.caption}" /><Setter Property="Foreground" Value="{DynamicResource Brush.text.muted}" /></Style>
  <Style Selector="TextBlock.technical"><Setter Property="FontFamily" Value="{DynamicResource Font.Mono}" /><Setter Property="Foreground" Value="{DynamicResource Brush.accent.cyanDim}" /></Style>

  <!-- Surfaces -->
  <Style Selector="Border.panel">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.border.subtle}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.lg}" />
    <Setter Property="Padding" Value="{DynamicResource Pad.lg}" />
  </Style>
  <Style Selector="Border.hud">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.panel}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.border.strong}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.md}" />
    <Setter Property="BoxShadow" Value="0 0 24 0 #40AB46EE" />
  </Style>

  <!-- Buttons: fill is violet, label is white (6.5:1 on the base fill) -->
  <Style Selector="Button">
    <Setter Property="Background" Value="{DynamicResource Brush.accent.primary}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.text.onAccent}" />
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.pill}" />
    <Setter Property="Padding" Value="20,8" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="BorderThickness" Value="0" />
  </Style>
  <Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="{DynamicResource Brush.accent.primaryHover}" /></Style>
  <Style Selector="Button:pressed /template/ ContentPresenter"><Setter Property="Background" Value="{DynamicResource Brush.accent.primaryActive}" /></Style>
  <Style Selector="Button:focus-visible /template/ ContentPresenter"><Setter Property="BorderBrush" Value="{DynamicResource Brush.border.focus}" /><Setter Property="BorderThickness" Value="2" /></Style>
  <Style Selector="Button.secondary"><Setter Property="Background" Value="{DynamicResource Brush.bg.raised}" /><Setter Property="Foreground" Value="{DynamicResource Brush.text.primary}" /></Style>

  <!-- Inputs -->
  <Style Selector="TextBox">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.raised}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.text.primary}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.border.subtle}" />
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.md}" />
    <Setter Property="CaretBrush" Value="{DynamicResource Brush.accent.cyan}" />
  </Style>
  <Style Selector="TextBox:focus /template/ Border#PART_BorderElement"><Setter Property="BorderBrush" Value="{DynamicResource Brush.border.focus}" /></Style>

  <!-- Window Selector rows (state is also written as a word in the pills, never colour alone) -->
  <Style Selector="ListBoxItem.selector-row">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.surface}" />
    <Setter Property="MinHeight" Value="{DynamicResource Component.selectorRowHeight}" />
    <Setter Property="Padding" Value="12,6" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.border.subtle}" />
    <Setter Property="BorderThickness" Value="0,0,0,1" />
  </Style>
  <Style Selector="ListBoxItem.selector-row:selected /template/ ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.raised}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.border.focus}" />
    <Setter Property="BorderThickness" Value="3,0,0,0" />
  </Style>

  <!-- Status pills -->
  <Style Selector="Border.pill">
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.pill}" />
    <Setter Property="Padding" Value="10,2" />
    <Setter Property="Height" Value="{DynamicResource Component.pillHeight}" />
    <Setter Property="Background" Value="{DynamicResource Brush.status.neutralBg}" />
  </Style>
  <Style Selector="Border.pill > TextBlock"><Setter Property="FontSize" Value="{DynamicResource Font.Size.caption}" /><Setter Property="FontWeight" Value="SemiBold" /><Setter Property="Foreground" Value="{DynamicResource Brush.text.secondary}" /></Style>
  <Style Selector="Border.pill.allowed"><Setter Property="Background" Value="{DynamicResource Brush.status.successBg}" /></Style>
  <Style Selector="Border.pill.allowed > TextBlock"><Setter Property="Foreground" Value="{DynamicResource Brush.status.success}" /></Style>
  <Style Selector="Border.pill.visible"><Setter Property="Background" Value="{DynamicResource Brush.status.infoBg}" /></Style>
  <Style Selector="Border.pill.visible > TextBlock"><Setter Property="Foreground" Value="{DynamicResource Brush.status.info}" /></Style>
  <Style Selector="Border.pill.minimized"><Setter Property="Background" Value="{DynamicResource Brush.status.warningBg}" /></Style>
  <Style Selector="Border.pill.minimized > TextBlock"><Setter Property="Foreground" Value="{DynamicResource Brush.status.warning}" /></Style>
  <Style Selector="Border.pill.error"><Setter Property="Background" Value="{DynamicResource Brush.status.errorBg}" /></Style>
  <Style Selector="Border.pill.error > TextBlock"><Setter Property="Foreground" Value="{DynamicResource Brush.status.error}" /></Style>

  <!-- Status text (always accompanied by words, never colour alone) -->
  <Style Selector="TextBlock.status-success"><Setter Property="Foreground" Value="{DynamicResource Brush.status.success}" /></Style>
  <Style Selector="TextBlock.status-error"><Setter Property="Foreground" Value="{DynamicResource Brush.status.error}" /></Style>
  <Style Selector="TextBlock.status-info"><Setter Property="Foreground" Value="{DynamicResource Brush.status.info}" /></Style>
  <Style Selector="TextBlock.muted-text"><Setter Property="Foreground" Value="{DynamicResource Brush.text.secondary}" /></Style>
  <Style Selector="TextBlock.label"><Setter Property="Foreground" Value="{DynamicResource Brush.text.muted}" /><Setter Property="FontSize" Value="{DynamicResource Font.Size.caption}" /></Style>

  <!-- Settings navigation -->
  <Style Selector="ListBox.nav"><Setter Property="Background" Value="Transparent" /><Setter Property="BorderThickness" Value="0" /></Style>
  <Style Selector="ListBox.nav > ListBoxItem">
    <Setter Property="Padding" Value="16,10" />
    <Setter Property="Margin" Value="8,2" />
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.md}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.text.secondary}" />
  </Style>
  <Style Selector="ListBox.nav > ListBoxItem:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="{DynamicResource Brush.bg.raised}" /></Style>
  <Style Selector="ListBox.nav > ListBoxItem:selected /template/ ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.raised}" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.text.primary}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.border.focus}" />
    <Setter Property="BorderThickness" Value="3,0,0,0" />
  </Style>

  <!-- Editable rows -->
  <Style Selector="Border.rule-row">
    <Setter Property="Background" Value="{DynamicResource Brush.bg.surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.border.subtle}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.md}" />
    <Setter Property="Padding" Value="14,10" />
    <Setter Property="Margin" Value="0,0,0,8" />
  </Style>
  <Style Selector="Border.warning-card">
    <Setter Property="Background" Value="{DynamicResource Brush.status.warningBg}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.status.warning}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{DynamicResource Radius.md}" />
    <Setter Property="Padding" Value="16" />
  </Style>

</Styles>
'''
open(os.path.join(root, "VeilStyles.axaml"), "w", encoding="utf-8").write(styles)
print("generated", len(t["colors"]), "colour tokens")
