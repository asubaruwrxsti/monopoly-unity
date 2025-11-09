using UnityEngine;

public class Board : MonoBehaviour
{
    public static MonopolyTile[] tiles;

    void Awake()
    {
        tiles = FindObjectsByType<MonopolyTile>(FindObjectsSortMode.None);
        Debug.Log($"Board initialized with {tiles.Length} tiles");
    }

    public static MonopolyTile GetTileByName(string tileName)
    {
        if (tiles == null || tiles.Length == 0)
        {
            Debug.LogError("Board tiles not initialized yet!");
            return null;
        }

        foreach (var tile in tiles)
        {
            if (tile.textureResourcePath == tileName)
            {
                Debug.Log($"Found tile: {tileName}");
                return tile;
            }
        }
        
        Debug.LogWarning($"Tile '{tileName}' not found!");
        return null;
    }

    public static MonopolyTile GetTileByIndexAndSide(int index, int side)
    {
        if (tiles == null || tiles.Length == 0)
        {
            Debug.LogError("Board tiles not initialized yet!");
            return null;
        }

        foreach (var tile in tiles)
        {
            var scaleScript = tile.GetComponent<CalculateTileScale>();
            if (scaleScript != null && scaleScript.positionIndex == index && scaleScript.sideIndex == side)
            {
                return tile;
            }
        }
        return null;
    }
}