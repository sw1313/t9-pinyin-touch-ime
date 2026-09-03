using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using T9Pane.Native;
using T9Pane.Services;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace T9Pane.Overlay;

internal partial class T9OverlayWindow
{
    private const int FullUnitSlots = 64;

    private void BuildFullBoard()
    {
        var stage = new Grid();
        var inner = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < FullKeyboardLayout.RowCount; i++)
        {
            inner.RowDefinitions.Add(new RowDefinition());
        }

        var rows = FullKeyboardLayout.Rows(_latin, _fn);
        for (var r = 0; r < rows.Count; r++)
        {
            var line = new Grid();
            for (var i = 0; i < FullUnitSlots; i++)
            {
                line.ColumnDefinitions.Add(new ColumnDefinition());
            }

            var col = 0;
            foreach (var spec in rows[r])
            {
                var span = Math.Max(1, (int)Math.Round(spec.Units * 4));
                var button = MakeFullKey(spec);
                Grid.SetColumn(button, col);
                Grid.SetColumnSpan(button, span);
                line.Children.Add(button);
                col += span;
            }

            Grid.SetRow(line, r);
            inner.Children.Add(line);
        }

        BindSquareHost(stage, inner, FullKeyboardLayout.Units, FullKeyboardLayout.RowCount);
        stage.Children.Add(inner);
        BoardHost.Children.Add(stage);
    }

    private Button MakeFullKey(FullKeySpec spec)
    {
        var button = new Button
        {
            Style = (Style)FindResource("FullKeyButton"),
            FontSize = spec.Action is FullKeyAction.Letter or FullKeyAction.Text or FullKeyAction.Function
                ? 16
                : 13
        };

        if (spec.Action is FullKeyAction.Shift or FullKeyAction.Caps or FullKeyAction.Lang
            or FullKeyAction.Esc or FullKeyAction.Tab or FullKeyAction.Delete
            or FullKeyAction.Ctrl or FullKeyAction.Alt or FullKeyAction.Win
            or FullKeyAction.Symbol or FullKeyAction.Backspace)
        {
            button.Style = (Style)FindResource("FunctionKeyButton");
            button.Margin = new Thickness(2.5);
            button.FontSize = 14;
        }

        if (spec.Action == FullKeyAction.Enter)
        {
            button.Style = (Style)FindResource("PrimaryKeyButton");
            button.Margin = new Thickness(2.5);
            button.FontSize = 14;
        }

        PaintModifier(button, spec);

        var (primary, secondary) = FullKeyboardLayout.Face(
            spec,
            TouchModifierPolicy.IsOn(_shift),
            _caps);
        if (!string.IsNullOrEmpty(secondary))
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = secondary,
                FontSize = 10,
                Opacity = 0.45,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = primary,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            button.Content = stack;
        }
        else
        {
            button.Content = spec.Action switch
            {
                FullKeyAction.Backspace => KeyGlyphs.Backspace(button.Foreground),
                FullKeyAction.Caps => KeyGlyphs.Caps(_caps, button.Foreground),
                FullKeyAction.Shift => KeyGlyphs.Shift(TouchModifierPolicy.IsOn(_shift), button.Foreground),
                FullKeyAction.Enter => KeyGlyphs.Enter(button.Foreground),
                FullKeyAction.Win => KeyGlyphs.WindowsFlag(button.Foreground),
                FullKeyAction.Up => KeyGlyphs.ChevronUp(button.Foreground),
                FullKeyAction.Down => KeyGlyphs.ChevronDown(button.Foreground),
                FullKeyAction.Left => KeyGlyphs.ChevronLeft(button.Foreground),
                FullKeyAction.Right => KeyGlyphs.ChevronRight(button.Foreground),
                FullKeyAction.Predict => MakePredictLabel(button),
                _ => primary
            };
        }

        void Tap() => OnFullKey(spec);
        Action hold = spec.Action == FullKeyAction.Symbol ? OnSymbolTray : Tap;
        BindTap(button, Tap, hold, spec.Label, gestureRegion: false);
        return button;
    }

    private void PaintModifier(Button button, FullKeySpec spec)
    {
        var on = spec.Action switch
        {
            FullKeyAction.Caps => _caps,
            FullKeyAction.Shift => TouchModifierPolicy.IsOn(_shift),
            FullKeyAction.Symbol => _fn,
            FullKeyAction.Ctrl => TouchModifierPolicy.IsOn(_ctrl),
            FullKeyAction.Alt => TouchModifierPolicy.IsOn(_alt),
            FullKeyAction.Win => TouchModifierPolicy.IsOn(_win),
            _ => false
        };
        if (!on)
        {
            if (spec.Action == FullKeyAction.Lang)
            {
                button.Background = (Brush)FindResource("AccentSoftBrush");
                button.Foreground = (Brush)FindResource("AccentBrush");
                button.FontWeight = FontWeights.SemiBold;
            }

            if (spec.Action == FullKeyAction.Predict)
            {
                button.Content = MakePredictLabel(button);
            }

            return;
        }

        button.Background = (Brush)FindResource("AccentBrush");
        button.Foreground = System.Windows.Media.Brushes.White;
        button.FontWeight = FontWeights.SemiBold;
    }

    private void OnFullKey(FullKeySpec spec)
    {
        switch (spec.Action)
        {
            case FullKeyAction.Letter:
                OnFullLetter(spec);
                return;
            case FullKeyAction.Text:
                OnFullText(spec);
                return;
            case FullKeyAction.Function:
                SendResolved(spec);
                return;
            case FullKeyAction.Backspace:
                if (TrySendChord(spec))
                {
                    return;
                }

                OnBackspace();
                return;
            case FullKeyAction.Enter:
                if (TrySendChord(spec))
                {
                    return;
                }

                OnEnter();
                return;
            case FullKeyAction.Space:
                if (TrySendChord(spec))
                {
                    return;
                }

                OnSpace();
                return;
            case FullKeyAction.Shift:
                ToggleModifier(ref _shift, NativeMethods.VkShift, windowsKey: false);
                return;
            case FullKeyAction.Caps:
                _caps = !_caps;
                BuildKeys();
                return;
            case FullKeyAction.Predict:
                ToggleEnglishPredict();
                return;
            case FullKeyAction.Lang:
                _latin = !_latin;
                ResetComposition();
                BuildKeys();
                return;
            case FullKeyAction.Symbol:
                _fn = !_fn;
                BuildKeys();
                return;
            case FullKeyAction.Esc:
            case FullKeyAction.Tab:
            case FullKeyAction.Delete:
            case FullKeyAction.Left:
            case FullKeyAction.Right:
            case FullKeyAction.Up:
            case FullKeyAction.Down:
                SendResolved(spec);
                return;
            case FullKeyAction.Ctrl:
                ToggleModifier(ref _ctrl, NativeMethods.VkControl, windowsKey: false);
                return;
            case FullKeyAction.Alt:
                ToggleModifier(ref _alt, NativeMethods.VkMenu, windowsKey: false);
                return;
            case FullKeyAction.Win:
                ToggleModifier(ref _win, NativeMethods.VkLWin, windowsKey: true);
                return;
        }
    }

    private void OnFullLetter(FullKeySpec spec)
    {
        var letter = spec.Payload ?? spec.Label;
        if (string.IsNullOrEmpty(letter))
        {
            return;
        }

        if (TrySendChord(spec))
        {
            return;
        }

        var ch = char.ToLowerInvariant(letter[0]);
        if (_latin)
        {
            var upper = TouchModifierPolicy.IsOn(_shift) ^ _caps;
            var typed = upper ? char.ToUpperInvariant(ch) : ch;
            if (EnglishPredictOn)
            {
                ComposeLetter(typed);
                ConsumeHeldModifiers();
                return;
            }

            EmitText(typed.ToString());
            ConsumeHeldModifiers();
            return;
        }

        ComposeLetter(ch);
        ConsumeHeldModifiers();
    }

    private FrameworkElement MakePredictLabel(Button button)
    {
        var on = _settings.EnglishPredict;
        var accent = (Brush)FindResource("AccentBrush");
        var muted = (Brush)FindResource("MutedBrush");
        var color = EnglishPredictPolicy.ShowsAccent(on) ? accent : muted;
        button.Foreground = color;
        button.ToolTip = on ? "关闭英文联想" : "开启英文联想";
        return new TextBlock
        {
            Text = EnglishPredictPolicy.Label,
            FontSize = 16,
            TextDecorations = EnglishPredictPolicy.ShowsUnderline(on) ? TextDecorations.Underline : null,
            Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void OnFullText(FullKeySpec spec)
    {
        if (TrySendChord(spec))
        {
            return;
        }

        EmitText(LocalizePunct(spec.Emit(TouchModifierPolicy.IsOn(_shift))));
        ConsumeHeldModifiers();
    }

    private void ToggleModifier(ref TouchModifierPhase phase, ushort vk, bool windowsKey)
    {
        if (TouchModifierPolicy.SecondTapFiresKey(phase, windowsKey))
        {
            phase = TouchModifierPhase.Off;
            ShellCommands.OpenStartMenu();
            BuildKeys();
            return;
        }

        var next = TouchModifierPolicy.Tap(phase, windowsKey);
        if (next == TouchModifierPhase.Held)
        {
            TextOutput.HoldKey(vk);
        }
        else
        {
            TextOutput.ReleaseKey(vk);
        }

        phase = next;
        BuildKeys();
    }

    private bool TrySendChord(FullKeySpec spec)
    {
        if (!FullKeyVirtuals.HasCommandMods(
                TouchModifierPolicy.IsOn(_ctrl),
                TouchModifierPolicy.IsOn(_alt),
                TouchModifierPolicy.IsOn(_win)))
        {
            return false;
        }

        var vk = FullKeyVirtuals.Of(spec);
        if (vk is null)
        {
            return false;
        }

        SendChorded(vk.Value);
        return true;
    }

    private void SendResolved(FullKeySpec spec)
    {
        var vk = FullKeyVirtuals.Of(spec);
        if (vk is null)
        {
            return;
        }

        SendChorded(vk.Value);
    }

    private void SendChorded(ushort vk)
    {
        ShellCommands.AllowExplorerFocus();
        if (TouchModifierPolicy.IsOn(_ctrl)
            || TouchModifierPolicy.IsOn(_alt)
            || TouchModifierPolicy.IsOn(_shift)
            || TouchModifierPolicy.IsOn(_win))
        {
            TextOutput.PulseKey(vk);
        }
        else
        {
            SendVirtual(vk);
        }

        if (TouchModifierPolicy.IsOn(_alt) && ShellCommands.KeepAltForSwitcher(vk))
        {
            ConsumeHeldExceptAlt();
            return;
        }

        ConsumeHeldModifiers();
    }

    private void ConsumeHeldExceptAlt()
    {
        var nextShift = TouchModifierPolicy.Consume(_shift);
        var nextCtrl = TouchModifierPolicy.Consume(_ctrl);
        var nextWin = TouchModifierPolicy.Consume(_win);
        if (nextShift == _shift && nextCtrl == _ctrl && nextWin == _win)
        {
            return;
        }

        if (nextShift != _shift)
        {
            TextOutput.ReleaseKey(NativeMethods.VkShift);
        }

        if (nextCtrl != _ctrl)
        {
            TextOutput.ReleaseKey(NativeMethods.VkControl);
        }

        if (nextWin != _win)
        {
            TextOutput.ReleaseKey(NativeMethods.VkLWin);
        }

        _shift = nextShift;
        _ctrl = nextCtrl;
        _win = nextWin;
        BuildKeys();
    }

    private void ConsumeHeldModifiers()
    {
        var nextShift = TouchModifierPolicy.Consume(_shift);
        var nextCtrl = TouchModifierPolicy.Consume(_ctrl);
        var nextAlt = TouchModifierPolicy.Consume(_alt);
        var nextWin = TouchModifierPolicy.Consume(_win);
        if (nextShift == _shift && nextCtrl == _ctrl && nextAlt == _alt && nextWin == _win)
        {
            return;
        }

        if (nextShift != _shift)
        {
            TextOutput.ReleaseKey(NativeMethods.VkShift);
        }

        if (nextCtrl != _ctrl)
        {
            TextOutput.ReleaseKey(NativeMethods.VkControl);
        }

        if (nextAlt != _alt)
        {
            TextOutput.ReleaseKey(NativeMethods.VkMenu);
        }

        if (nextWin != _win)
        {
            TextOutput.ReleaseKey(NativeMethods.VkLWin);
        }

        _shift = nextShift;
        _ctrl = nextCtrl;
        _alt = nextAlt;
        _win = nextWin;
        BuildKeys();
    }

    private string LocalizePunct(string text)
    {
        if (_latin || text.Length != 1)
        {
            return text;
        }

        return text switch
        {
            "," => "，",
            "." => "。",
            "?" => "？",
            "!" => "！",
            _ => text
        };
    }

    private static void BindSquareHost(
        FrameworkElement host,
        FrameworkElement inner,
        double columns,
        double rows)
    {
        inner.HorizontalAlignment = HorizontalAlignment.Stretch;
        inner.VerticalAlignment = VerticalAlignment.Stretch;
        host.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
            {
                return;
            }

            inner.Width = e.NewSize.Width;
            inner.Height = e.NewSize.Height;
        };
    }
}
