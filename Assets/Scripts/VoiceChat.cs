using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;

/// <summary>
/// Компонент голосового чата для Mirror с низкой задержкой.
/// Записывает звук с микрофона локального игрока и передаёт его всем игрокам в лобби.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VoiceChat : NetworkBehaviour
{
    [Header("Настройки микрофона")]
    [Tooltip("Частота дискретизации аудио")]
    public int sampleRate = 16000;
    
    [Tooltip("Длина записи в секундах (буфер)")]
    public int recordingLength = 1;
    
    [Tooltip("Размер сегмента для отправки (в сэмплах). Меньше = меньше задержка, но больше пакетов")]
    public int segmentSize = 800; // ~50ms при 16kHz (уменьшено с 1600)
    
    [Tooltip("Порог громкости для активации голоса (0-1)")]
    [Range(0f, 1f)]
    public float voiceActivationThreshold = 0.01f;
    
    [Tooltip("Использовать активацию голосом (VAD)")]
    public bool useVoiceActivation = true;
    
    [Tooltip("Клавиша для передачи голоса (Push-to-Talk)")]
    public KeyCode pushToTalkKey = KeyCode.V;
    
    [Tooltip("Использовать Push-to-Talk вместо VAD")]
    public bool usePushToTalk = false;

    [Header("Настройки воспроизведения")]
    [Tooltip("Громкость воспроизведения голоса других игроков")]
    [Range(0f, 2f)]
    public float playbackVolume = 1f;
    
    [Tooltip("3D звук (позиционный)")]
    public bool use3DAudio = true;
    
    [Tooltip("Минимальная дистанция 3D звука")]
    public float minDistance = 1f;
    
    [Tooltip("Максимальная дистанция 3D звука")]
    public float maxDistance = 50f;
    
    [Tooltip("Размер джиттер-буфера (мс). Меньше = меньше задержка, но возможны разрывы")]
    [Range(20, 200)]
    public int jitterBufferMs = 50;

    [Header("Диагностика")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private bool isSpeaking = false;
    [SerializeField] private string currentMicrophone = "";
    [SerializeField] private float currentVolume = 0f;
    [SerializeField] private bool isPlaybackSetup = false;
    [SerializeField] private int bufferedSamples = 0;
    [SerializeField] private float latencyMs = 0f;

    // Компоненты
    private AudioSource audioSource;
    private AudioClip microphoneClip;
    
    // Буферы записи
    private int lastSamplePosition = 0;
    private float[] sampleBuffer;
    
    // Буферы воспроизведения - кольцевой буфер
    private float[] playbackBuffer;
    private int writePosition = 0;
    private int readPosition = 0;
    private int samplesInBuffer = 0;
    private readonly object bufferLock = new object();
    
    // Размер буфера: 500ms максимум (достаточно для джиттера)
    private int PlaybackBufferSize => sampleRate / 2;
    
    // Джиттер-буфер: минимальное количество сэмплов перед воспроизведением
    private int JitterBufferSamples => (sampleRate * jitterBufferMs) / 1000;
    
    private bool isPlayingVoice = false;
    private bool hasEnoughDataToPlay = false;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        sampleBuffer = new float[segmentSize];
        playbackBuffer = new float[PlaybackBufferSize];
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log("[VoiceChat] Локальный игрок - запускаем микрофон");
        StartMicrophone();
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();
        StopMicrophone();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        if (!isLocalPlayer)
        {
            SetupPlayback();
        }
    }

    private void Start()
    {
        if (isClient && !isLocalPlayer && !isPlaybackSetup)
        {
            SetupPlayback();
        }
    }

    private void SetupPlayback()
    {
        if (isPlaybackSetup) return;
        
        Debug.Log($"[VoiceChat] Настройка воспроизведения для игрока {netId}");
        
        // Настраиваем AudioSource
        audioSource.volume = playbackVolume;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        
        if (use3DAudio)
        {
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }
        else
        {
            audioSource.spatialBlend = 0f;
        }
        
        // Создаём короткий AudioClip для низкой задержки
        // Используем НЕ-стриминговый клип с OnAudioFilterRead
        int clipLength = sampleRate / 10; // 100ms клип
        AudioClip clip = AudioClip.Create($"Voice_{netId}", clipLength, 1, sampleRate, false);
        
        // Заполняем тишиной
        float[] silence = new float[clipLength];
        clip.SetData(silence, 0);
        
        audioSource.clip = clip;
        
        isPlaybackSetup = true;
        Debug.Log($"[VoiceChat] Воспроизведение настроено, джиттер-буфер: {jitterBufferMs}ms");
    }

    // Вызывается Unity для обработки аудио (низкая задержка!)
    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (isLocalPlayer || !isPlaybackSetup) return;
        
        lock (bufferLock)
        {
            // Обновляем диагностику
            bufferedSamples = samplesInBuffer;
            latencyMs = (samplesInBuffer * 1000f) / sampleRate;
            
            // Проверяем, достаточно ли данных для воспроизведения
            if (!hasEnoughDataToPlay)
            {
                if (samplesInBuffer >= JitterBufferSamples)
                {
                    hasEnoughDataToPlay = true;
                }
                else
                {
                    // Заполняем тишиной пока буфер не заполнится
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = 0;
                    }
                    return;
                }
            }
            
            // Воспроизводим данные из буфера
            int samplesToRead = data.Length / channels;
            
            for (int i = 0; i < samplesToRead; i++)
            {
                float sample = 0f;
                
                if (samplesInBuffer > 0)
                {
                    sample = playbackBuffer[readPosition];
                    playbackBuffer[readPosition] = 0f;
                    readPosition = (readPosition + 1) % PlaybackBufferSize;
                    samplesInBuffer--;
                }
                else
                {
                    // Буфер опустел - сбрасываем джиттер-буфер
                    hasEnoughDataToPlay = false;
                }
                
                // Записываем во все каналы
                for (int ch = 0; ch < channels; ch++)
                {
                    data[i * channels + ch] = sample;
                }
            }
        }
    }

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[VoiceChat] Микрофон не найден!");
            return;
        }

        currentMicrophone = Microphone.devices[0];
        Debug.Log($"[VoiceChat] Микрофон: {currentMicrophone}");

        microphoneClip = Microphone.Start(currentMicrophone, true, recordingLength, sampleRate);
        
        // Ждём начала записи с таймаутом
        int timeout = 100;
        while (Microphone.GetPosition(currentMicrophone) <= 0 && timeout > 0)
        {
            timeout--;
            System.Threading.Thread.Sleep(10);
        }
        
        if (timeout <= 0)
        {
            Debug.LogError("[VoiceChat] Таймаут запуска микрофона!");
            return;
        }
        
        isRecording = true;
        lastSamplePosition = Microphone.GetPosition(currentMicrophone);
        Debug.Log("[VoiceChat] Микрофон запущен");
    }

    private void StopMicrophone()
    {
        if (isRecording && !string.IsNullOrEmpty(currentMicrophone))
        {
            Microphone.End(currentMicrophone);
            isRecording = false;
            Debug.Log("[VoiceChat] Микрофон остановлен");
        }
    }

    private void Update()
    {
        if (isLocalPlayer && isRecording)
        {
            ProcessMicrophoneInput();
        }
        
        // Запускаем воспроизведение если нужно
        if (!isLocalPlayer && isPlaybackSetup && hasEnoughDataToPlay && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private void ProcessMicrophoneInput()
    {
        if (microphoneClip == null) return;

        int currentPosition = Microphone.GetPosition(currentMicrophone);
        if (currentPosition < 0) return;

        // Проверяем режим передачи
        bool canSpeak = usePushToTalk ? Input.GetKey(pushToTalkKey) : true;

        // Вычисляем доступные сэмплы
        int samplesAvailable;
        if (currentPosition >= lastSamplePosition)
        {
            samplesAvailable = currentPosition - lastSamplePosition;
        }
        else
        {
            samplesAvailable = (microphoneClip.samples - lastSamplePosition) + currentPosition;
        }

        // Обрабатываем сегментами
        while (samplesAvailable >= segmentSize)
        {
            microphoneClip.GetData(sampleBuffer, lastSamplePosition);
            
            float volume = CalculateVolume(sampleBuffer);
            currentVolume = volume;

            bool voiceDetected = !useVoiceActivation || volume > voiceActivationThreshold;
            isSpeaking = canSpeak && voiceDetected;

            if (isSpeaking)
            {
                byte[] compressedData = CompressAudioData(sampleBuffer);
                CmdSendVoiceData(compressedData);
            }

            lastSamplePosition = (lastSamplePosition + segmentSize) % microphoneClip.samples;
            samplesAvailable -= segmentSize;
        }
    }

    private float CalculateVolume(float[] samples)
    {
        float sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += Mathf.Abs(samples[i]);
        }
        return sum / samples.Length;
    }

    private byte[] CompressAudioData(float[] samples)
    {
        byte[] data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcmValue = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(pcmValue & 0xFF);
            data[i * 2 + 1] = (byte)((pcmValue >> 8) & 0xFF);
        }
        return data;
    }

    private float[] DecompressAudioData(byte[] data)
    {
        float[] samples = new float[data.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcmValue = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            samples[i] = pcmValue / (float)short.MaxValue;
        }
        return samples;
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdSendVoiceData(byte[] voiceData)
    {
        RpcReceiveVoiceData(voiceData);
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcReceiveVoiceData(byte[] voiceData)
    {
        if (isLocalPlayer) return;
        
        if (!isPlaybackSetup)
        {
            SetupPlayback();
        }

        float[] samples = DecompressAudioData(voiceData);
        
        lock (bufferLock)
        {
            // Записываем в кольцевой буфер
            for (int i = 0; i < samples.Length; i++)
            {
                // Проверяем переполнение буфера
                if (samplesInBuffer >= PlaybackBufferSize - 1)
                {
                    // Буфер полон - пропускаем старые данные
                    readPosition = (readPosition + 1) % PlaybackBufferSize;
                    samplesInBuffer--;
                }
                
                playbackBuffer[writePosition] = samples[i];
                writePosition = (writePosition + 1) % PlaybackBufferSize;
                samplesInBuffer++;
            }
        }
    }

    public void SetMicrophone(string deviceName)
    {
        if (!isLocalPlayer) return;
        
        StopMicrophone();
        
        if (Array.IndexOf(Microphone.devices, deviceName) >= 0)
        {
            currentMicrophone = deviceName;
            StartMicrophone();
        }
    }

    public static string[] GetAvailableMicrophones() => Microphone.devices;

    public void SetMuted(bool muted)
    {
        if (!isLocalPlayer) return;
        if (muted) StopMicrophone();
        else StartMicrophone();
    }

    public void SetPlaybackVolume(float volume)
    {
        playbackVolume = Mathf.Clamp(volume, 0f, 2f);
        if (audioSource != null)
            audioSource.volume = playbackVolume;
    }

    public bool IsSpeaking => isSpeaking;
    public bool IsRecording => isRecording;
    public float LatencyMs => latencyMs;

    private void OnDestroy() => StopMicrophone();
    private void OnDisable() => StopMicrophone();

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) StopMicrophone();
        else if (isLocalPlayer) StartMicrophone();
    }
}
