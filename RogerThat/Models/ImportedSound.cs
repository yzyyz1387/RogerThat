using System;
using System.Windows.Input;
using System.ComponentModel;
using System.IO;
using RogerThat.Commands;

namespace RogerThat.Models
{
    public class ImportedSound : INotifyPropertyChanged
    {
        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public ICommand SetAsPrefixCommand { get; set; }
        public ICommand SetAsSuffixCommand { get; set; }
        public ICommand DeleteCommand { get; set; }
        public ICommand PlayCommand { get; set; }
        private bool _isPrefix;
        private bool _isSuffix;
        private bool _isPlaying;

        public bool IsPrefix
        {
            get => _isPrefix;
            set
            {
                if (_isPrefix != value)
                {
                    _isPrefix = value;
                    OnPropertyChanged(nameof(IsPrefix));
                }
            }
        }

        public bool IsSuffix
        {
            get => _isSuffix;
            set
            {
                if (_isSuffix != value)
                {
                    _isSuffix = value;
                    OnPropertyChanged(nameof(IsSuffix));
                }
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }

        public bool IsDefaultSound => FilePath.Contains(Path.Combine("Assets", "sounds"));
        public bool CanDelete => !IsDefaultSound;

        public string DisplayName
        {
            get
            {
                return FileName;
            }
        }

        public ImportedSound(string filePath)
        {
            FilePath = filePath;
            DeleteCommand = new RelayCommand(_ => RequestDelete?.Invoke(this, EventArgs.Empty));
        }

        public event EventHandler RequestDelete;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 