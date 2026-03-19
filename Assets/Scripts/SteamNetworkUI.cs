using UnityEngine;
using UnityEngine.UI;
using Mirror;

#if !DISABLESTEAMWORKS
using Steamworks;
#endif

public class SteamNetworkUI : MonoBehaviour
{
    [Header("UI References")]
    public Button hostButton;
    public Button leaveButton;
    public Button inviteButton;
    public Button joinButton;
    public Button copyLobbyIdButton;
    
    [Header("Input Fields")]
    public InputField lobbyIdInput;

    private SteamLobby steamLobby;

    private void Awake()
    {
        Debug.Log("[SteamNetworkUI] Awake called");
    }

    private void Start()
    {
        Debug.Log("[SteamNetworkUI] Start called");
        
        steamLobby = FindObjectOfType<SteamLobby>();
        
        if (steamLobby == null)
        {
            Debug.LogError("[SteamNetworkUI] SteamLobby NOT FOUND! Make sure SteamLobby component exists in the scene.");
        }
        else
        {
            Debug.Log("[SteamNetworkUI] SteamLobby found successfully");
        }

        if (hostButton != null)
        {
            hostButton.onClick.AddListener(OnHostClicked);
            Debug.Log("[SteamNetworkUI] Host button listener added");
        }
        else
        {
            Debug.LogError("[SteamNetworkUI] Host button is NULL!");
        }

        if (leaveButton != null)
            leaveButton.onClick.AddListener(OnLeaveClicked);

        if (inviteButton != null)
            inviteButton.onClick.AddListener(OnInviteClicked);

        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinClicked);

        if (copyLobbyIdButton != null)
            copyLobbyIdButton.onClick.AddListener(CopyLobbyId);

        UpdateUI();
    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
#if !DISABLESTEAMWORKS
        if (SteamLobby.CurrentLobbyID.IsValid())
        {
            if (copyLobbyIdButton != null)
                copyLobbyIdButton.gameObject.SetActive(true);
        }
        else
        {
            if (copyLobbyIdButton != null)
                copyLobbyIdButton.gameObject.SetActive(false);
        }
#endif

        if (hostButton != null)
            hostButton.interactable = !NetworkClient.isConnected && !NetworkServer.active;

        if (leaveButton != null)
            leaveButton.interactable = NetworkClient.isConnected || NetworkServer.active;

        if (inviteButton != null)
            inviteButton.interactable = NetworkServer.active;

        if (joinButton != null)
            joinButton.interactable = !NetworkClient.isConnected && !NetworkServer.active;
    }

    private void OnHostClicked()
    {
        Debug.Log("[SteamNetworkUI] OnHostClicked called!");
        
        if (steamLobby == null)
        {
            Debug.LogError("[SteamNetworkUI] steamLobby is NULL!");
            // Попробуем найти ещё раз
            steamLobby = FindObjectOfType<SteamLobby>();
            if (steamLobby == null)
            {
                Debug.LogError("[SteamNetworkUI] Still cannot find SteamLobby!");
                return;
            }
        }

        Debug.Log("[SteamNetworkUI] Calling steamLobby.HostLobby()...");
        steamLobby.HostLobby();
        Debug.Log("[SteamNetworkUI] HostLobby() called");
    }

    private void OnLeaveClicked()
    {
        Debug.Log("[SteamNetworkUI] OnLeaveClicked called");
        if (steamLobby != null)
        {
            steamLobby.LeaveLobby();
        }
    }

    private void OnInviteClicked()
    {
        Debug.Log("[SteamNetworkUI] OnInviteClicked called");
#if !DISABLESTEAMWORKS
        if (SteamManager.Initialized && SteamLobby.CurrentLobbyID.IsValid())
        {
            SteamFriends.ActivateGameOverlayInviteDialog(SteamLobby.CurrentLobbyID);
            Debug.Log("[SteamNetworkUI] Opening Steam invite dialog...");
        }
        else
        {
            Debug.LogWarning("[SteamNetworkUI] Cannot invite - Steam not initialized or no active lobby!");
        }
#endif
    }

    private void OnJoinClicked()
    {
        Debug.Log("[SteamNetworkUI] OnJoinClicked called");
#if !DISABLESTEAMWORKS
        if (steamLobby != null && lobbyIdInput != null && !string.IsNullOrEmpty(lobbyIdInput.text))
        {
            steamLobby.JoinLobbyById(lobbyIdInput.text.Trim());
            Debug.Log($"[SteamNetworkUI] Joining lobby: {lobbyIdInput.text}");
        }
        else
        {
            Debug.LogWarning("[SteamNetworkUI] Enter Lobby ID to join!");
        }
#endif
    }

    public void CopyLobbyId()
    {
        Debug.Log("[SteamNetworkUI] CopyLobbyId called");
#if !DISABLESTEAMWORKS
        if (SteamLobby.CurrentLobbyID.IsValid())
        {
            string lobbyId = SteamLobby.CurrentLobbyID.ToString();
            GUIUtility.systemCopyBuffer = lobbyId;
            Debug.Log($"[SteamNetworkUI] Lobby ID copied: {lobbyId}");
        }
#endif
    }
}
