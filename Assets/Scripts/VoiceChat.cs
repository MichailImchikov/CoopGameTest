using UnityEngine;
using Mirror;
using System;

/// <summary>
/// Голосовой чат с минимальной задержкой.
/// Постоянно записывает микрофон и отправляет данные без VAD.
/// </summary>
public class VoiceChat : NetworkBehaviour
{
    [Header("Настройки микрофона")]
    [Tooltip("Частота дискретизации (меньше = меньше задержка, но хуже качество)")]
    public int sampleRate = 11025; // Низкая частота для минимальной задержки
    
    [Tooltip("Размер сегмента в миллисекундах")]
    [Range(20, 100)]
    public int segmentMs = 20; // 20ms = очень низкая задержка
    
    [Tooltip("Клавиша для передачи голоса (Push-to-Talk)")]
    public KeyCode pushToTalkKey = KeyCode.V;
    
    [Tooltip("Использовать Push-to-Talk (если false - постоянная передача)")]
    public bool usePushToTalk = false;

    [Header("Настройки воспроизведения")]
    [Tooltip("Громкость воспроизведения")]
    [Range(0f, 2f)]
    public float playbackVolume = 1f;
    
    [Tooltip("3D звук")]
    public bool use3DAudio = false;
    
    public float minDistance = 1f;
    public float maxDistance = 50f;

    [Header("Диагностика")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private bool isTransmitting = false;
    [SerializeField] private string microphoneName = "";
    [SerializeField] private int packetsSent = 0;
    [SerializeField] private int packetsReceived = 0;
    [SerializeField] private float inputLevel = 0f;
    [SerializeField] private int bufferMs = 0;

    // Микрофон
    private AudioClip micClip;
    private int lastMicPosition = 0;
    private float[] micBuffer;
    private int segmentSamples;
    
    // Воспроизведение
    private AudioSource audioSource;
    private float[] playbackBuffer;
    private int writePos = 0;
    private int readPos = 0;
    private int samplesBuffered = 0;
    private readonly object bufferLock = new object();
    private const int BUFFER_SECONDS = 1;
    private int bufferSize;
    
    private bool isPlaybackSetup = false;

    private void Awake()
    {
        segmentSamples = (sampleRate * segmentMs) / 1000;
        micBuffer = new float[segmentSamples];
        bufferSize = sampleRate * BUFFER_SECONDS;
        playbackBuffer = new float[bufferSize];
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log("[VoiceChat] Локальный игрок - запуск микрофона");
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

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[VoiceChat] Микрофон не найден!");
            return;
        }

        microphoneName = Microphone.devices[0];
        
        // Запускаем микрофон с минимальным буфером (1 секунда - минимум Unity)
        micClip = Microphone.Start(microphoneName, true, 1, sampleRate);
        
        // Ждём запуска
        int wait = 0;
        while (Microphone.GetPosition(microphoneName) <= 0 && wait < 50)
        {
            wait++;
            System.Threading.Thread.Sleep(10);
        }
        
        if (wait >= 50)
        {
            Debug.LogError("[VoiceChat] Таймаут запуска микрофона!");
            return;
        }
        
        lastMicPosition = Microphone.GetPosition(microphoneName);
        isRecording = true;
        
        Debug.Log($"[VoiceChat] Микрофон запущен: {microphoneName}, {sampleRate}Hz, сегмент {segmentMs}ms ({segmentSamples} samples)");
    }

    private void StopMicrophone()
    {
        if (isRecording)
        {
            Microphone.End(microphoneName);
            isRecording = false;
        }
    }

    private void SetupPlayback()
    {
        if (isPlaybackSetup) return;
        
        GameObject audioObj = new GameObject($"Voice_{netId}");
        audioObj.transform.SetParent(transform);
        audioObj.transform.localPosition = Vector3.zero;
        
        audioSource = audioObj.AddComponent<AudioSource>();
        audioSource.volume = playbackVolume;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.priority = 0;
        audioSource.spatialBlend = use3DAudio ? 1f : 0f;
        
        if (use3DAudio)
        {
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }
        
        // Стриминговый клип для минимальной задержки
        AudioClip clip = AudioClip.Create($"Voice_{netId}", bufferSize, 1, sampleRate, true, OnAudioRead);
        audioSource.clip = clip;
        audioSource.Play();
        
        isPlaybackSetup = true;
        Debug.Log($"[VoiceChat] Воспроизведение готово для {netId}");
    }

    private void OnAudioRead(float[] data)
    {
        lock (bufferLock)
        {
            bufferMs = (samplesBuffered * 1000) / sampleRate;
            
            for (int i = 0; i < data.Length; i++)
            {
                if (samplesBuffered > 0)
                {
                    data[i] = playbackBuffer[readPos];
                    playbackBuffer[readPos] = 0f;
                    readPos = (readPos + 1) % bufferSize;
                    samplesBuffered--;
                }
                else
                {
                    data[i] = 0f;
                }
            }
        }
    }

    private void Update()
    {
        if (!isLocalPlayer || !isRecording) return;
        
        // Проверяем PTT
        isTransmitting = !usePushToTalk || Input.GetKey(pushToTalkKey);
        
        ProcessMicrophone();
    }

    private void ProcessMicrophone()
    {
        if (micClip == null) return;
        
        int currentPos = Microphone.GetPosition(microphoneName);
        if (currentPos < 0) return;
        
        // Вычисляем доступные сэмплы
        int available;
        if (currentPos >= lastMicPosition)
        {
            available = currentPos - lastMicPosition;
        }
        else
        {
            available = (micClip.samples - lastMicPosition) + currentPos;
        }
        
        // Отправляем сегментами
        while (available >= segmentSamples)
        {
            int readStart = lastMicPosition % micClip.samples;
            micClip.GetData(micBuffer, readStart);
            
            // Вычисляем уровень входа
            float sum = 0f;
            for (int i = 0; i < micBuffer.Length; i++)
            {
                sum += Mathf.Abs(micBuffer[i]);
            }
            inputLevel = sum / micBuffer.Length;
            
            if (isTransmitting)
            {
                // Сжимаем в 8-bit для минимального размера пакета
                byte[] data = CompressTo8Bit(micBuffer);
                CmdSendVoice(data);
                packetsSent++;
            }
            
            lastMicPosition = (lastMicPosition + segmentSamples) % micClip.samples;
            available -= segmentSamples;
        }
    }

    // 8-bit сжатие (µ-law подобное) для минимального размера пакетов
    private byte[] CompressTo8Bit(float[] samples)
    {
        byte[] data = new byte[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            // Простое линейное сжатие в 8 бит
            float clamped = Mathf.Clamp(samples[i], -1f, 1f);
            data[i] = (byte)((clamped + 1f) * 127.5f);
        }
        return data;
    }

    private float[] DecompressFrom8Bit(byte[] data)
    {
        float[] samples = new float[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            samples[i] = (data[i] / 127.5f) - 1f;
        }
        return samples;
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdSendVoice(byte[] data)
    {
        RpcReceiveVoice(data);
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcReceiveVoice(byte[] data)
    {
        if (isLocalPlayer) return;
        
        if (!isPlaybackSetup)
        {
            SetupPlayback();
        }
        
        packetsReceived++;
        
        float[] samples = DecompressFrom8Bit(data);
        
        lock (bufferLock)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                // При переполнении - отбрасываем старые данные
                if (samplesBuffered >= bufferSize - 1)
                {
                    readPos = (readPos + 1) % bufferSize;
                    samplesBuffered--;
                }
                
                playbackBuffer[writePos] = samples[i];
                writePos = (writePos + 1) % bufferSize;
                samplesBuffered++;
            }
        }
    }

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

    public bool IsTransmitting => isTransmitting;
    public bool IsRecording => isRecording;

    private void OnDestroy()
    {
        StopMicrophone();
        if (audioSource != null && audioSource.gameObject != gameObject)
        {
            Destroy(audioSource.gameObject);
        }
    }

    private void OnDisable() => StopMicrophone();

    // Отладочный UI
    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUI.color = Color.black;
        GUILayout.Label("=== Voice Chat (Low Latency) ===");
        GUILayout.Label($"Microphone: {(isRecording ? "ON" : "OFF")}");
        GUILayout.Label($"Transmitting: {isTransmitting}");
        GUILayout.Label($"Input Level: {inputLevel:F3}");
        GUILayout.Label($"Packets Sent: {packetsSent}");
        GUILayout.Label($"Segment: {segmentMs}ms ({segmentSamples} samples)");
        GUILayout.Label($"Sample Rate: {sampleRate}Hz");
        
        if (usePushToTalk)
            GUILayout.Label($"Hold [{pushToTalkKey}] to talk");
        else
            GUILayout.Label("Always transmitting");
        
        GUI.color = Color.white;
        GUILayout.EndArea();
    }
}
