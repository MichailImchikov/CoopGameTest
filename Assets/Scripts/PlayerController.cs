using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;

    void Update()
    {
        // “олько локальный игрок может управл€ть
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

        // Ќормализуем дл€ одинаковой скорости по диагонали
        if (input.magnitude > 1f)
            input.Normalize();

        // ƒвижение относительно направлени€ взгл€да
        Vector3 movement = transform.right * input.x + transform.forward * input.y;
        transform.position += movement * speed * Time.deltaTime;
    }
}