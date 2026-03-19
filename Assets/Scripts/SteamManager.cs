using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

public class SteamManager : MonoBehaviour
{
    private static SteamManager s_instance;
    public static SteamManager Instance => s_instance;

    private bool m_bInitialized;
    public static bool Initialized => Instance != null && Instance.m_bInitialized;

#if !DISABLESTEAMWORKS
    public static CSteamID SteamId { get; private set; }
#endif

    private void Awake()
    {
        if (s_instance != null)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        DontDestroyOnLoad(gameObject);

#if !DISABLESTEAMWORKS
        if (!Packsize.Test())
        {
            Debug.LogError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.");
            return;
        }

        if (!DllCheck.Test())
        {
            Debug.LogError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.");
            return;
        }

        // ВАЖНО: Пропускаем RestartAppIfNecessary для разработки с App ID 480 (Spacewar)
        // Эта проверка нужна только для релизных игр с собственным App ID
        // Если у вас есть собственный App ID, раскомментируйте код ниже и замените 480 на ваш ID
        /*
        try
        {
            if (SteamAPI.RestartAppIfNecessary((AppId_t)YOUR_APP_ID))
            {
                Debug.Log("[SteamManager] Restarting app through Steam...");
                Application.Quit();
                return;
            }
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogError("[SteamManager] Could not load steam_api64.dll. Please make sure Steam is running.\n" + e);
            return;
        }
        */

        m_bInitialized = SteamAPI.Init();
        if (!m_bInitialized)
        {
            Debug.LogError("[SteamManager] SteamAPI_Init() failed. Ensure Steam is running and that steam_appid.txt is present.");
            return;
        }

        SteamId = SteamUser.GetSteamID();
        string playerName = SteamFriends.GetPersonaName();
        Debug.Log($"[SteamManager] Steam initialized! Player: {playerName} (ID: {SteamId})");
#else
        Debug.LogWarning("[SteamManager] Steamworks is disabled.");
#endif
    }

    private void Update()
    {
#if !DISABLESTEAMWORKS
        if (!m_bInitialized)
            return;

        SteamAPI.RunCallbacks();
#endif
    }

    private void OnApplicationQuit()
    {
#if !DISABLESTEAMWORKS
        if (m_bInitialized)
        {
            SteamAPI.Shutdown();
        }
#endif
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }
}
