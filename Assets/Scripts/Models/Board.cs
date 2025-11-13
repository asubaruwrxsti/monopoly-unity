using System.Collections.Generic;
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

    public static List<MonopolyTile> GetPathBetweenTiles(MonopolyTile startTile, MonopolyTile endTile)
    {
        List<MonopolyTile> path = new List<MonopolyTile>();

        if (tiles == null || tiles.Length == 0)
        {
            Debug.LogError("Board tiles not initialized yet!");
            return path;
        }

        int startIndex = System.Array.IndexOf(tiles, startTile);
        int endIndex = System.Array.IndexOf(tiles, endTile);

        if (startIndex == -1 || endIndex == -1)
        {
            Debug.LogError("Start or end tile not found on the board!");
            return path;
        }

        int currentIndex = startIndex;
        while (currentIndex != endIndex)
        {
            path.Add(tiles[currentIndex]);
            currentIndex = (currentIndex + 1) % tiles.Length; // loop around the board
        }
        path.Add(tiles[endIndex]); // include the end tile

        return path;
    }
}