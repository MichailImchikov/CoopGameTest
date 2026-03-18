using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;

    [Header("Animation")]
    public Animator animator;
    
    // Кэшируем хэши параметров для производительности
    private int horizontalHash;
    private int verticalHash;
    private bool hasHorizontalParam;
    private bool hasVerticalParam;
    
    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("[PlayerController] Animator not found on this object!");
        }
        else
        {
            // Кэшируем хэши параметров
            horizontalHash = Animator.StringToHash("HorizontalMove");
            verticalHash = Animator.StringToHash("VerticalMove");
            
            // Проверяем наличие параметров в аниматоре
            hasHorizontalParam = HasParameter("HorizontalMove");
            hasVerticalParam = HasParameter("VerticalMove");
            
            if (!hasHorizontalParam)
                Debug.LogWarning("[PlayerController] Animator parameter 'HorizontalMove' not found! Check your Animator Controller.");
            if (!hasVerticalParam)
                Debug.LogWarning("[PlayerController] Animator parameter 'VerticalMove' not found! Check your Animator Controller.");
            
            // Выводим все параметры аниматора для диагностики
            Debug.Log("[PlayerController] Available Animator parameters:");
            foreach (var param in animator.parameters)
            {
                Debug.Log($"  - {param.name} ({param.type})");
            }
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

        HandleMovement();
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

        // Обновляем анимацию (NetworkAnimator синхронизирует автоматически)
        if (animator != null)
        {
            if (hasHorizontalParam)
                animator.SetFloat(horizontalHash, input.x);
            if (hasVerticalParam)
                animator.SetFloat(verticalHash, input.y);
        }
    }
}