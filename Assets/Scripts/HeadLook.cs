using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class HeadLook : NetworkBehaviour
{
    [Header("Camera Settings")]
    [Tooltip("Камера игрока - перетащите сюда камеру")]
    public Camera playerCamera;
    
    [Header("Look Settings")]
    [Tooltip("Чувствительность мыши")]
    public float mouseSensitivity = 2f;
    
    [Tooltip("Максимальный угол поворота вверх/вниз")]
    public float maxVerticalAngle = 90f;
    
    [Tooltip("Максимальный угол поворота головы влево/вправо")]
    public float maxHorizontalAngle = 90f;
    
    [Header("References")]
    [Tooltip("Тело игрока (родительский объект) - для поворота всего персонажа")]
    public Transform playerBody;
    
    // Текущие углы поворота
    private float rotationX = 0f; // Вертикальный (вверх/вниз)
    private float rotationY = 0f; // Горизонтальный (влево/вправо) - локальный для головы
    
    // Синхронизация поворота головы по сети
    [SyncVar]
    private float syncHeadRotationX;
    [SyncVar]
    private float syncHeadRotationY;

    void Start()
    {
        if (isLocalPlayer)
        {
            SetupCamera();
            LockCursor();
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
            // Для других игроков - применяем синхронизированные значения
            ApplySyncedRotation();
        }
    }

    void HandleMouseLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || playerCamera == null) return;

        // Получаем движение мыши
        Vector2 mouseDelta = mouse.delta.ReadValue() * mouseSensitivity * 0.1f;

        // Вертикальный поворот (камера и голова смотрят вверх/вниз)
        rotationX -= mouseDelta.y;
        rotationX = Mathf.Clamp(rotationX, -maxVerticalAngle, maxVerticalAngle);

        // Горизонтальный поворот головы (локальный)
        rotationY += mouseDelta.x;
        
        // Если голова повернулась больше чем на maxHorizontalAngle - поворачиваем тело
        if (Mathf.Abs(rotationY) > maxHorizontalAngle)
        {
            // Вычисляем на сколько превысили лимит
            float excess = rotationY - Mathf.Sign(rotationY) * maxHorizontalAngle;
            
            // Поворачиваем тело на эту величину
            if (playerBody != null)
            {
                playerBody.Rotate(Vector3.up * excess);
            }
            
            // Ограничиваем поворот головы
            rotationY = Mathf.Sign(rotationY) * maxHorizontalAngle;
        }

        // Применяем поворот к голове (этот объект)
        transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0);
        
        // Камера следует за головой (можно добавить дополнительный offset)
        playerCamera.transform.localRotation = Quaternion.identity;

        // Синхронизируем с сервером
        if (isOwned)
        {
            CmdSyncHeadRotation(rotationX, rotationY);
        }
    }

    [Command]
    void CmdSyncHeadRotation(float x, float y)
    {
        syncHeadRotationX = x;
        syncHeadRotationY = y;
    }

    void ApplySyncedRotation()
    {
        // Плавно применяем синхронизированный поворот для других игроков
        Quaternion targetRotation = Quaternion.Euler(syncHeadRotationX, syncHeadRotationY, 0);
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
