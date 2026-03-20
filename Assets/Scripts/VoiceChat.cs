using UnityEngine;
using Mirror;
using System;

/// <summary>
/// Компонент голосового чата для Mirror.
/// Записывает звук с микрофона локального игрока и передаёт его всем игрокам в лобби.
/// </summary>
public class VoiceChat : NetworkBehaviour
{
    [Header("Настройки микрофона")]
    [Tooltip("Частота дискретизации аудио")]
    public int sampleRate = 16000;
    
    [Tooltip("Длина записи в секундах (буфер)")]
    public int recordingLength = 1;
    
    [Tooltip("Размер сегмента для отправки (в сэмплах). Меньше = меньше задержка")]
    public int segmentSize = 800; // ~50ms при 16kHz
    
    [Tooltip("Порог громкости для активации голоса (0-1)")]
    [Range(0f, 0.1f)]
    public float voiceActivationThreshold = 0.005f;
    
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
    public bool use3DAudio = false;
    
    [Tooltip("Минимальная дистанция 3D звука")]
    public float minDistance = 1f;
    
    [Tooltip("Максимальная дистанция 3D звука")]
    public float maxDistance = 50f;

    [Header("Диагностика")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private bool isSpeaking = false;
    [SerializeField] private string currentMicrophone = "";
    [SerializeField] private float currentVolume = 0f;
    [SerializeField] private int bufferedSamples = 0;
    [SerializeField] private int packetsReceived = 0;
    [SerializeField] private int packetsSent = 0;

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
    
    // Размер буфера: 1 секунда
    private const int PLAYBACK_BUFFER_SIZE = 16000;
    
    private bool isInitialized = false;
    private bool isPlaybackSetup = false;

    private void Awake()
    {
        sampleBuffer = new float[segmentSize];
        playbackBuffer = new float[PLAYBACK_BUFFER_SIZE];
        isInitialized = true;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log("[VoiceChat] === ЭТО ЛОКАЛЬНЫЙ ИГРОК ===");
        StartMicrophone();
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();
        StopMicrophone();
    }

    private void Start()
    {
        // Для НЕ-локальных игроков настраиваем воспроизведение
        if (isClient && !isLocalPlayer)
        {
            Debug.Log($"[VoiceChat] Настройка воспроизведения для удалённого игрока {netId}");
            SetupPlayback();
        }
    }

    private void SetupPlayback()
    {
        if (isPlaybackSetup) return;
        
        // Создаём отдельный GameObject для воспроизведения голоса
        GameObject audioObj = new GameObject($"VoicePlayback_{netId}");
        audioObj.transform.SetParent(transform);
        audioObj.transform.localPosition = Vector3.zero;
        
        audioSource = audioObj.AddComponent<AudioSource>();
        audioSource.volume = playbackVolume;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.priority = 0;
        
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
        
        // Создаём стриминговый AudioClip
        AudioClip clip = AudioClip.Create(
            $"Voice_{netId}", 
            PLAYBACK_BUFFER_SIZE, 
            1, 
            sampleRate, 
            true, // stream
            OnAudioRead,
            OnAudioSetPosition
        );
        
        audioSource.clip = clip;
        audioSource.Play();
        isPlaybackSetup = true;
        
        Debug.Log($"[VoiceChat] Воспроизведение настроено для игрока {netId}");
    }

    // Callback для стримингового AudioClip
    private void OnAudioRead(float[] data)
    {
        if (!isInitialized) return;
        
        lock (bufferLock)
        {
            bufferedSamples = samplesInBuffer;
            
            for (int i = 0; i < data.Length; i++)
            {
                if (samplesInBuffer > 0)
                {
                    data[i] = playbackBuffer[readPosition];
                    playbackBuffer[readPosition] = 0f;
                    readPosition = (readPosition + 1) % PLAYBACK_BUFFER_SIZE;
                    samplesInBuffer--;
                }
                else
                {
                    data[i] = 0f;
                }
            }
        }
    }
    
    private void OnAudioSetPosition(int newPosition)
    {
        // Не используем, но нужен для AudioClip.Create
    }

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[VoiceChat] ОШИБКА: Микрофон не найден!");
            return;
        }

        currentMicrophone = Microphone.devices[0];
        Debug.Log($"[VoiceChat] Микрофон: {currentMicrophone}");

        microphoneClip = Microphone.Start(currentMicrophone, true, recordingLength, sampleRate);
        
        if (microphoneClip == null)
        {
            Debug.LogError("[VoiceChat] ОШИБКА: Не удалось создать AudioClip!");
            return;
        }

        // Ждём начала записи
        int waitCount = 0;
        while (Microphone.GetPosition(currentMicrophone) <= 0 && waitCount < 100)
        {
            waitCount++;
            System.Threading.Thread.Sleep(10);
        }
        
        if (waitCount >= 100)
        {
            Debug.LogError("[VoiceChat] ОШИБКА: Таймаут микрофона!");
            return;
        }
        
        isRecording = true;
        lastSamplePosition = Microphone.GetPosition(currentMicrophone);
        Debug.Log("[VoiceChat] Микрофон запущен!");
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
        if (!isLocalPlayer || !isRecording) return;
        ProcessMicrophoneInput();
    }

    private void ProcessMicrophoneInput()
    {
        if (microphoneClip == null) return;

        int currentPosition = Microphone.GetPosition(currentMicrophone);
        if (currentPosition < 0) return;

        // Проверяем режим передачи
        bool canSpeak = !usePushToTalk || Input.GetKey(pushToTalkKey);

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
            int readPos = lastSamplePosition % microphoneClip.samples;
            microphoneClip.GetData(sampleBuffer, readPos);
            
            // Вычисляем громкость (RMS)
            float sum = 0f;
            for (int i = 0; i < sampleBuffer.Length; i++)
            {
                sum += sampleBuffer[i] * sampleBuffer[i];
            }
            float rms = Mathf.Sqrt(sum / sampleBuffer.Length);
            currentVolume = rms;

            // Проверяем VAD
            bool voiceDetected = !useVoiceActivation || rms > voiceActivationThreshold;
            isSpeaking = canSpeak && voiceDetected;

            if (isSpeaking)
            {
                byte[] compressedData = CompressAudioData(sampleBuffer);
                CmdSendVoiceData(compressedData);
                packetsSent++;
            }

            lastSamplePosition = (lastSamplePosition + segmentSize) % microphoneClip.samples;
            samplesAvailable -= segmentSize;
        }
    }

    private byte[] CompressAudioData(float[] samples)
    {
        byte[] data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcmValue = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
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
            samples[i] = pcmValue / 32767f;
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
        
        packetsReceived++;
        
        float[] samples = DecompressAudioData(voiceData);
        
        lock (bufferLock)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                if (samplesInBuffer >= PLAYBACK_BUFFER_SIZE - 1)
                {
                    readPosition = (readPosition + 1) % PLAYBACK_BUFFER_SIZE;
                    samplesInBuffer--;
                }
                
                playbackBuffer[writePosition] = samples[i];
                writePosition = (writePosition + 1) % PLAYBACK_BUFFER_SIZE;
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

    private void OnDestroy()
    {
        StopMicrophone();
        if (audioSource != null && audioSource.gameObject != this.gameObject)
        {
            Destroy(audioSource.gameObject);
        }
    }
    
    private void OnDisable() => StopMicrophone();

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) StopMicrophone();
        else if (isLocalPlayer) StartMicrophone();
    }
    
    // Отладочный UI
    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 180));
        GUI.color = Color.black;
        GUILayout.Label("=== VoiceChat Debug ===");
        GUILayout.Label($"Recording: {isRecording}");
        GUILayout.Label($"Speaking: {isSpeaking}");
        GUILayout.Label($"Volume: {currentVolume:F4}");
        GUILayout.Label($"Threshold: {voiceActivationThreshold:F4}");
        GUILayout.Label($"Packets Sent: {packetsSent}");
        GUILayout.Label($"Microphone: {currentMicrophone}");
        if (usePushToTalk)
            GUILayout.Label($"Hold [{pushToTalkKey}] to talk");
        GUI.color = Color.white;
        GUILayout.EndArea();
    }
}
