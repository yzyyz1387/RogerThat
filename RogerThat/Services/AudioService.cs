using NAudio.Wave;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RogerThat.Services;

public class AudioService : IDisposable
{
    private WaveOutEvent? waveOut;
    private AudioFileReader? audioFile;
    private string? prefixAudioPath;
    private string? suffixAudioPath;
    private bool isPlaying;
    private Key monitorKey;

    public event EventHandler<string>? LogMessage;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;

    public AudioService()
    {
        waveOut = new WaveOutEvent();
        waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
    }

    public void SetPrefixAudio(string path)
    {
        prefixAudioPath = path;
        LogMessage?.Invoke(this, $"已设置前置音频: {path}");
    }

    public void SetSuffixAudio(string path)
    {
        suffixAudioPath = path;
        LogMessage?.Invoke(this, $"已设置后置音频: {path}");
    }

    public void SetMonitorKey(Key key)
    {
        monitorKey = key;
        LogMessage?.Invoke(this, $"已设置监听按键: {key}");
    }

    public void PlayPrefixAudio()
    {
        if (string.IsNullOrEmpty(prefixAudioPath))
        {
            LogMessage?.Invoke(this, "未设置前置音频");
            return;
        }

        PlayAudio(prefixAudioPath);
    }

    public void PlaySuffixAudio()
    {
        if (string.IsNullOrEmpty(suffixAudioPath))
        {
            LogMessage?.Invoke(this, "未设置后置音频");
            return;
        }

        PlayAudio(suffixAudioPath);
    }

    private void PlayAudio(string path)
    {
        try
        {
            if (isPlaying)
            {
                waveOut?.Stop();
                audioFile?.Dispose();
            }

            audioFile = new AudioFileReader(path);
            waveOut?.Init(audioFile);
            waveOut?.Play();
            isPlaying = true;

            LogMessage?.Invoke(this, $"正在播放音频: {path}");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"播放音频失败: {ex.Message}");
        }
    }

    private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        isPlaying = false;
        if (monitorKey != Key.None)
        {
            // 模拟释放按键
            keybd_event((byte)KeyInterop.VirtualKeyFromKey(monitorKey), 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            LogMessage?.Invoke(this, $"音频播放结束，释放按键: {monitorKey}");
        }
    }

    public void Dispose()
    {
        waveOut?.Dispose();
        audioFile?.Dispose();
    }
} 