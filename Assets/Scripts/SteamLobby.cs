using UnityEngine;
using Mirror;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

public class SteamLobby : MonoBehaviour
{
#if !DISABLESTEAMWORKS
    // Callbacks
    protected Callback<LobbyCreated_t> LobbyCreated;
    protected Callback<GameLobbyJoinRequested_t> JoinRequest;
    protected Callback<LobbyEnter_t> LobbyEntered;

    // Lobby ID
    public static CSteamID CurrentLobbyID { get; private set; }

    private NetworkManager networkManager;

    private const string HostAddressKey = "HostAddress";

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobby] SteamManager is not initialized!");
            return;
        }

        networkManager = GetComponent<NetworkManager>();
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<NetworkManager>();
        }

        // Setup callbacks
        LobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        JoinRequest = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequest);
        LobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);

        Debug.Log($"[SteamLobby] Steam Lobby initialized. Your Steam ID: {SteamUser.GetSteamID()}");
        Debug.Log($"[SteamLobby] Your Steam Name: {SteamFriends.GetPersonaName()}");
    }

    /// <summary>
    /// Create a new Steam lobby and start hosting
    /// </summary>
    public void HostLobby()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobby] Cannot create lobby - Steam not initialized!");
            return;
        }

        Debug.Log("[SteamLobby] Creating lobby...");
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, networkManager.maxConnections);
    }

    /// <summary>
    /// Join a lobby by Steam ID (for manual joining)
    /// </summary>
    public void JoinLobbyById(ulong lobbyId)
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobby] Cannot join lobby - Steam not initialized!");
            return;
        }

        Debug.Log($"[SteamLobby] Attempting to join lobby: {lobbyId}");
        SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
    }

    /// <summary>
    /// Join a lobby by Steam ID string
    /// </summary>
    public void JoinLobbyById(string lobbyIdString)
    {
        if (ulong.TryParse(lobbyIdString, out ulong lobbyId))
        {
            JoinLobbyById(lobbyId);
        }
        else
        {
            Debug.LogError($"[SteamLobby] Invalid lobby ID: {lobbyIdString}");
        }
    }

    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError($"[SteamLobby] Failed to create lobby: {callback.m_eResult}");
            return;
        }

        CurrentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
        Debug.Log($"[SteamLobby] ========================================");
        Debug.Log($"[SteamLobby] Lobby created successfully!");
        Debug.Log($"[SteamLobby] Lobby ID: {CurrentLobbyID}");
        Debug.Log($"[SteamLobby] Host Steam ID: {SteamUser.GetSteamID()}");
        Debug.Log($"[SteamLobby] ========================================");
        Debug.Log($"[SteamLobby] Friends can join via Steam overlay or friend list!");

        // Copy lobby ID to clipboard for easy sharing
        GUIUtility.systemCopyBuffer = CurrentLobbyID.ToString();
        Debug.Log($"[SteamLobby] Lobby ID copied to clipboard!");

        // Start hosting
        networkManager.StartHost();

        // Set lobby data so others can find the host
        SteamMatchmaking.SetLobbyData(
            CurrentLobbyID,
            HostAddressKey,
            SteamUser.GetSteamID().ToString()
        );

        Debug.Log("[SteamLobby] Host started, waiting for players...");
    }

    private void OnJoinRequest(GameLobbyJoinRequested_t callback)
    {
        Debug.Log($"[SteamLobby] Received join request for lobby: {callback.m_steamIDLobby}");
        Debug.Log($"[SteamLobby] From friend: {callback.m_steamIDFriend}");
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    private void OnLobbyEntered(LobbyEnter_t callback)
    {
        CurrentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
        Debug.Log($"[SteamLobby] Entered lobby: {CurrentLobbyID}");

        // If we're the host, we already started hosting
        if (NetworkServer.active)
        {
            Debug.Log("[SteamLobby] We are the host");
            return;
        }

        // Get host address from lobby data
        string hostAddress = SteamMatchmaking.GetLobbyData(CurrentLobbyID, HostAddressKey);

        if (string.IsNullOrEmpty(hostAddress))
        {
            Debug.LogError("[SteamLobby] Could not get host address from lobby!");
            return;
        }

        Debug.Log($"[SteamLobby] Connecting to host: {hostAddress}");
        networkManager.networkAddress = hostAddress;
        networkManager.StartClient();
    }

    /// <summary>
    /// Leave the current lobby
    /// </summary>
    public void LeaveLobby()
    {
        if (CurrentLobbyID.IsValid())
        {
            SteamMatchmaking.LeaveLobby(CurrentLobbyID);
            CurrentLobbyID = CSteamID.Nil;
            Debug.Log("[SteamLobby] Left lobby");
        }

        if (NetworkServer.active && NetworkClient.isConnected)
        {
            networkManager.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            networkManager.StopClient();
        }
        else if (NetworkServer.active)
        {
            networkManager.StopServer();
        }
    }

    /// <summary>
    /// Get current lobby info for debugging
    /// </summary>
    public string GetLobbyInfo()
    {
        if (!CurrentLobbyID.IsValid())
            return "No active lobby";

        int memberCount = SteamMatchmaking.GetNumLobbyMembers(CurrentLobbyID);
        string info = $"Lobby ID: {CurrentLobbyID}\nMembers: {memberCount}";
        
        for (int i = 0; i < memberCount; i++)
        {
            CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobbyID, i);
            string memberName = SteamFriends.GetFriendPersonaName(memberId);
            info += $"\n  - {memberName} ({memberId})";
        }

        return info;
    }

    private void OnDestroy()
    {
        LeaveLobby();
    }

    private void OnApplicationQuit()
    {
        LeaveLobby();
    }

    private void OnGUI()
    {
        // Debug info в правом верхнем углу
        if (SteamManager.Initialized)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 310, 10, 300, 200));
            GUILayout.BeginVertical("box");
            GUILayout.Label($"Steam: {SteamFriends.GetPersonaName()}");
            GUILayout.Label($"ID: {SteamUser.GetSteamID()}");
            if (CurrentLobbyID.IsValid())
            {
                GUILayout.Label($"Lobby: {CurrentLobbyID}");
                GUILayout.Label($"Players: {SteamMatchmaking.GetNumLobbyMembers(CurrentLobbyID)}");
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
#else
    private void Start()
    {
        Debug.LogWarning("[SteamLobby] Steamworks is disabled!");
    }

    public void HostLobby()
    {
        Debug.LogWarning("[SteamLobby] Steamworks is disabled!");
    }

    public void LeaveLobby()
    {
        Debug.LogWarning("[SteamLobby] Steamworks is disabled!");
    }

    public void JoinLobbyById(ulong lobbyId)
    {
        Debug.LogWarning("[SteamLobby] Steamworks is disabled!");
    }

    public void JoinLobbyById(string lobbyIdString)
    {
        Debug.LogWarning("[SteamLobby] Steamworks is disabled!");
    }
#endif
}
