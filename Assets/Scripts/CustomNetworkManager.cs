using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class CustomNetworkManager : NetworkManager
{
    [Header("Player Prefabs")]
    [Tooltip("Список всех доступных персонажей. Первый игрок получит Element 0, второй - Element 1 и т.д.")]
    public GameObject[] characterPrefabs;

    [Header("Spawn Settings")]
    public Vector3 spawnPosition = new Vector3(0, 1, 0);
    public float spawnRadius = 3f;

    // Счётчик для чередования персонажей
    private int nextCharacterIndex = 0;

    public override void Awake()
    {
        // Регистрируем все characterPrefabs ДО base.Awake()
        RegisterCharacterPrefabs();
        
        // Автоматически устанавливаем playerPrefab если он пустой
        if (playerPrefab == null && characterPrefabs != null && characterPrefabs.Length > 0)
        {
            playerPrefab = characterPrefabs[0];
            Debug.Log($"[CustomNetworkManager] Auto-assigned playerPrefab: {playerPrefab.name}");
        }
        
        base.Awake();
    }

    private void RegisterCharacterPrefabs()
    {
        if (characterPrefabs == null) return;
        
        foreach (GameObject prefab in characterPrefabs)
        {
            if (prefab != null && !spawnPrefabs.Contains(prefab))
            {
                spawnPrefabs.Add(prefab);
                Debug.Log($"[CustomNetworkManager] Registered spawn prefab: {prefab.name}");
            }
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // Определяем какой префаб использовать
        GameObject prefabToSpawn;
        
        if (characterPrefabs != null && characterPrefabs.Length > 0)
        {
            // Берём следующий персонаж по очереди
            prefabToSpawn = characterPrefabs[nextCharacterIndex];
            nextCharacterIndex = (nextCharacterIndex + 1) % characterPrefabs.Length;
        }
        else
        {
            // Если массив пустой, используем стандартный playerPrefab
            prefabToSpawn = playerPrefab;
        }

        // Получаем позицию спавна
        Vector3 position = GetSpawnPos();

        // Создаём игрока
        GameObject player = Instantiate(prefabToSpawn, position, Quaternion.identity);

        // Регистрируем игрока для этого соединения
        NetworkServer.AddPlayerForConnection(conn, player);

        Debug.Log($"Spawned character {prefabToSpawn.name} for connection {conn.connectionId} at {position}");
    }

    private Vector3 GetSpawnPos()
    {
        // Если есть точки спавна в сцене — используем их
        if (startPositions.Count > 0)
        {
            Transform startPos = GetStartPosition();
            if (startPos != null)
                return startPos.position;
        }

        // Иначе спавним вокруг заданной позиции
        Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
        return  new Vector3(randomOffset.x, 0, randomOffset.y);
    }

    // Сброс счётчика при остановке сервера
    public override void OnStopServer()
    {
        base.OnStopServer();
        nextCharacterIndex = 0;
    }
}
