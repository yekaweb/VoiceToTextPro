using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MaterialDesignThemes.Wpf;

namespace VoiceToTextPro.Services
{
    public static class ModernDialogService
    {
        public static MessageBoxResult Show(
            string message,
            string title = "اطلاع",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information,
            Window? owner = null)
        {
            MessageBoxResult dialogResult = button == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;

            // Resolve owner window safely
            Window? parentWindow = owner 
                ?? Application.Current?.Dispatcher?.Invoke(() => 
                {
                    if (Application.Current?.Windows != null)
                    {
                        foreach (Window w in Application.Current.Windows)
                        {
                            if (w.IsActive) return w;
                        }
                    }
                    return Application.Current?.MainWindow;
                });

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var flowDir = LanguageManager.Instance.CurrentCulture == "fa-IR" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                var dlg = new Window
                {
                    Title = title,
                    Width = 460,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    FlowDirection = flowDir,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                if (parentWindow != null && parentWindow.IsVisible)
                {
                    dlg.Owner = parentWindow;
                }

                // Determine Accent Color & Icon Pack
                PackIconKind iconKind = PackIconKind.Information;
                Brush headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")); // Indigo Accent

                switch (icon)
                {
                    case MessageBoxImage.Error:
                        iconKind = PackIconKind.CloseCircle;
                        headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
                        break;
                    case MessageBoxImage.Warning:
                        iconKind = PackIconKind.Alert;
                        headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Amber
                        break;
                    case MessageBoxImage.Question:
                        iconKind = PackIconKind.HelpCircle;
                        headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")); // Sky Blue
                        break;
                    case MessageBoxImage.Information:
                    default:
                        iconKind = PackIconKind.Information;
                        headerBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")); // Purple
                        break;
                }

                // Main Outer Card Container
                var cardBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")), // Deep Slate
                    BorderBrush = headerBrush,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(12),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 24,
                        Opacity = 0.65,
                        ShadowDepth = 6
                    }
                };

                var rootGrid = new Grid();
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer Buttons

                // ── Header Bar ──
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    Padding = new Thickness(16, 12, 16, 12),
                    CornerRadius = new CornerRadius(10, 10, 0, 0)
                };

                var headerGrid = new Grid();

                var headerSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var packIcon = new PackIcon
                {
                    Kind = iconKind,
                    Foreground = headerBrush,
                    Width = 22,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                headerSp.Children.Add(packIcon);

                var titleBlock = new TextBlock
                {
                    Text = title,
                    Foreground = Brushes.White,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerSp.Children.Add(titleBlock);
                headerGrid.Children.Add(headerSp);

                // Close X Button
                var closeBtn = new Button
                {
                    Content = "✕",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Padding = new Thickness(6, 2, 6, 2)
                };
                closeBtn.Click += (s, e) =>
                {
                    dialogResult = button == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
                    dlg.Close();
                };
                headerGrid.Children.Add(closeBtn);

                // Enable Window Dragging by Header
                headerBorder.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left) dlg.DragMove();
                };

                headerBorder.Child = headerGrid;
                Grid.SetRow(headerBorder, 0);
                rootGrid.Children.Add(headerBorder);

                // ── Message Content ──
                var contentSp = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
                var msgBlock = new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
                    FontSize = 12.5,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap
                };
                contentSp.Children.Add(msgBlock);
                Grid.SetRow(contentSp, 1);
                rootGrid.Children.Add(contentSp);

                // ── Footer Button Panel ──
                var footerBorder = new Border
                {
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
                    Padding = new Thickness(16, 12, 16, 12)
                };
                var buttonSp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                if (button == MessageBoxButton.YesNo)
                {
                    var btnNo = new Button
                    {
                        Content = LanguageManager.Instance.GetString("Dialog_No", "No"),
                        Width = 100,
                        Height = 32,
                        Margin = new Thickness(0, 0, 10, 0),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                        BorderThickness = new Thickness(0),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Cursor = Cursors.Hand
                    };
                    btnNo.Click += (s, e) =>
                    {
                        dialogResult = MessageBoxResult.No;
                        dlg.Close();
                    };

                    var btnYes = new Button
                    {
                        Content = LanguageManager.Instance.GetString("Dialog_Yes", "Yes"),
                        Width = 100,
                        Height = 32,
                        Foreground = Brushes.White,
                        Background = headerBrush,
                        BorderThickness = new Thickness(0),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    btnYes.Click += (s, e) =>
                    {
                        dialogResult = MessageBoxResult.Yes;
                        dlg.Close();
                    };

                    buttonSp.Children.Add(btnNo);
                    buttonSp.Children.Add(btnYes);
                }
                else if (button == MessageBoxButton.OKCancel)
                {
                    var btnCancel = new Button
                    {
                        Content = LanguageManager.Instance.GetString("Dialog_Cancel", "Cancel"),
                        Width = 90,
                        Height = 32,
                        Margin = new Thickness(0, 0, 10, 0),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
                        BorderThickness = new Thickness(0),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Cursor = Cursors.Hand
                    };
                    btnCancel.Click += (s, e) =>
                    {
                        dialogResult = MessageBoxResult.Cancel;
                        dlg.Close();
                    };

                    var btnOk = new Button
                    {
                        Content = LanguageManager.Instance.GetString("Dialog_OK", "OK"),
                        Width = 90,
                        Height = 32,
                        Foreground = Brushes.White,
                        Background = headerBrush,
                        BorderThickness = new Thickness(0),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    btnOk.Click += (s, e) =>
                    {
                        dialogResult = MessageBoxResult.OK;
                        dlg.Close();
                    };

                    buttonSp.Children.Add(btnCancel);
                    buttonSp.Children.Add(btnOk);
                }
                else
                {
                    var btnOk = new Button
                    {
                        Content = LanguageManager.Instance.GetString("Dialog_OK", "OK"),
                        Width = 110,
                        Height = 32,
                        Foreground = Brushes.White,
                        Background = headerBrush,
                        BorderThickness = new Thickness(0),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };
                    btnOk.Click += (s, e) =>
                    {
                        dialogResult = MessageBoxResult.OK;
                        dlg.Close();
                    };
                    buttonSp.Children.Add(btnOk);
                }

                footerBorder.Child = buttonSp;
                Grid.SetRow(footerBorder, 2);
                rootGrid.Children.Add(footerBorder);

                cardBorder.Child = rootGrid;
                dlg.Content = cardBorder;

                // Support ESC and Enter hotkeys
                dlg.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        dialogResult = button == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
                        dlg.Close();
                    }
                    else if (e.Key == Key.Enter)
                    {
                        dialogResult = button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK;
                        dlg.Close();
                    }
                };

                dlg.ShowDialog();
            });

            return dialogResult;
        }

        // Convenience Helpers
        public static void ShowInfo(string message, string? title = null)
        {
            title ??= LanguageManager.Instance.GetString("Dialog_Info", "Information");
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void ShowWarning(string message, string? title = null)
        {
            title ??= LanguageManager.Instance.GetString("Dialog_Warning", "Warning");
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public static void ShowError(string message, string? title = null)
        {
            title ??= LanguageManager.Instance.GetString("Dialog_Error", "Error");
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static bool AskConfirmation(string message, string? title = null)
        {
            title ??= LanguageManager.Instance.GetString("Dialog_Warning", "Confirmation");
            return Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
    }
}
