using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Markup;
using NAudio.Wave;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;
using RogerThat.Models;
using RogerThat.Services;
using RogerThat.Dialogs;
using RogerThat.Commands;
using System.Collections.ObjectModel;
using System.Linq;
using MaterialDesignThemes.Wpf;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Application = System.Windows.Application;
using System.Reflection;

namespace RogerThat
{
    [ContentProperty("Content")]
    public partial class MainWindow : Window
    {
        private WaveOutEvent waveOut;
        private AudioFileReader audioFile;
        private Key selectedHotkey = Key.K;
        private bool isListening = false;
        private bool isWaitingForKey = false;
        private bool keyReleased = false;
        private enum KeyState
        {
            Idle,           // 空闲状态
            KeyDown,        // 按键按下
            PlayingPrefix,  // 播放前置音效
            KeyHeld,        // 按键保持
            WaitingForPrefix,// 等待前置音效完成
            PlayingSuffix   // 播放后置音效
        }
        private KeyState currentState = KeyState.Idle;
        private Settings settings;
        private PresetService presetService;
        private ObservableCollection<ImportedSound> importedSounds;
        private string prefixSoundPath;
        private string suffixSoundPath;
        private readonly SettingsService _settingsService;
        private ImportedSound _currentPlayingSound;
        private const int BAR_COUNT = 64;
        private const int BAR_WIDTH = 2;
        private const int BAR_SPACING = 1;
        private readonly Rectangle[] _bars;
        private AudioWaveform _waveform;
        private DispatcherTimer _progressTimer;
        private readonly UpdateService _updateService;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;

        private enum LogType
        {
            Info,       // 普通信息
            Success,    // 成功信息
            Warning,    // 警告信息
            Error,      // 错误信息
            System      // 系统信息
        }

        private readonly Dictionary<string, string> ThemeColors = new Dictionary<string, string>
        {
            { "#3898fc", "#276ab0" }, // 园长蓝  (开发者初学编程时喜欢并记住16进制代码的颜色)
            { "#2196f3", "#1769aa" }, // 天空蓝
            { "#9c27b0", "#6d1b7b" }, // 搞基紫
            { "#009688", "#00695f" }, // 自然青
            { "#18a96e", "#10764d" }, // 安安绿
            { "#f50057", "#ab003c" }  // 姨妈红
        };

        private bool _isFullScreen = false;

        public MainWindow()
        {
            InitializeComponent();
            _settingsService = new SettingsService();
            _updateService = new UpdateService();
            _updateService.LogMessage += (message, level) => 
            {
                var logType = level switch
                {
                    UpdateService.LogLevel.Info => LogType.Info,
                    UpdateService.LogLevel.Warning => LogType.Warning,
                    UpdateService.LogLevel.Error => LogType.Error,
                    _ => LogType.Info
                };
                LogMessage(message, logType);
            };
            InitializeStorageSettings();
            
            // 初始化音频列表
            importedSounds = new ObservableCollection<ImportedSound>();
            ImportedSoundsListView.ItemsSource = importedSounds;
            
            // 组件初始化
            InitializeComponents();
            
            // 加载音频文件
            LoadAllSounds();
            
            // 加载设置和预设
            LoadSettings();
            LoadPresets();
            
            _bars = new Rectangle[BAR_COUNT];
            InitializeWaveform();
            
            _waveform = new AudioWaveform(WaveformCanvas, WaveformProgress);
            
            LogMessage("程序启动完成", LogType.Success);
            InitializeWaveAnimation();

            // 启动时检查更新
            _ = CheckUpdateOnStartup();
        }

        private void InitializeComponents()
        {
            // 初始化服务
            presetService = new PresetService();
            settings = Settings.Load();

            // 初始化音频设备
            InitializeAudioDevices();

            // 加载主题色
            var primaryColor = (Color)ColorConverter.ConvertFromString(_settingsService.ThemeColor);
            var darkColor = (Color)ColorConverter.ConvertFromString(_settingsService.ThemeDarkColor);

            var primaryPalette = new SolidColorBrush(primaryColor);
            var darkPalette = new SolidColorBrush(darkColor);

            Resources["PrimaryHueMidBrush"] = primaryPalette;
            Resources["PrimaryHueDarkBrush"] = darkPalette;

            // 初始化事件处理
            InitializeEventHandlers();

            // 导航事件处理
            HomeButton.Checked += Navigation_Checked;
            AboutButton.Checked += Navigation_Checked;

            // 初始化热键文本框
            HotkeyTextBox.Text = selectedHotkey.ToString();

            // 初始化存储路径文本框
            if (!string.IsNullOrEmpty(_settingsService.AudioStoragePath))
            {
                StoragePathTextBox.Text = _settingsService.AudioStoragePath;
            }
        }

        private void InitializeAudioDevices()
        {
            AudioDevicesComboBox.Items.Clear();
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in devices)
            {
                AudioDevicesComboBox.Items.Add(device.FriendlyName);
            }

            if (AudioDevicesComboBox.Items.Count > 0)
                AudioDevicesComboBox.SelectedIndex = 0;
        }

        private int GetWaveOutDeviceNumber(string deviceName)
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var capabilities = WaveOut.GetCapabilities(i);
                if (deviceName.Contains(capabilities.ProductName))
                {
                    return i;
                }
            }
            return 0; // 未找到设备时使用默认设备
        }

        private void InitializeEventHandlers()
        {
            SetHotkeyButton.Click += SetHotkeyButton_Click;
            StartButton.Click += StartButton_Click;
            StopButton.Click += StopButton_Click;
            ImportSoundButton.Click += ImportSoundButton_Click;
            ClearLogButton.Click += ClearLogButton_Click;
            
            HotkeyTextBox.Text = selectedHotkey.ToString();
            
            PrefixSoundCheckBox.Checked += (s, e) => LogMessage("启用前置音效");
            PrefixSoundCheckBox.Unchecked += (s, e) => LogMessage("禁用前置音效");
            SuffixSoundCheckBox.Checked += (s, e) => LogMessage("启用后置音效");
            SuffixSoundCheckBox.Unchecked += (s, e) => LogMessage("禁用后置音效");
            
            AudioDevicesComboBox.SelectionChanged += (s, e) => 
            {
                if (AudioDevicesComboBox.SelectedItem != null)
                {
                    LogMessage($"选择音频设备: {AudioDevicesComboBox.SelectedItem}", LogType.Info);
                }
            };

            //导航事件处理
            foreach (RadioButton button in FindVisualChildren<RadioButton>(this))
            {
                if (button.GroupName == "Navigation")
                {
                    button.Checked += Navigation_Checked;
                }
            }
        }

        private void Navigation_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                string pageName = radioButton.Tag as string;

                switch (pageName)
                {
                    case "主页":
                        MainContent.Visibility = Visibility.Visible;
                        AboutContent.Visibility = Visibility.Collapsed;
                        break;
                    case "关于":
                        MainContent.Visibility = Visibility.Collapsed;
                        AboutContent.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        private void LoadAllSounds()
        {
            importedSounds.Clear();
            LogMessage("开始加载音频文件...", LogType.Info);

            // 加载默认音频
            LoadDefaultSounds();

            // 2. 加载用户音效
            var userSounds = _settingsService.GetImportedSounds();
            foreach (var soundConfig in userSounds)
            {
                // 跳过默认音效路径
                if (soundConfig.FilePath.Contains("Assets\\sounds\\"))
                {
                    continue;
                }

                if (File.Exists(soundConfig.FilePath))
                {
                    var sound = new ImportedSound(soundConfig.FilePath)
                    {
                        IsPrefix = soundConfig.IsPrefix,
                        IsSuffix = soundConfig.IsSuffix
                    };
                    AddSoundToList(sound);
                    LogMessage($"加载用户音效: {sound.FileName}", LogType.Success);

                    // 恢复音效设置
                    if (sound.IsPrefix) prefixSoundPath = sound.FilePath;
                    if (sound.IsSuffix) suffixSoundPath = sound.FilePath;
                }
            }

            LogMessage($"音频文件加载完成，共 {importedSounds.Count} 个", LogType.Success);
        }

        private void LoadDefaultSounds()
        {
            try
            {
                string defaultSoundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "sounds");
                if (!Directory.Exists(defaultSoundsPath))
                {
                    LogMessage("默认音频目录不存在", LogType.Warning);
                    return;
                }

                // 获取所有WAV和MP3文件
                var audioFiles = Directory.GetFiles(defaultSoundsPath, "*.*")
                    .Where(file => file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || 
                                  file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));

                foreach (string filePath in audioFiles)
                {
                    try
                    {
                        AddSoundToList(new ImportedSound(filePath));
                        LogMessage($"加载默认音频: {Path.GetFileName(filePath)}", LogType.Info);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"加载默认音频失败 {filePath}: {ex.Message}", LogType.Error);
                    }
                }

                LogMessage($"音频文件加载完成, 共 {audioFiles.Count()} 个", LogType.Success);
            }
            catch (Exception ex)
            {
                LogMessage($"加载默认音频目录失败: {ex.Message}", LogType.Error);
            }
        }

        private void AddSoundToList(ImportedSound sound)
        {
            // 检查是否已存在
            if (importedSounds.Any(s => s.FilePath.Equals(sound.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                LogMessage($"跳过重复音效: {sound.FileName}", LogType.Warning);
                return;
            }

            //命令
            sound.SetAsPrefixCommand = new RelayCommand(_ => SetAsPrefix(sound));
            sound.SetAsSuffixCommand = new RelayCommand(_ => SetAsSuffix(sound));
            sound.RequestDelete += HandleSoundDelete;
            sound.PlayCommand = new RelayCommand(_ => PlaySound(sound));
            
            // 添加到列表
            importedSounds.Add(sound);
        }

        private void SaveImportedSounds()
        {
            // 只保存用户导入的音效
            var userSounds = importedSounds.Where(s => !s.IsDefaultSound);
            _settingsService.SaveImportedSounds(userSounds);
            LogMessage("已保存用户音效配置", LogType.Info);
        }

        private void LoadSettings()
        {
            PrefixSoundCheckBox.IsChecked = settings.PrefixEnabled;
            SuffixSoundCheckBox.IsChecked = settings.SuffixEnabled;
            
            if (!string.IsNullOrEmpty(settings.PrefixSoundPath) && File.Exists(settings.PrefixSoundPath))
            {
                prefixSoundPath = settings.PrefixSoundPath;
            }
            
            if (!string.IsNullOrEmpty(settings.SuffixSoundPath) && File.Exists(settings.SuffixSoundPath))
            {
                suffixSoundPath = settings.SuffixSoundPath;
            }
            
            selectedHotkey = (Key)Enum.Parse(typeof(Key), settings.SelectedHotkey);
            HotkeyTextBox.Text = selectedHotkey.ToString();
            
            if (settings.SelectedAudioDevice < AudioDevicesComboBox.Items.Count)
            {
                AudioDevicesComboBox.SelectedIndex = settings.SelectedAudioDevice;
            }

            // 更新选中状态
            UpdateSoundListSelections();
        }

        private void SaveSettings()
        {
            settings.PrefixEnabled = PrefixSoundCheckBox.IsChecked ?? false;
            settings.SuffixEnabled = SuffixSoundCheckBox.IsChecked ?? false;
            settings.PrefixSoundPath = prefixSoundPath;
            settings.SuffixSoundPath = suffixSoundPath;
            settings.SelectedHotkey = selectedHotkey.ToString();
            settings.SelectedAudioDevice = AudioDevicesComboBox.SelectedIndex;
            settings.Save();
        }

        private void LogMessage(string message, LogType type = LogType.Info)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string logEntry = $"[{timestamp}] {message}\n";

                var textRange = new TextRange(LogTextBox.Document.ContentEnd, LogTextBox.Document.ContentEnd);
                textRange.Text = logEntry;

                switch (type)
                {
                    case LogType.Success:
                        textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Green));
                        break;
                    case LogType.Warning:
                        textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Orange));
                        break;
                    case LogType.Error:
                        textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Red));
                        break;
                    case LogType.System:
                        textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Blue));
                        break;
                    default:
                        if (message.Contains("开始监听"))
                        {
                            textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Blue));
                        }
                        else if (message.Contains("停止监听"))
                        {
                            textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Red));
                        }
                        else if (message.Contains("播放音频") || message.Contains("音频播放"))
                        {
                            textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(75, 0, 130))); // 深紫色
                        }
                        else
                        {
                            textRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Gray));
                        }
                        break;
                }

                LogTextBox.ScrollToEnd();
            });
        }

        private void SetHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            HotkeyTextBox.Text = "按下任意键...";
            isWaitingForKey = true;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            LogMessage("等待按键设置...");
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (isWaitingForKey)
            {
                selectedHotkey = e.Key;
                HotkeyTextBox.Text = selectedHotkey.ToString();
                isWaitingForKey = false;
                PreviewKeyDown -= MainWindow_PreviewKeyDown;
                e.Handled = true;
                LogMessage($"检测到按键按下: {selectedHotkey}", LogType.Info);
                SaveSettings();
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSettings()) return;

            isListening = true;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            LogMessage("开始监听");

            await Task.Run(() => StartKeyboardHook());
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            isListening = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            LogMessage("停止监听");
        }

        private bool ValidateSettings()
        {
            if (AudioDevicesComboBox.SelectedIndex == -1)
            {
                DialogService.ShowMessageDialog(
                    "设置错误",
                    "请选择音频输出设备"
                );
                return false;
            }

            if (PrefixSoundCheckBox.IsChecked == true && !File.Exists(prefixSoundPath))
            {
                DialogService.ShowMessageDialog(
                    "设置错误",
                    "前置音效文件不存在"
                );
                return false;
            }

            if (SuffixSoundCheckBox.IsChecked == true && !File.Exists(suffixSoundPath))
            {
                DialogService.ShowMessageDialog(
                    "设置错误",
                    "后置音效文件不存在"
                );
                return false;
            }

            return true;
        }

        private void StartKeyboardHook()
        {
            bool lastKeyState = false;

            while (isListening)
            {
                bool currentKeyState = false;
                bool prefixEnabled = false;
                bool suffixEnabled = false;
                string prefixPath = "";
                string suffixPath = "";

                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        currentKeyState = Keyboard.IsKeyDown(selectedHotkey);
                        prefixEnabled = PrefixSoundCheckBox.IsChecked == true;
                        suffixEnabled = SuffixSoundCheckBox.IsChecked == true;
                        prefixPath = prefixSoundPath;
                        suffixPath = suffixSoundPath;
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"检测按键状态时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    isListening = false;
                    Dispatcher.Invoke(() =>
                    {
                        StartButton.IsEnabled = true;
                        StopButton.IsEnabled = false;
                    });
                    return;
                }

                // 状态机处理
                switch (currentState)
                {
                    case KeyState.Idle:
                        if (currentKeyState && !lastKeyState)
                        {
                            LogMessage($"检测到按键按下: {selectedHotkey}", LogType.Info);
                            if (prefixEnabled)
                            {
                                currentState = KeyState.PlayingPrefix;
                                keyReleased = false;
                                Dispatcher.Invoke(() => PlayAudio(prefixPath, OnPrefixComplete));
                            }
                            else
                            {
                                currentState = KeyState.KeyHeld;
                            }
                        }
                        break;

                    case KeyState.PlayingPrefix:
                        if (!currentKeyState && lastKeyState)
                        {
                            keyReleased = true;
                            LogMessage($"检测到按键释放: {selectedHotkey}（等待前置音效完成）", LogType.Info);
                        }
                        break;

                    case KeyState.KeyHeld:
                        if (!currentKeyState && lastKeyState)
                        {
                            LogMessage($"检测到按键释放: {selectedHotkey}", LogType.Info);
                            if (suffixEnabled)
                            {
                                currentState = KeyState.PlayingSuffix;
                                SimulateKeyPress();
                                Dispatcher.Invoke(() => PlayAudio(suffixPath, OnSuffixComplete));
                            }
                            else
                            {
                                currentState = KeyState.Idle;
                            }
                        }
                        break;
                }

                lastKeyState = currentKeyState;
                System.Threading.Thread.Sleep(10);
            }
        }

        private void OnPrefixComplete()
        {
            switch (currentState)
            {
                case KeyState.PlayingPrefix:
                    if (keyReleased)
                    {
                        // 如果前置音效播放时按键已释放，直接播放后置音效
                        LogMessage($"前置音效播放完成，检测到之前的按键释放", LogType.Info);
                        if (SuffixSoundCheckBox.IsChecked == true)
                        {
                            currentState = KeyState.PlayingSuffix;
                            SimulateKeyPress();
                            Dispatcher.Invoke(() => PlayAudio(suffixSoundPath, OnSuffixComplete));
                        }
                        else
                        {
                            currentState = KeyState.Idle;
                        }
                    }
                    else
                    {
                        currentState = KeyState.KeyHeld;
                    }
                    break;
            }
        }

        private void OnSuffixComplete()
        {
            if (currentState == KeyState.PlayingSuffix)
            {
                byte virtualKey = (byte)KeyInterop.VirtualKeyFromKey(selectedHotkey);
                keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                currentState = KeyState.Idle;
            }
        }

        private void SimulateKeyPress()
        {
            byte virtualKey = (byte)KeyInterop.VirtualKeyFromKey(selectedHotkey);
            keybd_event(virtualKey, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        }

        private void PlayAudio(string filePath, Action onComplete = null)
        {
            try
            {
                StopAudio();
                waveOut = new WaveOutEvent();
                waveOut.DeviceNumber = GetWaveOutDeviceNumber(AudioDevicesComboBox.SelectedItem.ToString());
                audioFile = new AudioFileReader(filePath);
                waveOut.Init(audioFile);
                
                waveOut.PlaybackStopped += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 停止进度更新
                        if (_progressTimer != null)
                        {
                            _progressTimer.Stop();
                            _progressTimer = null;
                        }

                        // 重置当前播放音频的状态
                        if (_currentPlayingSound != null)
                        {
                            _currentPlayingSound.IsPlaying = false;
                            _currentPlayingSound = null;
                        }
                        
                        LogMessage($"音频播放完成: {Path.GetFileName(filePath)}");
                        onComplete?.Invoke();
                    });
                };
                
                waveOut.Play();
                LogMessage($"播放音频: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                LogMessage($"播放音频失败: {ex.Message}", LogType.Error);
                throw;
            }
        }

        private void PlaySound(ImportedSound sound)
        {
            if (_currentPlayingSound == sound && sound.IsPlaying)
            {
                // 如果点击的是正在播放的音频，则停止
                StopAudio();
                sound.IsPlaying = false;
                _currentPlayingSound = null;
            }
            else
            {
                // 如果有其他音频在播放，先停止
                if (_currentPlayingSound != null)
                {
                    _currentPlayingSound.IsPlaying = false;
                    StopAudio();
                }

                // 播放新音频
                try
                {
                    // 绘制
                    _waveform.DrawWaveform(sound.FilePath);
                    
                    // 播放
                    PlayAudio(sound.FilePath);
                    sound.IsPlaying = true;
                    _currentPlayingSound = sound;
                    LogMessage($"试听音频: {sound.FileName}", LogType.Info);

                    // 开始更新进度
                    StartProgressUpdate();
                }
                catch (Exception ex)
                {
                    LogMessage($"试听音频失败: {ex.Message}", LogType.Error);
                }
            }
        }

        private void StartProgressUpdate()
        {
            // 停止现有的计时器
            if (_progressTimer != null)
            {
                _progressTimer.Stop();
            }

            // 创建新的计时器
            _progressTimer = new DispatcherTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(50); // 每50ms更新一次
            _progressTimer.Tick += (s, e) =>
            {
                if (waveOut != null && audioFile != null)
                {
                    OnPlaybackPositionChanged(audioFile.CurrentTime);
                }
                else
                {
                    _progressTimer.Stop();
                }
            };
            _progressTimer.Start();
        }

        private void StopAudio()
        {
            if (_progressTimer != null)
            {
                _progressTimer.Stop();
                _progressTimer = null;
            }

            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
                waveOut = null;
            }
            if (audioFile != null)
            {
                audioFile.Dispose();
                audioFile = null;
            }

            // 重置波形进度
            OnPlaybackPositionChanged(TimeSpan.Zero);
        }

        private async void ImportSoundButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "音频文件|*.wav;*.mp3|所有文件|*.*",
                Title = "选择音频文件",
                Multiselect = true  // 启用多选
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (string filePath in dialog.FileNames)  // 遍历
                {
                    try
                    {
                        await ImportAudioFile(filePath);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"导入音频失败: {ex.Message}", LogType.Error);
                    }
                }
            }
        }

        private async Task ImportAudioFile(string sourceFilePath)
        {
            string fileName = Path.GetFileName(sourceFilePath);
            string targetPath = Path.Combine(_settingsService.AudioStoragePath, fileName);
            
            // 如果目标位置已有同名文件，添加数字后缀
            int counter = 1;
            while (File.Exists(targetPath))
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                targetPath = Path.Combine(_settingsService.AudioStoragePath, $"{fileNameWithoutExt}_{counter}{extension}");
                counter++;
            }
            
            File.Copy(sourceFilePath, targetPath);
            
            var sound = new ImportedSound(targetPath);
            sound.SetAsPrefixCommand = new RelayCommand(_ => SetAsPrefix(sound));
            sound.SetAsSuffixCommand = new RelayCommand(_ => SetAsSuffix(sound));
            sound.RequestDelete += HandleSoundDelete;
            sound.PlayCommand = new RelayCommand(_ => PlaySound(sound));  // 确保设置播放命令
            
            importedSounds.Add(sound);
            SaveImportedSounds();
            
            LogMessage($"已导入音频: {sound.DisplayName}", LogType.Success);
        }

        private void SetAsPrefix(ImportedSound sound)
        {
            prefixSoundPath = sound.FilePath;
            foreach (var s in importedSounds)
            {
                s.IsPrefix = s == sound;
            }
            LogMessage($"已设置前置音效: {sound.FileName}");
            SaveImportedSounds();
        }

        private void SetAsSuffix(ImportedSound sound)
        {
            suffixSoundPath = sound.FilePath;
            foreach (var s in importedSounds)
            {
                s.IsSuffix = s == sound;
            }
            LogMessage($"已设置后置音效: {sound.FileName}");
            SaveImportedSounds();
        }

        private async void HandleSoundDelete(object sender, EventArgs e)
        {
            if (sender is ImportedSound sound)
            {
                // 如果是默认音效，不允许删除
                if (sound.IsDefaultSound)
                {
                    await DialogService.ShowMessageDialog(
                        "提示",
                        "默认音效文件不可删除"
                    );
                    return;
                }

                // 显示删除确认对话框
                var result = await DialogService.ShowConfirmDialog(
                    "删除确认",
                    $"确定要删除音频文件 \"{sound.DisplayName}\" 吗？\n注意：这将从磁盘中永久删除该文件。"
                );

                if (result)
                {
                    try
                    {
                        // 如果正在使用这个音效，清除相关设置
                        if (sound.FilePath == prefixSoundPath)
                        {
                            prefixSoundPath = null;
                            PrefixSoundCheckBox.IsChecked = false;
                        }
                        if (sound.FilePath == suffixSoundPath)
                        {
                            suffixSoundPath = null;
                            SuffixSoundCheckBox.IsChecked = false;
                        }

                        // 从列表中移除
                        importedSounds.Remove(sound);
                        SaveImportedSounds();

                        // 删除物理文件
                        if (File.Exists(sound.FilePath))
                        {
                            File.Delete(sound.FilePath);
                            LogMessage($"已删除音频文件: {sound.FileName}", LogType.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"删除音频文件失败: {ex.Message}", LogType.Error);
                        await DialogService.ShowMessageDialog(
                            "错误",
                            $"删除音频文件失败: {ex.Message}"
                        );
                    }
                }
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // 获取日志框的可见高度
                var scrollViewer = GetScrollViewer(LogTextBox);
                double viewportHeight = scrollViewer?.ViewportHeight ?? 0;
                
                // 创建空白段落
                var emptyParagraph = new Paragraph();
                int emptyLines = (int)(viewportHeight / 16);
                emptyParagraph.Inlines.Add(new Run(new string('\n', emptyLines)));
                LogTextBox.Document.Blocks.Add(emptyParagraph);
                
                // 创建分隔线段落
                var separatorParagraph = new Paragraph
                {
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0)
                };
                
                //分隔线、左侧装饰线
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                separatorParagraph.Inlines.Add(new Run("─────── "));
                
                // 时间戳和说明
                separatorParagraph.Inlines.Add(new Run($"{timestamp} 已清理历史日志") 
                { 
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237))
                });
                
                // 右侧装饰线
                separatorParagraph.Inlines.Add(new Run(" ───────"));
                separatorParagraph.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
            
                // 分隔线段
                LogTextBox.Document.Blocks.Add(separatorParagraph);
                
                // 确保后续日志样式正常
                var newParagraph = new Paragraph
                {
                    TextAlignment = TextAlignment.Left,  // 恢复默认对齐
                    Margin = new Thickness(0)
                };
                LogTextBox.Document.Blocks.Add(newParagraph);
                
                // 到底部
                LogTextBox.ScrollToEnd();
            });
        }

        // 辅助方法：获取 RichTextBox 的 ScrollViewer
        private ScrollViewer GetScrollViewer(DependencyObject depObj)
        {
            if (depObj == null) return null;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                
                if (child is ScrollViewer)
                    return child as ScrollViewer;
                
                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (isListening)
            {
                e.Cancel = true;  // 先取消关闭
                var result = await DialogService.ShowConfirmDialog(
                    "确认退出",
                    "程序正在运行中，确定要退出吗？"
                );

                if (result)
                {
                    isListening = false;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StartButton.IsEnabled = true;
                        StopButton.IsEnabled = false;
                        LogMessage("停止监听", LogType.Warning);
                    });
                    Close();  // 手动关闭窗口
                }
                return;
            }

            SaveSettings();
            if (waveOut != null)
            {
                waveOut.Stop();
                waveOut.Dispose();
            }
            if (audioFile != null)
            {
                audioFile.Dispose();
            }
            LogMessage("程序退出", LogType.System);
            base.OnClosing(e);
        }

        private void LoadPresets()
        {
            PresetsComboBox.ItemsSource = presetService.GetAllPresets();
            
            // 加载上次选择的预设
            string lastPreset = presetService.LastSelectedPreset;
            if (!string.IsNullOrEmpty(lastPreset))
            {
                var preset = presetService.GetAllPresets()
                    .FirstOrDefault(p => p.Name == lastPreset);
                
                if (preset != null)
                {
                    PresetsComboBox.SelectedItem = preset;
                    
                    // 更新前后置音效状态
                    prefixSoundPath = preset.PrefixSoundPath;
                    suffixSoundPath = preset.SuffixSoundPath;
                    
                    // 更新选中状态
                    UpdateSoundListSelections();
                    
                    LogMessage($"已加载上次选择的预设: {preset.Name}", LogType.Info);
                }
            }

            PresetsComboBox.SelectionChanged += PresetsComboBox_SelectionChanged;
            SavePresetButton.Click += SavePresetButton_Click;
            DeletePresetButton.Click += DeletePresetButton_Click;
        }

        private void PresetsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetsComboBox.SelectedItem is Preset preset)
            {
                // 更新音效状态
                PrefixSoundCheckBox.IsChecked = preset.PrefixEnabled;
                SuffixSoundCheckBox.IsChecked = preset.SuffixEnabled;
                
                // 更新音效路径
                prefixSoundPath = preset.PrefixSoundPath;
                suffixSoundPath = preset.SuffixSoundPath;

                // 更新选中状态
                UpdateSoundListSelections();
                
                // 更新热键
                if (Enum.TryParse<Key>(preset.SelectedHotkey, out Key hotkey))
                {
                    selectedHotkey = hotkey;
                    HotkeyTextBox.Text = selectedHotkey.ToString();
                    LogMessage($"已更新热键绑定: {selectedHotkey}", LogType.Info);
                }
                
                DeletePresetButton.IsEnabled = !preset.IsBuiltin;
                LogMessage($"已应用预设: {preset.Name}", LogType.Success);

                // 保存预设选择
                presetService.UpdateLastSelectedPreset(preset.Name);
            }
        }

        private void UpdateSoundListSelections()
        {
            foreach (var sound in importedSounds)
            {
                sound.IsPrefix = sound.FilePath == prefixSoundPath;
                sound.IsSuffix = sound.FilePath == suffixSoundPath;
            }
        }

        private void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SavePresetDialog();
            if (dialog.ShowDialog() == true)
            {
                var preset = new Preset
                {
                    Name = dialog.PresetName,
                    Description = dialog.PresetDescription,
                    PrefixEnabled = PrefixSoundCheckBox.IsChecked ?? false,
                    SuffixEnabled = SuffixSoundCheckBox.IsChecked ?? false,
                    PrefixSoundPath = prefixSoundPath,
                    SuffixSoundPath = suffixSoundPath,
                    SelectedHotkey = selectedHotkey.ToString()  // 保存当前热键设置
                };
                
                presetService.SaveUserPreset(preset);
                LoadPresets();
                LogMessage($"已保存预设: {preset.Name} (热键: {preset.SelectedHotkey})", LogType.Success);
            }
        }

        private async void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsComboBox.SelectedItem is Preset preset && !preset.IsBuiltin)
            {
                var result = await DialogService.ShowConfirmDialog(
                    "删除预设",
                    $"确定要删除预设 \"{preset.Name}\" 吗？此操作无法撤销。"
                );
                
                if (result)
                {
                    try
                    {
                        presetService.DeleteUserPreset(preset);
                        LoadPresets();
                        LogMessage($"已删除预设: {preset.Name}", LogType.Warning);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"删除预设失败: {ex.Message}", LogType.Error);
                        await DialogService.ShowMessageDialog(
                            "错误",
                            $"删除预设失败: {ex.Message}"
                        );
                    }
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击标题栏不执行任何操作
                return;
            }
            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void InitializeStorageSettings()
        {
            try
            {
                string storagePath = _settingsService.AudioStoragePath;
                if (!string.IsNullOrEmpty(storagePath) && Directory.Exists(storagePath))
                {
                    StoragePathTextBox.Text = storagePath;
                    LogMessage($"已加载存储路径: {storagePath}", LogType.Info);
                }
                else
                {
                    LogMessage("使用默认存储路径", LogType.Warning);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"加载存储路径失败: {ex.Message}", LogType.Error);
            }
        }

        private async void ChangeStoragePathButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择音频文件存储位置",
                UseDescriptionForTitle = true,
                InitialDirectory = _settingsService.AudioStoragePath,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    var result = await DialogService.ShowConfirmDialog(
                        "确认更改",
                        "是否将现有音频文件移动到新位置？\n选择\"否\"则仅更改新导入文件的存储位置。"
                    );

                    if (result)
                    {
                        // 移动现有文件
                        await MoveExistingAudioFiles(dialog.SelectedPath);
                    }

                    _settingsService.UpdateAudioStoragePath(dialog.SelectedPath);
                    StoragePathTextBox.Text = dialog.SelectedPath;
                    LogMessage($"已更改存储路径: {dialog.SelectedPath}", LogType.Success);
                }
                catch (Exception ex)
                {
                    LogMessage($"更改存储路径失败: {ex.Message}", LogType.Error);
                    await DialogService.ShowMessageDialog(
                        "错误",
                        "更改存储路径失败，请确保有足够的权限访问该目录。"
                    );
                }
            }
        }

        private async Task MoveExistingAudioFiles(string newPath)
        {
            foreach (var sound in importedSounds.ToList())
            {
                if (!sound.IsDefaultSound && File.Exists(sound.FilePath))
                {
                    string fileName = Path.GetFileName(sound.FilePath);
                    string newFilePath = Path.Combine(newPath, fileName);
                    
                    // 如果目标位置已有同名文件，添加数字后缀
                    int counter = 1;
                    while (File.Exists(newFilePath))
                    {
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string extension = Path.GetExtension(fileName);
                        newFilePath = Path.Combine(newPath, $"{fileNameWithoutExt}_{counter}{extension}");
                        counter++;
                    }

                    // 处理文件
                    await Task.Run(() => 
                    {
                        File.Move(sound.FilePath, newFilePath);
                    });
                    
                    // 更新音频文件路径
                    if (prefixSoundPath == sound.FilePath)
                        prefixSoundPath = newFilePath;
                    if (suffixSoundPath == sound.FilePath)
                        suffixSoundPath = newFilePath;
                        
                    // 更新导入音频列表中的路径
                    var index = importedSounds.IndexOf(sound);
                    var updatedSound = new ImportedSound(newFilePath)
                    {
                        IsPrefix = sound.IsPrefix,
                        IsSuffix = sound.IsSuffix
                    };
                    updatedSound.SetAsPrefixCommand = new RelayCommand(_ => SetAsPrefix(updatedSound));
                    updatedSound.SetAsSuffixCommand = new RelayCommand(_ => SetAsSuffix(updatedSound));
                    updatedSound.RequestDelete += HandleSoundDelete;
                    
                    importedSounds[index] = updatedSound;
                }
            }
        }

        private void OpenStoragePathButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_settingsService.AudioStoragePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _settingsService.AudioStoragePath,
                        UseShellExecute = true
                    });
                    LogMessage($"已打开存储目录", LogType.Info);
                }
                else
                {
                    LogMessage($"存储目录不存在", LogType.Warning);
                    DialogService.ShowMessageDialog(
                        "提示",
                        "存储目录不存在，请检查路径设置。"
                    );
                }
            }
            catch (Exception ex)
            {
                LogMessage($"打开存储目录失败: {ex.Message}", LogType.Error);
                DialogService.ShowMessageDialog(
                    "错误",
                    $"打开存储目录失败: {ex.Message}"
                );
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private async void OpenConfigDirButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RogerThat"
                );

                if (Directory.Exists(configDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = configDir,
                        UseShellExecute = true
                    });
                    LogMessage("已打开配置文件目录", LogType.Info);
                }
                else
                {
                    LogMessage("配置目录不存在", LogType.Warning);
                    await DialogService.ShowMessageDialog(
                        "提示",
                        "配置目录不存在，请先运行程序生成配置。"
                    );
                }
            }
            catch (Exception ex)
            {
                LogMessage($"打开配置目录失败: {ex.Message}", LogType.Error);
                await DialogService.ShowMessageDialog(
                    "错误",
                    $"打开配置目录失败: {ex.Message}"
                );
            }
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton button)
            {
                button.IsChecked = false;  // 取消选中状态
                button.ContextMenu.IsOpen = true;
            }
        }

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string color)
            {
                // 确保颜色存在于字典中
                if (!ThemeColors.ContainsKey(color))
                {
                    LogMessage($"未找到对应的主题色: {color}", LogType.Error);
                    return;
                }

                // 设置主色
                var primaryColor = (Color)ColorConverter.ConvertFromString(color);
                var darkColor = (Color)ColorConverter.ConvertFromString(ThemeColors[color]);

                var primaryPalette = new SolidColorBrush(primaryColor);
                var darkPalette = new SolidColorBrush(darkColor);

                Resources["PrimaryHueMidBrush"] = primaryPalette;
                Resources["PrimaryHueDarkBrush"] = darkPalette;

                // 波形会自动更新，因为使用了 DynamicResource

                // 保存主题色设置
                _settingsService.ThemeColor = color;
                _settingsService.ThemeDarkColor = ThemeColors[color];

                LogMessage($"已切换主题颜色: {menuItem.Header}", LogType.Info);
            }
        }

        private void FullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            _isFullScreen = !_isFullScreen;
            
            if (_isFullScreen)
            {
                // 保存当前窗口状态
                Tag = new Tuple<double, double, double, double, WindowStyle, WindowState, ResizeMode>(
                    Left, Top, Width, Height, WindowStyle, WindowState, ResizeMode);

                // 切换到全屏
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
                
                // 更改图标为退出全屏
                FullScreenIcon.Kind = PackIconKind.FullscreenExit;
            }
            else
            {
                // 恢复窗口状态
                if (Tag is Tuple<double, double, double, double, WindowStyle, WindowState, ResizeMode> windowInfo)
                {
                    Left = windowInfo.Item1;
                    Top = windowInfo.Item2;
                    Width = windowInfo.Item3;
                    Height = windowInfo.Item4;
                    WindowStyle = windowInfo.Item5;
                    WindowState = windowInfo.Item6;
                    ResizeMode = windowInfo.Item7;
                }
                
                // 更改图标为全屏
                FullScreenIcon.Kind = PackIconKind.Fullscreen;
            }
            
            LogMessage($"切换{(_isFullScreen ? "全屏" : "窗口")}模式", LogType.Info);
        }

        //键盘快捷键支持
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            // Alt + Enter 切全屏
            if (e.Key == Key.Return && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                FullScreenButton_Click(null, null);
                e.Handled = true;
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer scrollviewer = sender as ScrollViewer;
            if (e.Delta > 0)
                scrollviewer.LineUp();
            else
                scrollviewer.LineDown();
            e.Handled = true;
        }

        private void InitializeWaveform()
        {
            WaveformCanvas.Children.Clear();

            double totalWidth = BAR_COUNT * (BAR_WIDTH + BAR_SPACING) - BAR_SPACING;
            double startX = (WaveformCanvas.ActualWidth - totalWidth) / 2;
            double centerY = WaveformCanvas.ActualHeight / 2;

            for (int i = 0; i < BAR_COUNT; i++)
            {
                var bar = new Rectangle
                {
                    Width = BAR_WIDTH,
                    Fill = FindResource("PrimaryHueMidBrush") as SolidColorBrush,
                    Height = 2,
                    Opacity = 0.8
                };

                Canvas.SetLeft(bar, startX + i * (BAR_WIDTH + BAR_SPACING));
                Canvas.SetTop(bar, centerY - 1);

                _bars[i] = bar;
                WaveformCanvas.Children.Add(bar);
            }
        }

        private async Task PlayAudioAsync(string filePath)
        {
            try
            {
                StopAudio();
                waveOut = new WaveOutEvent();
                waveOut.DeviceNumber = GetWaveOutDeviceNumber(AudioDevicesComboBox.SelectedItem.ToString());
                audioFile = new AudioFileReader(filePath);
                var sampleProvider = audioFile.ToSampleProvider();
                waveOut.Init(audioFile);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    var buffer = new float[1024];
                    int samplesRead = sampleProvider.Read(buffer, 0, buffer.Length);
                    if (samplesRead > 0)
                    {
                        UpdateWaveform(buffer, samplesRead);
                    }
                    await Task.Delay(16);
                }

                // 播放结束时重置波形
                ResetWaveform();
            }
            catch (Exception ex)
            {
                LogMessage($"音频播放失败: {ex.Message}", LogType.Error);
            }
        }

        private void UpdateWaveform(float[] samples, int count)
        {
            int samplesPerBar = count / BAR_COUNT;
            double centerY = WaveformCanvas.ActualHeight / 2;

            for (int i = 0; i < BAR_COUNT; i++)
            {
                float max = 0;
                int start = i * samplesPerBar;
                int end = Math.Min(start + samplesPerBar, count);

                for (int j = start; j < end; j++)
                {
                    max = Math.Max(max, Math.Abs(samples[j]));
                }

                var bar = _bars[i];
                double height = max * WaveformCanvas.ActualHeight * 0.8;

                Dispatcher.Invoke(() =>
                {
                    var heightAnimation = new DoubleAnimation
                    {
                        To = Math.Max(2, height),
                        Duration = TimeSpan.FromMilliseconds(16)
                    };
                    bar.BeginAnimation(HeightProperty, heightAnimation);

                    var topAnimation = new DoubleAnimation
                    {
                        To = centerY - (height / 2),
                        Duration = TimeSpan.FromMilliseconds(16)
                    };
                    bar.BeginAnimation(Canvas.TopProperty, topAnimation);
                });
            }
        }

        private void ResetWaveform()
        {
            foreach (var bar in _bars)
            {
                Dispatcher.Invoke(() =>
                {
                    var heightAnimation = new DoubleAnimation
                    {
                        To = 2,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    bar.BeginAnimation(HeightProperty, heightAnimation);

                    var topAnimation = new DoubleAnimation
                    {
                        To = WaveformCanvas.ActualHeight / 2 - 1,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    bar.BeginAnimation(Canvas.TopProperty, topAnimation);
                });
            }
        }

        private void OnAudioFileSelected(string filePath)
        {
            _waveform.DrawWaveform(filePath);
        }

        private void OnPlaybackPositionChanged(TimeSpan position)
        {
            _waveform.UpdateProgress(position);
            WaveformTimeText.Text = $"{position:mm\\:ss} / {TimeSpan.FromSeconds(_waveform.Duration):mm\\:ss}";
        }

        private void OpenDefaultAudioDirButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string defaultAudioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "sounds");
                if (Directory.Exists(defaultAudioPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = defaultAudioPath,
                        UseShellExecute = true
                    });
                    LogMessage("已打开默认音频目录", LogType.Info);
                }
                else
                {
                    LogMessage("默认音频目录不存在", LogType.Warning);
                    DialogService.ShowMessageDialog(
                        "提示",
                        "默认音频目录不存在。"
                    );
                }
            }
            catch (Exception ex)
            {
                LogMessage($"打开默认音频目录失败: {ex.Message}", LogType.Error);
                DialogService.ShowMessageDialog(
                    "错误",
                    $"打开默认音频目录失败: {ex.Message}"
                );
            }
        }

        private void InitializeWaveAnimation()
        {
            CompositionTarget.Rendering += (s, e) =>
            {
                if (AboutContent.Visibility != Visibility.Visible) return;

                double width = WaveCanvas.ActualWidth;
                double height = WaveCanvas.ActualHeight;
                if (width <= 0 || height <= 0) return;

                var time = DateTime.Now.TimeOfDay.TotalSeconds;
                var points1 = new List<Point>();
                var points2 = new List<Point>();

                // 波浪1
                for (double x = 0; x <= width; x += width / 10)
                {
                    double y = Math.Sin(x / 200 + time * 5) * 25 + height / 2;
                    points1.Add(new Point(x, y));
                }

                // 波浪2
                for (double x = 0; x <= width; x += width / 10)
                {
                    double y = Math.Sin(x / 180 + time * 3) * 35 + height / 2;
                    points2.Add(new Point(x, y));
                }

                // 更新路径
                WaveSegment1.Points = new PointCollection(points1);
                WaveSegment2.Points = new PointCollection(points2);
                
                // 设置起点
                if (WavePath1.Data is PathGeometry geometry1 && geometry1.Figures.Count > 0)
                {
                    geometry1.Figures[0].StartPoint = points1[0];
                }
                if (WavePath2.Data is PathGeometry geometry2 && geometry2.Figures.Count > 0)
                {
                    geometry2.Figures[0].StartPoint = points2[0];
                }
            };
        }

        private void OpenRepositoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/yzyyz1387/RogerThat",
                    UseShellExecute = true
                });
                LogMessage("正在打开仓库页面", LogType.Info);
            }
            catch (Exception ex)
            {
                LogMessage($"打开仓库页面失败: {ex.Message}", LogType.Error);
            }
        }

        private async void MITLicense_Click(object sender, RoutedEventArgs e)
        {
            var mitLicense = @"MIT License

Copyright (c) 2024 幼稚园园长

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";

            await DialogService.ShowMessageDialog("MIT 开源协议", mitLicense, maxHeight: 400);
        }

        private async Task CheckUpdateOnStartup()
        {
            try
            {
                var currentVersion = VersionInfo.Version;
                var updateInfo = await _updateService.CheckForUpdates();

                if (_updateService.IsNewVersionAvailable(currentVersion, updateInfo.Version) &&
                    _updateService.ShouldRemindUpdate(updateInfo.Version))
                {
                    var result = await DialogService.ShowCustomDialog(
                        "发现新版本",
                        $"发现新版本 {updateInfo.Version}\n" +
                        $"发布日期: {updateInfo.ReleaseDate}\n\n" +
                        "更新内容:\n" +
                        string.Join("\n", updateInfo.Changelog.Select(c => "• " + c)),
                        new[] { "从 GitHub 下载", "从 Gitee 下载", "此版本不再提醒", "关闭" },
                        new[] { "推荐：如果您能访问 GitHub，下载速度会更快", "备用：GitHub 无法访问时的备选", null, null }
                    );

                    switch (result)
                    {
                        case 0: // 从 GitHub 下载
                            await DownloadAndInstallUpdate(updateInfo, true, "github");
                            break;
                        case 1: // 从 Gitee 下载
                            await DownloadAndInstallUpdate(updateInfo, true, "gitee");
                            break;
                        case 2: // 此版本不再提醒
                            _updateService.IgnoreVersion(updateInfo.Version);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"自动检查更新失败: {ex.Message}", LogType.Error);
            }
        }

        private async Task DownloadAndInstallUpdate(UpdateInfo updateInfo, bool showProgress, string? preferredSource = null)
        {
            try
            {
                // 创建临时目录
                var tempDir = Path.Combine(Path.GetTempPath(), "RogerThatUpdate");
                Directory.CreateDirectory(tempDir);
                var downloadPath = Path.Combine(tempDir, "update.zip");

                if (showProgress)
                {
                    UpdateProgressPopup.Visibility = Visibility.Visible;
                    UpdateProgressBarPopup.Value = 0;
                    UpdateProgressBarPopup.IsIndeterminate = false;
                }

                var progress = new Progress<double>(value => 
                {
                    if (showProgress)
                    {
                        UpdateProgressBarPopup.Value = value * 100;
                        UpdateProgressText.Text = $"{value:P0}";
                    }
                });

                await _updateService.DownloadAndVerifyUpdate(updateInfo, downloadPath, progress, preferredSource);

                if (showProgress)
                {
                    UpdateProgressTitle.Text = "正在准备更新...";
                    UpdateProgressBarPopup.IsIndeterminate = true;
                    UpdateProgressText.Text = "";
                }

                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                string updateScript = await _updateService.ExtractAndUpdate(downloadPath, appDir);

                // 提示重启
                var restartResult = await DialogService.ShowConfirmDialog(
                    "更新准备就绪",
                    "更新文件已准备就绪，需要重启应用程序来完成更新。\n是否立即重启？\n\n" +
                    "这可能会消耗一些时间，请您耐心等待。"
                );

                if (restartResult)
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = updateScript,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(startInfo);
                    System.Windows.Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"更新失败: {ex.Message}", LogType.Error);
                await DialogService.ShowMessageDialog("更新失败", ex.Message);
            }
            finally
            {
                if (showProgress)
                {
                    UpdateProgressPopup.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;
            statusText.Visibility = Visibility.Visible;

            try
            {
                var currentVersion = VersionInfo.Version;
                var updateInfo = await _updateService.CheckForUpdates();

                if (_updateService.IsNewVersionAvailable(currentVersion, updateInfo.Version))
                {
                    var result = await DialogService.ShowCustomDialog(
                        "发现新版本",
                        $"发现新版本 {updateInfo.Version}\n" +
                        $"发布日期: {updateInfo.ReleaseDate}\n\n" +
                        "更新内容:\n" +
                        string.Join("\n", updateInfo.Changelog.Select(c => "• " + c)) +
                        "\n\n是否立即更新？",
                        new[] { "从 GitHub 下载", "从 Gitee 下载", "取消" },
                        new[] { "推荐：如果您能访问 GitHub，下载速度会更快", "备用：GitHub 无法访问时的备选", null }
                    );

                    switch (result)
                    {
                        case 0: // 从 GitHub 下载
                            await DownloadAndInstallUpdate(updateInfo, true, "github");
                            break;
                        case 1: // 从 Gitee 下载
                            await DownloadAndInstallUpdate(updateInfo, true, "gitee");
                            break;
                    }
                }
                else
                {
                    statusText.Text = "当前已是最新版本";
                    await Task.Delay(3000);
                }
            }
            catch (Exception ex)
            {
                statusText.Text = "检查更新失败";
                await Task.Delay(3000);
                LogMessage($"检查更新失败: {ex.Message}", LogType.Error);
                LogMessage($"错误详情: {ex}", LogType.Error);
            }
            finally
            {
                button.IsEnabled = true;
                statusText.Visibility = Visibility.Collapsed;
                statusText.Text = "正在检查新版本...";
            }
        }
    }
} 