using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class HeadLook : NetworkBehaviour
{
    [Header("Camera Settings")]
    [Tooltip("Камера игрока")]
    public Camera playerCamera;
    
    [Header("Look Settings")]
    [Tooltip("Чувствительность мыши")]
    public float mouseSensitivity = 2f;
    
    [Tooltip("Максимальный угол взгляда вверх/вниз")]
    [Range(0f, 90f)]
    public float maxVerticalAngle = 90f;
    
    [Header("References")]
    [Tooltip("Тело игрока - для горизонтального вращения")]
    public Transform playerBody;
    
    // Текущий угол вертикального вращения
    private float rotationX = 0f;
    
    // Синхронизация вращения головы по сети
    [SyncVar]
    private float syncHeadRotationX;

    void Start()
    {
        // Защита от нулевого maxVerticalAngle
        if (maxVerticalAngle <= 0f)
        {
            maxVerticalAngle = 90f;
            Debug.LogWarning("[HeadLook] maxVerticalAngle was 0 or negative, reset to 90");
        }
        
        if (isLocalPlayer)
        {
            SetupCamera();
            LockCursor();
        }
        else
        {
            // Отключаем камеру для других игроков
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(false);
            }
        }
    }

    void SetupCamera()
    {
        if (playerCamera == null)
        {
            Debug.LogError("[HeadLook] Camera is not assigned!");
            return;
        }

        playerCamera.nearClipPlane = 0.1f;
        
        // Добавляем AudioListener только если его ещё нет
        if (playerCamera.GetComponent<AudioListener>() == null)
        {
            playerCamera.gameObject.AddComponent<AudioListener>();
        }
        
        // Включаем камеру
        playerCamera.gameObject.SetActive(true);
        
        // Отключаем главную камеру сцены
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera != playerCamera)
        {
            mainCamera.gameObject.SetActive(false);
        }
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (isLocalPlayer)
        {
            HandleMouseLook();
            HandleCursorLock();
        }
        else
        {
            // Для других игроков - применяем синхронизированный поворот
            ApplySyncedRotation();
        }
    }

    void HandleMouseLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        // Получаем движение мыши
        Vector2 mouseDelta = mouse.delta.ReadValue() * mouseSensitivity * 0.1f;

        // Вертикальное вращение (взгляд вверх-вниз)
        rotationX -= mouseDelta.y;
        rotationX = Mathf.Clamp(rotationX, -maxVerticalAngle, maxVerticalAngle);

        // Применяем вертикальное вращение к камере напрямую
        if (playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        }
        
        // Также вращаем голову (для синхронизации по сети)
        transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);

        // Горизонтальное вращение (поворот всего тела)
        if (playerBody != null)
        {
            playerBody.Rotate(Vector3.up * mouseDelta.x);
        }
        else
        {
            // Если playerBody не назначен, вращаем родителя
            Transform body = transform.parent;
            if (body != null)
            {
                body.Rotate(Vector3.up * mouseDelta.x);
            }
        }

        // Синхронизация с сервером
        if (isOwned)
        {
            CmdSyncHeadRotation(rotationX);
        }
    }

    [Command]
    void CmdSyncHeadRotation(float x)
    {
        syncHeadRotationX = x;
    }

    void ApplySyncedRotation()
    {
        // Плавно применяем синхронизированный поворот для других игроков
        Quaternion targetRotation = Quaternion.Euler(syncHeadRotationX, 0f, 0f);
        transform.localRotation = Quaternion.Lerp(transform.localRotation, targetRotation, Time.deltaTime * 10f);
    }

    void HandleCursorLock()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Escape - разблокировать курсор
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Клик мышью - заблокировать курсор
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.None)
        {
            LockCursor();
        }
    }

    void OnDisable()
    {
        if (isLocalPlayer)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
