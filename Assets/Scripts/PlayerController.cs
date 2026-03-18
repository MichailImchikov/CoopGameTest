using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    
    [Header("Camera")]
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 80f;
    
    private Camera playerCamera;
    private float rotationX = 0f;

    void Start()
    {
        // Создаём камеру только для локального игрока
        if (isLocalPlayer)
        {
            SetupCamera();
            LockCursor();
        }
    }

    void SetupCamera()
    {
        // Создаём объект камеры как дочерний элемент игрока
        GameObject cameraObject = new GameObject("PlayerCamera");
        cameraObject.transform.SetParent(transform);
        
        // Позиционируем камеру на уровне головы
        cameraObject.transform.localPosition = new Vector3(0, 0.8f, 0);
        cameraObject.transform.localRotation = Quaternion.identity;
        
        // Добавляем компонент камеры
        playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.nearClipPlane = 0.1f;
        
        // Добавляем AudioListener (для звука)
        cameraObject.AddComponent<AudioListener>();
        
        // Отключаем главную камеру сцены если есть
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
        // Только локальный игрок может управлять
        if (!isLocalPlayer) return;

        HandleMouseLook();
        HandleMovement();
        HandleCursorLock();
    }

    void HandleMouseLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || playerCamera == null) return;

        // Получаем движение мыши
        Vector2 mouseDelta = mouse.delta.ReadValue() * mouseSensitivity * 0.1f;

        // Вращение по вертикали (камера смотрит вверх/вниз)
        rotationX -= mouseDelta.y;
        rotationX = Mathf.Clamp(rotationX, -maxLookAngle, maxLookAngle);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);

        // Вращение по горизонтали (игрок поворачивается)
        transform.Rotate(Vector3.up * mouseDelta.x);
    }

    void HandleMovement()
    {
        Vector2 input = Vector2.zero;
        
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1;
        }

        // Нормализуем для одинаковой скорости по диагонали
        if (input.magnitude > 1f)
            input.Normalize();

        // Движение относительно направления взгляда
        Vector3 movement = transform.right * input.x + transform.forward * input.y;
        transform.position += movement * speed * Time.deltaTime;
    }

    void HandleCursorLock()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Нажми Escape чтобы разблокировать курсор
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Клик мышью чтобы заблокировать курсор снова
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.None)
        {
            LockCursor();
        }
    }

    // Визуально отличаем локального игрока
    public override void OnStartLocalPlayer()
    {
        GetComponent<Renderer>().material.color = Color.blue;
    }

    // Очистка при отключении
    void OnDisable()
    {
        if (isLocalPlayer)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}