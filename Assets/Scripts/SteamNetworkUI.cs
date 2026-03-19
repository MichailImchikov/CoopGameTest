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

    private void Start()
    {
        steamLobby = FindObjectOfType<SteamLobby>();

        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostClicked);

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
        if (steamLobby != null)
        {
            steamLobby.HostLobby();
            Debug.Log("[UI] Host button clicked - Creating Steam Lobby...");
        }
    }

    private void OnLeaveClicked()
    {
        if (steamLobby != null)
        {
            steamLobby.LeaveLobby();
            Debug.Log("[UI] Leave button clicked");
        }
    }

    private void OnInviteClicked()
    {
#if !DISABLESTEAMWORKS
        if (SteamManager.Initialized && SteamLobby.CurrentLobbyID.IsValid())
        {
            SteamFriends.ActivateGameOverlayInviteDialog(SteamLobby.CurrentLobbyID);
            Debug.Log("[UI] Opening Steam invite dialog...");
        }
        else
        {
            Debug.LogWarning("[UI] Cannot invite - no active lobby!");
        }
#endif
    }

    private void OnJoinClicked()
    {
#if !DISABLESTEAMWORKS
        if (steamLobby != null && lobbyIdInput != null && !string.IsNullOrEmpty(lobbyIdInput.text))
        {
            steamLobby.JoinLobbyById(lobbyIdInput.text.Trim());
            Debug.Log($"[UI] Joining lobby: {lobbyIdInput.text}");
        }
        else
        {
            Debug.LogWarning("[UI] Enter Lobby ID to join!");
        }
#endif
    }

    public void CopyLobbyId()
    {
#if !DISABLESTEAMWORKS
        if (SteamLobby.CurrentLobbyID.IsValid())
        {
            string lobbyId = SteamLobby.CurrentLobbyID.ToString();
            GUIUtility.systemCopyBuffer = lobbyId;
            Debug.Log($"[UI] Lobby ID copied: {lobbyId}");
        }
#endif
    }
}
