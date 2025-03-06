using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RogerThat.Commands;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using System.Threading.Tasks;

namespace RogerThat.Services
{
    public static class DialogService
    {
        private static bool isDialogOpen = false;

        public static async Task<bool> ShowConfirmDialog(string title, string message, string acceptText = "确定", string cancelText = "取消")
        {
            if (isDialogOpen)
            {
                return false;
            }

            try
            {
                isDialogOpen = true;
                var dialog = new Grid
                {
                    Width = 300,
                    Margin = new Thickness(16)
                };

                dialog.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                dialog.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                dialog.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleTextBlock = new TextBlock
                {
                    Text = title,
                    Style = Application.Current.FindResource("MaterialDesignHeadline6TextBlock") as Style,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var messageTextBlock = new TextBlock
                {
                    Text = message,
                    Style = Application.Current.FindResource("MaterialDesignBody1TextBlock") as Style,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 24)
                };

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var cancelButton = new Button
                {
                    Content = cancelText,
                    Style = Application.Current.FindResource("MaterialDesignOutlinedButton") as Style,
                    Margin = new Thickness(0, 0, 8, 0),
                    Command = new RelayCommand(_ => DialogHost.CloseDialogCommand.Execute(false, null))
                };

                var acceptButton = new Button
                {
                    Content = acceptText,
                    Style = Application.Current.FindResource("MaterialDesignRaisedButton") as Style,
                    Command = new RelayCommand(_ => DialogHost.CloseDialogCommand.Execute(true, null))
                };

                buttonsPanel.Children.Add(cancelButton);
                buttonsPanel.Children.Add(acceptButton);

                Grid.SetRow(titleTextBlock, 0);
                Grid.SetRow(messageTextBlock, 1);
                Grid.SetRow(buttonsPanel, 2);

                dialog.Children.Add(titleTextBlock);
                dialog.Children.Add(messageTextBlock);
                dialog.Children.Add(buttonsPanel);

                var result = (bool)(await DialogHost.Show(dialog, "RootDialog"));
                return result;
            }
            finally
            {
                isDialogOpen = false;
            }
        }

        public static async Task ShowMessageDialog(string title, string message, double maxHeight = 0)
        {
            if (isDialogOpen)
            {
                return;
            }

            try
            {
                isDialogOpen = true;
                var dialog = new Grid
                {
                    Width = 400,  // 加宽一点
                    Margin = new Thickness(16)
                };

                dialog.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                dialog.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                dialog.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleTextBlock = new TextBlock
                {
                    Text = title,
                    Style = Application.Current.FindResource("MaterialDesignHeadline6TextBlock") as Style,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = maxHeight > 0 ? maxHeight : 400,  // 默认最大高度 400
                    Margin = new Thickness(0, 0, 0, 24)
                };

                var messageTextBlock = new TextBlock
                {
                    Text = message,
                    Style = Application.Current.FindResource("MaterialDesignBody1TextBlock") as Style,
                    TextWrapping = TextWrapping.Wrap
                };

                scrollViewer.Content = messageTextBlock;

                var button = new Button
                {
                    Content = "确定",
                    Style = Application.Current.FindResource("MaterialDesignRaisedButton") as Style,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Command = new RelayCommand(_ => DialogHost.CloseDialogCommand.Execute(null, null))
                };

                Grid.SetRow(titleTextBlock, 0);
                Grid.SetRow(scrollViewer, 1);
                Grid.SetRow(button, 2);

                dialog.Children.Add(titleTextBlock);
                dialog.Children.Add(scrollViewer);
                dialog.Children.Add(button);

                await DialogHost.Show(dialog, "RootDialog");
            }
            finally
            {
                isDialogOpen = false;
            }
        }

        public static async Task<int> ShowCustomDialog(string title, string message, string[] buttons)
        {
            var dialogContent = new StackPanel { Margin = new Thickness(16) };
            
            // 标题
            dialogContent.Children.Add(new TextBlock
            {
                Text = title,
                Style = Application.Current.FindResource("MaterialDesignHeadline6TextBlock") as Style,
                Margin = new Thickness(0, 0, 0, 16)
            });
            
            // 消息内容
            dialogContent.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Style = Application.Current.FindResource("MaterialDesignBody1TextBlock") as Style
            });

            // 按钮面板
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var tcs = new TaskCompletionSource<int>();

            // 添加按钮
            for (int i = 0; i < buttons.Length; i++)
            {
                var index = i;
                var button = new Button
                {
                    Content = buttons[i],
                    Style = Application.Current.FindResource("MaterialDesignFlatButton") as Style,
                    Margin = new Thickness(8, 0, 0, 0)
                };

                button.Click += (s, e) =>
                {
                    if (DialogHost.IsDialogOpen("RootDialog"))
                    {
                        DialogHost.Close("RootDialog");
                    }
                    tcs.SetResult(index);
                };

                buttonPanel.Children.Add(button);
            }

            dialogContent.Children.Add(buttonPanel);

            await DialogHost.Show(dialogContent, "RootDialog");
            return await tcs.Task;
        }
    }
} 