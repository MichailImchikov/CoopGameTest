using UnityEngine;
using Mirror;
using System;
using Steamworks;

/// <summary>
/// Голосовой чат через Steam Voice API.
/// Использует встроенное сжатие Opus, шумоподавление и эхоподавение Steam.
/// </summary>
public class VoiceChat : NetworkBehaviour
{
    [Header("Настройки Steam Voice")]
    [Tooltip("Автоматически начинать запись при старте")]
    public bool autoStartRecording = true;
    
    [Tooltip("Клавиша для передачи голоса (Push-to-Talk)")]
    public KeyCode pushToTalkKey = KeyCode.V;
    
    [Tooltip("Использовать Push-to-Talk вместо постоянной передачи")]
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
    [SerializeField] private bool steamInitialized = false;
    [SerializeField] private bool isRecording = false;
    [SerializeField] private bool isSpeaking = false;
    [SerializeField] private int compressedBytesSent = 0;
    [SerializeField] private int packetsReceived = 0;
    [SerializeField] private int packetsSent = 0;

    // Steam Voice
    private const int SAMPLE_RATE = 48000;
    private byte[] compressedBuffer = new byte[8192];
    private byte[] decompressedBuffer = new byte[22050 * 2];
    
    // Воспроизведение
    private AudioSource audioSource;
    private float[] audioBuffer;
    private int writePosition = 0;
    private int readPosition = 0;
    private int samplesInBuffer = 0;
    private readonly object bufferLock = new object();
    private const int AUDIO_BUFFER_SIZE = 48000;
    
    private bool isPlaybackSetup = false;

    private void Awake()
    {
        audioBuffer = new float[AUDIO_BUFFER_SIZE];
    }

    /// <summary>
    /// Проверяет инициализацию Steam через SteamManager
    /// </summary>
    private bool CheckSteamInitialization()
    {
        // Сначала проверяем через SteamManager
        if (SteamManager.Instance != null && SteamManager.Initialized)
        {
            steamInitialized = true;
            return true;
        }
        
        // Fallback: прямая проверка Steam API
        try
        {
            if (!SteamAPI.IsSteamRunning())
            {
                Debug.LogWarning("[VoiceChat] Steam не запущен!");
                steamInitialized = false;
                return false;
            }
            
            CSteamID steamId = SteamUser.GetSteamID();
            if (!steamId.IsValid())
            {
                Debug.LogWarning("[VoiceChat] Steam ID невалидный!");
                steamInitialized = false;
                return false;
            }
            
            steamInitialized = true;
            Debug.Log($"[VoiceChat] Steam OK (fallback). User: {SteamFriends.GetPersonaName()}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[VoiceChat] Ошибка проверки Steam: {e.Message}");
            steamInitialized = false;
            return false;
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        
        Debug.Log("[VoiceChat] === ЛОКАЛЬНЫЙ ИГРОК ===");
        
        // Проверяем Steam
        if (!CheckSteamInitialization())
        {
            Debug.LogError("[VoiceChat] Steam не инициализирован! Убедитесь что:");
            Debug.LogError("  1. Steam запущен");
            Debug.LogError("  2. SteamManager есть на сцене");
            Debug.LogError("  3. steam_appid.txt существует (480 для тестов)");
            return;
        }
        
        Debug.Log($"[VoiceChat] Steam готов. User: {SteamFriends.GetPersonaName()}");
        
        if (autoStartRecording)
        {
            StartRecording();
        }
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();
        StopRecording();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        // Для удалённых игроков настраиваем воспроизведение
        if (!isLocalPlayer)
        {
            CheckSteamInitialization();
            SetupPlayback();
        }
    }

    private void SetupPlayback()
    {
        if (isPlaybackSetup) return;
        
        // Создаём GameObject для воспроизведения
        GameObject audioObj = new GameObject($"SteamVoice_{netId}");
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
            $"SteamVoice_{netId}",
            AUDIO_BUFFER_SIZE,
            1,
            SAMPLE_RATE,
            true,
            OnAudioRead,
            OnAudioSetPosition
        );
        
        audioSource.clip = clip;
        audioSource.Play();
        isPlaybackSetup = true;
        
        Debug.Log($"[VoiceChat] Воспроизведение настроено для игрока {netId}");
    }

    private void OnAudioRead(float[] data)
    {
        lock (bufferLock)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (samplesInBuffer > 0)
                {
                    data[i] = audioBuffer[readPosition];
                    audioBuffer[readPosition] = 0f;
                    readPosition = (readPosition + 1) % AUDIO_BUFFER_SIZE;
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
        // Не используем
    }

    public void StartRecording()
    {
        if (!isLocalPlayer) return;
        
        if (!steamInitialized && !CheckSteamInitialization())
        {
            Debug.LogError("[VoiceChat] Не могу начать запись - Steam не инициализирован");
            return;
        }
        
        SteamUser.StartVoiceRecording();
        isRecording = true;
        Debug.Log("[VoiceChat] Steam Voice запись начата");
    }

    public void StopRecording()
    {
        if (!isLocalPlayer) return;
        
        if (steamInitialized)
        {
            try
            {
                SteamUser.StopVoiceRecording();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VoiceChat] Ошибка остановки записи: {e.Message}");
            }
        }
        isRecording = false;
        isSpeaking = false;
    }

    private void Update()
    {
        if (!isLocalPlayer || !steamInitialized || !isRecording) return;
        
        // Push-to-Talk логика
        if (usePushToTalk)
        {
            if (Input.GetKeyDown(pushToTalkKey))
            {
                SteamUser.StartVoiceRecording();
            }
            else if (Input.GetKeyUp(pushToTalkKey))
            {
                SteamUser.StopVoiceRecording();
            }
        }
        
        ProcessVoice();
    }

    private void ProcessVoice()
    {
        try
        {
            EVoiceResult result = SteamUser.GetAvailableVoice(out uint compressedSize);
            
            if (result == EVoiceResult.k_EVoiceResultOK && compressedSize > 0)
            {
                isSpeaking = true;
                
                result = SteamUser.GetVoice(
                    true,
                    compressedBuffer,
                    (uint)compressedBuffer.Length,
                    out uint bytesWritten
                );
                
                if (result == EVoiceResult.k_EVoiceResultOK && bytesWritten > 0)
                {
                    byte[] dataToSend = new byte[bytesWritten];
                    Array.Copy(compressedBuffer, dataToSend, bytesWritten);
                    
                    CmdSendVoiceData(dataToSend);
                    
                    compressedBytesSent += (int)bytesWritten;
                    packetsSent++;
                }
            }
            else if (result == EVoiceResult.k_EVoiceResultNoData)
            {
                isSpeaking = false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[VoiceChat] Ошибка обработки голоса: {e.Message}");
            isRecording = false;
        }
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdSendVoiceData(byte[] compressedVoiceData)
    {
        RpcReceiveVoiceData(compressedVoiceData);
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    private void RpcReceiveVoiceData(byte[] compressedVoiceData)
    {
        if (isLocalPlayer) return;
        
        if (!steamInitialized && !CheckSteamInitialization())
        {
            return;
        }
        
        if (!isPlaybackSetup)
        {
            SetupPlayback();
        }
        
        packetsReceived++;
        
        try
        {
            EVoiceResult result = SteamUser.DecompressVoice(
                compressedVoiceData,
                (uint)compressedVoiceData.Length,
                decompressedBuffer,
                (uint)decompressedBuffer.Length,
                out uint bytesWritten,
                (uint)SAMPLE_RATE
            );
            
            if (result == EVoiceResult.k_EVoiceResultOK && bytesWritten > 0)
            {
                int sampleCount = (int)bytesWritten / 2;
                
                lock (bufferLock)
                {
                    for (int i = 0; i < sampleCount; i++)
                    {
                        short pcmSample = (short)(decompressedBuffer[i * 2] | (decompressedBuffer[i * 2 + 1] << 8));
                        float sample = pcmSample / 32768f;
                        
                        if (samplesInBuffer >= AUDIO_BUFFER_SIZE - 1)
                        {
                            readPosition = (readPosition + 1) % AUDIO_BUFFER_SIZE;
                            samplesInBuffer--;
                        }
                        
                        audioBuffer[writePosition] = sample;
                        writePosition = (writePosition + 1) % AUDIO_BUFFER_SIZE;
                        samplesInBuffer++;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[VoiceChat] Ошибка декомпрессии: {e.Message}");
        }
    }

    public void SetMuted(bool muted)
    {
        if (!isLocalPlayer) return;
        if (muted) StopRecording();
        else StartRecording();
    }

    public void SetPlaybackVolume(float volume)
    {
        playbackVolume = Mathf.Clamp(volume, 0f, 2f);
        if (audioSource != null)
            audioSource.volume = playbackVolume;
    }

    public bool IsSpeaking => isSpeaking;
    public bool IsRecording => isRecording;
    public bool IsSteamInitialized => steamInitialized;

    private void OnDestroy()
    {
        StopRecording();
        if (audioSource != null && audioSource.gameObject != this.gameObject)
        {
            Destroy(audioSource.gameObject);
        }
    }

    private void OnDisable() => StopRecording();

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus) StopRecording();
        else if (isLocalPlayer && autoStartRecording && steamInitialized) StartRecording();
    }

    // Отладочный UI
    private void OnGUI()
    {
        if (!isLocalPlayer) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 350, 230));
        GUI.color = Color.black;
        GUILayout.Label("=== Steam Voice Chat ===");
        
        string steamStatus = steamInitialized ? "OK" : "NOT INITIALIZED";
        string steamManagerStatus = (SteamManager.Instance != null && SteamManager.Initialized) ? "OK" : "NOT READY";
        
        GUILayout.Label($"SteamManager: {steamManagerStatus}");
        GUILayout.Label($"Steam Voice: {steamStatus}");
        GUILayout.Label($"Recording: {isRecording}");
        GUILayout.Label($"Speaking: {isSpeaking}");
        GUILayout.Label($"Packets Sent: {packetsSent}");
        GUILayout.Label($"Compressed KB: {compressedBytesSent / 1024f:F1}");
        
        if (usePushToTalk)
            GUILayout.Label($"Hold [{pushToTalkKey}] to talk");
        else
            GUILayout.Label("Voice Activation: ON");
        
        GUI.color = Color.white;
        GUILayout.EndArea();
    }
}
