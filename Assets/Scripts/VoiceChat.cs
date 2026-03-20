using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;

/// <summary>
/// Компонент голосового чата для Mirror.
/// Записывает звук с микрофона локального игрока и передаёт его всем игрокам в лобби.
/// Добавьте этот компонент на префаб игрока вместе с NetworkIdentity.
/// 
/// Как это работает:
/// 1. Локальный игрок: записывает микрофон ? отправляет CmdSendVoiceData на сервер
/// 2. Сервер: получает данные ? рассылает RpcReceiveVoiceData всем клиентам
/// 3. Удалённые игроки: AudioSource на GameObject говорящего воспроизводит его голос
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VoiceChat : NetworkBehaviour
{
    [Header("Настройки микрофона")]
    [Tooltip("Частота дискретизации аудио")]
    public int sampleRate = 16000;
    
    [Tooltip("Длина записи в секундах (буфер)")]
    public int recordingLength = 1;
    
    [Tooltip("Размер сегмента для отправки (в сэмплах)")]
    public int segmentSize = 1600; // ~100ms при 16kHz
    
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

    [Header("Диагностика")]
    [SerializeField] private bool isRecording = false;
    [SerializeField] private bool isSpeaking = false;
    [SerializeField] private string currentMicrophone = "";
    [SerializeField] private float currentVolume = 0f;
    [SerializeField] private bool isPlaybackSetup = false;

    // Компоненты
    private AudioSource audioSource;
    private AudioClip microphoneClip;
    
    // Буферы
    private int lastSamplePosition = 0;
    private float[] sampleBuffer;
    private Queue<float[]> playbackQueue = new Queue<float[]>();
    
    // Воспроизведение
    private AudioClip playbackClip;
    private int playbackWritePosition = 0;
    private int playbackReadPosition = 0;
    private bool isPlayingVoice = false;
    private float[] playbackBuffer;
    private const int PLAYBACK_BUFFER_SIZE = 32000; // 2 секунды при 16kHz
    private object bufferLock = new object();

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        sampleBuffer = new float[segmentSize];
        playbackBuffer = new float[PLAYBACK_BUFFER_SIZE];
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log("[VoiceChat] OnStartLocalPlayer - это локальный игрок, запускаем микрофон");
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
        Debug.Log($"[VoiceChat] OnStartClient - isLocalPlayer: {isLocalPlayer}, netId: {netId}");
        
        // Для удалённых игроков настраиваем воспроизведение
        // isLocalPlayer уже должен быть установлен на этом этапе
        if (!isLocalPlayer)
        {
            SetupPlayback();
        }
    }

    private void Start()
    {
        // Дополнительная проверка на случай, если OnStartClient вызвался раньше
        if (isClient && !isLocalPlayer && !isPlaybackSetup)
        {
            Debug.Log("[VoiceChat] Start - отложенная настройка воспроизведения");
            SetupPlayback();
        }
    }

    private void SetupPlayback()
    {
        if (isPlaybackSetup) return;
        
        Debug.Log($"[VoiceChat] SetupPlayback для игрока {netId}");
        
        // Настраиваем AudioSource для воспроизведения голоса других игроков
        audioSource.volume = playbackVolume;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        
        if (use3DAudio)
        {
            audioSource.spatialBlend = 1f; // 3D звук
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }
        else
        {
            audioSource.spatialBlend = 0f; // 2D звук
        }
        
        // Создаём AudioClip для воспроизведения с PCMReaderCallback
        playbackClip = AudioClip.Create(
            $"VoicePlayback_{netId}", 
            PLAYBACK_BUFFER_SIZE, 
            1, 
            sampleRate, 
            true,  // stream = true для использования callback
            OnAudioRead
        );
        audioSource.clip = playbackClip;
        
        isPlaybackSetup = true;
        Debug.Log($"[VoiceChat] Воспроизведение настроено для игрока {netId}");
    }

    private void OnAudioRead(float[] data)
    {
        // Callback вызывается Unity для заполнения аудиобуфера
        lock (bufferLock)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (playbackBuffer[playbackReadPosition] != 0)
                {
                    data[i] = playbackBuffer[playbackReadPosition];
                    playbackBuffer[playbackReadPosition] = 0;
                }
                else
                {
                    data[i] = 0;
                }
                playbackReadPosition = (playbackReadPosition + 1) % PLAYBACK_BUFFER_SIZE;
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
        Debug.Log($"[VoiceChat] Используется микрофон: {currentMicrophone}");

        // Запускаем запись
        microphoneClip = Microphone.Start(currentMicrophone, true, recordingLength, sampleRate);
        
        // Ждём начала записи (с таймаутом)
        float timeout = Time.realtimeSinceStartup + 2f;
        while (!(Microphone.GetPosition(currentMicrophone) > 0))
        {
            if (Time.realtimeSinceStartup > timeout)
            {
                Debug.LogError("[VoiceChat] Таймаут ожидания микрофона!");
                return;
            }
        }
        
        isRecording = true;
        Debug.Log("[VoiceChat] Запись микрофона начата");
    }

    private void StopMicrophone()
    {
        if (isRecording && !string.IsNullOrEmpty(currentMicrophone))
        {
            Microphone.End(currentMicrophone);
            isRecording = false;
            Debug.Log("[VoiceChat] Запись микрофона остановлена");
        }
    }

    private void Update()
    {
        if (isLocalPlayer && isRecording)
        {
            ProcessMicrophoneInput();
        }
    }

    private void ProcessMicrophoneInput()
    {
        if (microphoneClip == null) return;

        int currentPosition = Microphone.GetPosition(currentMicrophone);
        if (currentPosition < 0) return;

        // Проверяем, можно ли говорить
        bool canSpeak = false;
        if (usePushToTalk)
        {
            canSpeak = Input.GetKey(pushToTalkKey);
        }
        else if (useVoiceActivation)
        {
            canSpeak = true; // VAD проверяется ниже
        }
        else
        {
            canSpeak = true; // Всегда передавать
        }

        // Вычисляем, сколько новых сэмплов доступно
        int samplesAvailable;
        if (currentPosition >= lastSamplePosition)
        {
            samplesAvailable = currentPosition - lastSamplePosition;
        }
        else
        {
            samplesAvailable = (microphoneClip.samples - lastSamplePosition) + currentPosition;
        }

        // Обрабатываем сэмплы сегментами
        while (samplesAvailable >= segmentSize)
        {
            // Читаем сегмент
            microphoneClip.GetData(sampleBuffer, lastSamplePosition);
            
            // Вычисляем громкость
            float volume = CalculateVolume(sampleBuffer);
            currentVolume = volume;

            // Проверяем VAD
            bool voiceDetected = !useVoiceActivation || volume > voiceActivationThreshold;
            isSpeaking = canSpeak && voiceDetected;

            if (isSpeaking)
            {
                // Сжимаем и отправляем данные
                byte[] compressedData = CompressAudioData(sampleBuffer);
                CmdSendVoiceData(compressedData);
            }

            // Обновляем позицию
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

    /// <summary>
    /// Простое сжатие: конвертируем float в 16-bit PCM
    /// </summary>
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

    /// <summary>
    /// Декомпрессия: конвертируем 16-bit PCM обратно в float
    /// </summary>
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

    /// <summary>
    /// Команда: отправляем голосовые данные на сервер
    /// </summary>
    [Command(channel = Channels.Unreliable)]
    private void CmdSendVoiceData(byte[] voiceData)
    {
        // Сервер получил данные, рассылаем всем клиентам (кроме отправителя)
        RpcReceiveVoiceData(voiceData);
    }

    /// <summary>
    /// RPC: получаем голосовые данные от сервера
    /// Вызывается на всех клиентах (кроме владельца) на GameObject говорящего игрока
    /// </summary>
    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcReceiveVoiceData(byte[] voiceData)
    {
        // Этот метод вызывается на GameObject говорящего игрока
        // но выполняется на клиентах других игроков
        if (isLocalPlayer) return;
        
        // Убеждаемся что воспроизведение настроено
        if (!isPlaybackSetup)
        {
            SetupPlayback();
        }

        // Декомпрессируем данные
        float[] samples = DecompressAudioData(voiceData);
        
        // Добавляем в кольцевой буфер воспроизведения
        lock (bufferLock)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                playbackBuffer[playbackWritePosition] = samples[i];
                playbackWritePosition = (playbackWritePosition + 1) % PLAYBACK_BUFFER_SIZE;
            }
        }

        // Начинаем воспроизведение, если ещё не играет
        if (!isPlayingVoice && audioSource != null)
        {
            isPlayingVoice = true;
            audioSource.Play();
            Debug.Log($"[VoiceChat] Начато воспроизведение голоса от игрока {netId}");
        }
    }

    /// <summary>
    /// Переключить микрофон
    /// </summary>
    public void SetMicrophone(string deviceName)
    {
        if (!isLocalPlayer) return;
        
        StopMicrophone();
        
        if (Array.IndexOf(Microphone.devices, deviceName) >= 0)
        {
            currentMicrophone = deviceName;
            StartMicrophone();
        }
        else
        {
            Debug.LogWarning($"[VoiceChat] Микрофон '{deviceName}' не найден!");
        }
    }

    /// <summary>
    /// Получить список доступных микрофонов
    /// </summary>
    public static string[] GetAvailableMicrophones()
    {
        return Microphone.devices;
    }

    /// <summary>
    /// Включить/выключить передачу голоса
    /// </summary>
    public void SetMuted(bool muted)
    {
        if (!isLocalPlayer) return;
        
        if (muted)
        {
            StopMicrophone();
        }
        else
        {
            StartMicrophone();
        }
    }

    /// <summary>
    /// Установить громкость воспроизведения
    /// </summary>
    public void SetPlaybackVolume(float volume)
    {
        playbackVolume = Mathf.Clamp(volume, 0f, 2f);
        if (audioSource != null)
        {
            audioSource.volume = playbackVolume;
        }
    }

    /// <summary>
    /// Проверить, говорит ли этот игрок сейчас
    /// </summary>
    public bool IsSpeaking => isSpeaking;

    /// <summary>
    /// Проверить, идёт ли запись
    /// </summary>
    public bool IsRecording => isRecording;

    private void OnDestroy()
    {
        StopMicrophone();
    }

    private void OnDisable()
    {
        StopMicrophone();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopMicrophone();
        }
        else if (isLocalPlayer)
        {
            StartMicrophone();
        }
    }
}
