using System.Numerics;
using UnityEngine;

/// <summary>
/// Simplified MonoBehaviour for Monopoly tiles.
/// Only handles: scale calculation, position, side index, and texture rotation.
/// </summary>
public class MonopolyTile : MonoBehaviour
{
    [Header("Visual References")]
    [Tooltip("Renderer for the tile (usually MeshRenderer)")]
    public Renderer tileRenderer;
    
    [Tooltip("Texture to apply to the tile surface")]
    public Texture2D tileTexture;
    
    [Tooltip("Material index to apply texture to (usually 0)")]
    public int textureMaterialIndex = 0;

    [Header("Texture Path")]
    [Tooltip("Path to texture in Resources folder (without extension, e.g., 'PropertyCards/Boardwalk')")]
    [field: SerializeField]
    public string textureResourcePath { get; set; } = "";
    
    [Header("Debug Settings")]
    [Tooltip("Enable detailed debug logging")]
    public bool enableDebugLogs = false;
    
    private CalculateTileScale tileScaleScript;
    private MonopolyPropertyCardRenderer cardRenderer;
    private int lastSideIndex = -999;
    private string lastTextureResourcePath = "";
    internal bool useDynamicTexture;

    void Start()
    {
        InitializeComponents();
        LoadAndApplyTexture();
    }

    void OnValidate()
    {
        // Get the CalculateTileScale component if it exists
        if (tileScaleScript == null)
        {
            tileScaleScript = GetComponent<CalculateTileScale>();
        }

        // Check if side index or texture path changed
        int currentSideIndex = tileScaleScript != null ? tileScaleScript.sideIndex : 0;
        bool sideChanged = lastSideIndex != currentSideIndex;
        bool pathChanged = lastTextureResourcePath != textureResourcePath;
        
        // Always reload if path is not empty and something changed
        if ((sideChanged || pathChanged) && !string.IsNullOrEmpty(textureResourcePath))
        {
            LogDebug($"OnValidate triggered - Side: {currentSideIndex}, Path: {textureResourcePath}, SideChanged: {sideChanged}, PathChanged: {pathChanged}");
            
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && !string.IsNullOrEmpty(textureResourcePath))
                    {
                        LoadAndApplyTexture();
                        lastSideIndex = currentSideIndex;
                        lastTextureResourcePath = textureResourcePath;
                    }
                };
            }
            else
#endif
            {
                LoadAndApplyTexture();
                lastSideIndex = currentSideIndex;
                lastTextureResourcePath = textureResourcePath;
            }
        }
        
        // Update tracking even if we didn't reload
        if (!string.IsNullOrEmpty(textureResourcePath))
        {
            lastSideIndex = currentSideIndex;
            lastTextureResourcePath = textureResourcePath;
        }
    }

    private void InitializeComponents()
    {
        tileScaleScript = GetComponent<CalculateTileScale>();
        cardRenderer = new MonopolyPropertyCardRenderer(enableDebugLogs);
        
        if (tileScaleScript != null)
        {
            lastSideIndex = tileScaleScript.sideIndex;
        }
        
        lastTextureResourcePath = textureResourcePath;
    }

    /// <summary>
    /// Load texture from Resources and apply rotation based on side index
    /// </summary>
    private void LoadAndApplyTexture()
    {
        if (string.IsNullOrEmpty(textureResourcePath))
        {
            LogDebug("No texture path specified");
            return;
        }

        if (cardRenderer == null)
        {
            cardRenderer = new MonopolyPropertyCardRenderer(enableDebugLogs);
        }

        int sideIndex = tileScaleScript != null ? tileScaleScript.sideIndex : 0;
        int position = tileScaleScript != null ? tileScaleScript.positionIndex : 0;
        
        LogDebug($"Loading texture from '{textureResourcePath}' for side {sideIndex}, position {position}");
        
        tileTexture = cardRenderer.LoadPropertyCard(textureResourcePath, sideIndex, position);
        
        if (tileTexture != null)
        {
            ApplyTexture();
            LogDebug("Texture loaded and applied successfully");
        }
        else
        {
            Debug.LogError($"Failed to load texture from Resources/{textureResourcePath}");
        }
    }

    /// <summary>
    /// Apply the texture to the tile's renderer
    /// </summary>
    private void ApplyTexture()
    {
        if (tileRenderer == null || tileTexture == null) return;

#if UNITY_EDITOR
        // Use sharedMaterials in edit mode to avoid material leaks
        if (!Application.isPlaying)
        {
            Material[] materials = tileRenderer.sharedMaterials;
            if (materials.Length > textureMaterialIndex)
            {
                materials[textureMaterialIndex].mainTexture = tileTexture;
                tileRenderer.sharedMaterials = materials;
            }
        }
        else
#endif
        {
            Material[] materials = tileRenderer.materials;
            if (materials.Length > textureMaterialIndex)
            {
                materials[textureMaterialIndex].mainTexture = tileTexture;
                tileRenderer.materials = materials;
            }
        }
    }

    /// <summary>
    /// Force regenerate the texture (called from CalculateTileScale or inspector)
    /// </summary>
    public void ForceRegenerateTexture()
    {
        LoadAndApplyTexture();
        
        if (tileScaleScript != null)
        {
            lastSideIndex = tileScaleScript.sideIndex;
        }
    }

    /// <summary>
    /// Manually reload and apply texture (useful for debugging)
    /// </summary>
    [ContextMenu("Reload Texture")]
    public void ReloadTexture()
    {
        LogDebug($"=== RELOAD TEXTURE CALLED ===");
        LogDebug($"Texture Path: '{textureResourcePath}'");
        LogDebug($"Tile Renderer: {(tileRenderer != null ? "Assigned" : "NULL")}");
        
        if (tileScaleScript == null)
        {
            tileScaleScript = GetComponent<CalculateTileScale>();
            LogDebug($"CalculateTileScale: {(tileScaleScript != null ? "Found" : "NULL")}");
        }
        
        int sideIndex = tileScaleScript != null ? tileScaleScript.sideIndex : 0;
        LogDebug($"Side Index: {sideIndex}");
        
        LoadAndApplyTexture();
        LogDebug("=== RELOAD COMPLETE ===");
    }

    private void LogDebug(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[MonopolyTile] {message}");
        }
    }

    public UnityEngine.Vector3 GetTileCoordinates()
    {
        return transform.position;
    }

    public static MonopolyTile GetTileByCoordinates(UnityEngine.Vector3 coordinates)
    {
        Board board = FindFirstObjectByType<Board>();
        if (board == null)
        {
            Debug.LogWarning("Board not found in the scene.");
            return null;
        }

        foreach (var tile in Board.tiles)
        {
            if (tile.GetTileCoordinates() == coordinates)
            {
                return tile;
            }
        }

        Debug.LogWarning($"No tile found at coordinates: {coordinates}");
        return null;
    }
}
