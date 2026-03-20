using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    
    [Header("Jump")]
    public float jumpForce = 5f;
    public float gravity = -15f;
    public float groundCheckDistance = 0.3f;
    public LayerMask groundMask = ~0; // По умолчанию все слои

    [Header("Animation")]
    public Animator animator;
    
    // Переменные для прыжка
    private float verticalVelocity;
    private bool isGrounded;
    private CharacterController characterController;
    
    // Кэшируем хэши параметров для производительности
    private int horizontalHash;
    private int verticalHash;
    private int jumpHash;
    private bool hasHorizontalParam;
    private bool hasVerticalParam;
    private bool hasJumpParam;
    
    private void Awake()
    {
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
        
        if (animator == null)
        {
            Debug.LogError("[PlayerController] Animator not found on this object!");
        }
        else
        {
            // Кэшируем хэши параметров
            horizontalHash = Animator.StringToHash("HorizontalMove");
            verticalHash = Animator.StringToHash("VerticalMove");
            jumpHash = Animator.StringToHash("Jump");
            
            // Проверяем наличие параметров в аниматоре
            hasHorizontalParam = HasParameter("HorizontalMove");
            hasVerticalParam = HasParameter("VerticalMove");
            hasJumpParam = HasParameter("Jump");
        }
    }
    
    private bool HasParameter(string paramName)
    {
        if (animator == null) return false;
        foreach (var param in animator.parameters)
        {
            if (param.name == paramName)
                return true;
        }
        return false;
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        CheckGrounded();
        HandleMovement();
        HandleJump();
        ApplyGravity();
        HandleEscapeInput();
    }

    void CheckGrounded()
    {
        // Если есть CharacterController, используем его
        if (characterController != null)
        {
            isGrounded = characterController.isGrounded;
        }
        else
        {
            // Иначе используем Raycast
            Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
            isGrounded = Physics.Raycast(rayOrigin, Vector3.down, groundCheckDistance + 0.1f, groundMask);
        }
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
        
        if (characterController != null)
        {
            characterController.Move(movement * speed * Time.deltaTime);
        }
        else
        {
            transform.position += movement * speed * Time.deltaTime;
        }

        // Обновляем анимацию (NetworkAnimator синхронизирует автоматически)
        if (animator != null)
        {
            if (hasHorizontalParam)
                animator.SetFloat(horizontalHash, input.x);
            if (hasVerticalParam)
                animator.SetFloat(verticalHash, input.y);
        }
    }

    void HandleJump()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && isGrounded)
        {
            // Прыжок
            verticalVelocity = jumpForce;
            
            // Триггер анимации прыжка
            if (animator != null && hasJumpParam)
            {
                animator.SetTrigger(jumpHash);
            }
            
            Debug.Log("[PlayerController] Jump!");
        }
    }

    void ApplyGravity()
    {
        // Сброс вертикальной скорости при приземлении
        if (isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f; // Небольшое значение чтобы персонаж "прилипал" к земле
        }
        else
        {
            // Применяем гравитацию
            verticalVelocity += gravity * Time.deltaTime;
        }
        
        // Применяем вертикальное движение
        Vector3 verticalMovement = Vector3.up * verticalVelocity * Time.deltaTime;
        
        if (characterController != null)
        {
            characterController.Move(verticalMovement);
        }
        else
        {
            transform.position += verticalMovement;
            
            // Не даём провалиться под землю (простая проверка)
            if (isGrounded && verticalVelocity < 0)
            {
                // Корректируем позицию на землю
                RaycastHit hit;
                Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
                if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 1f, groundMask))
                {
                    transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
                }
            }
        }
    }

    void HandleEscapeInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            // Toggle SteamNetworkUI panel
            if (SteamNetworkUI.Instance != null)
            {
                SteamNetworkUI.Instance.TogglePanel();
            }
        }
    }
}