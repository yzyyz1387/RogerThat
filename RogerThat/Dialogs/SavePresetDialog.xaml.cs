using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Controls;
using System.Windows.Input;

namespace RogerThat.Dialogs
{
    public partial class SavePresetDialog : Window
    {
        public string PresetName => NameTextBox.Text;
        public string PresetDescription => DescriptionTextBox.Text;

        public SavePresetDialog()
        {
            InitializeComponent();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PresetName))
            {
                MessageBox.Show("请输入预设名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    }
} 