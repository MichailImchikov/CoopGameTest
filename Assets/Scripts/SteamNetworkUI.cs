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

    [Header("UI Panel")]
    [Tooltip("Assign the top-level UI panel that contains the SteamNetworkUI controls. If left empty the script will hide/show child GameObjects.")]
    public GameObject uiPanel;

    private SteamLobby steamLobby;

    // State to control auto-hide
    private bool isInGame = false;

    // Singleton for easy access from PlayerController
    public static SteamNetworkUI Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        Debug.Log("[SteamNetworkUI] Awake called");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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
        HandleConnectionState();
    }

    private void HandleConnectionState()
    {
        bool connected = NetworkClient.isConnected || NetworkServer.active;
#if !DISABLESTEAMWORKS
        bool inLobby = SteamLobby.CurrentLobbyID.IsValid();
#else
        bool inLobby = false;
#endif

        bool currentlyInGame = connected || inLobby;

        // Detect when player enters the game (connected or joined lobby)
        if (currentlyInGame && !isInGame)
        {
            isInGame = true;
            HidePanel();
            Debug.Log("[SteamNetworkUI] Entered game - UI hidden");
        }

        // Detect when player leaves the game (disconnected and left lobby)
        if (!currentlyInGame && isInGame)
        {
            isInGame = false;
            ShowPanel();
            Debug.Log("[SteamNetworkUI] Left game - UI shown");
        }
    }

    /// <summary>
    /// Toggle panel visibility. Called from PlayerController on Escape press.
    /// </summary>
    public void TogglePanel()
    {
        if (IsPanelActive())
        {
            HidePanel();
            Debug.Log("[SteamNetworkUI] Panel toggled - hidden");
        }
        else
        {
            ShowPanel();
            Debug.Log("[SteamNetworkUI] Panel toggled - shown");
        }
    }

    public void HidePanel()
    {
        if (uiPanel != null)
        {
            uiPanel.SetActive(false);
        }
        else
        {
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    public void ShowPanel()
    {
        if (uiPanel != null)
        {
            uiPanel.SetActive(true);
        }
        else
        {
            foreach (Transform child in transform)
            {
                child.gameObject.SetActive(true);
            }
        }
    }

    public bool IsPanelActive()
    {
        if (uiPanel != null)
            return uiPanel.activeSelf;

        // If no panel assigned, check if at least one child is active
        foreach (Transform child in transform)
        {
            if (child.gameObject.activeSelf)
                return true;
        }

        return false;
    }

    private void UpdateUI()
    {
#if !DISABLESTEAMWORKS
        // Only update copyLobbyIdButton if panel is visible
        if (IsPanelActive())
        {
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
