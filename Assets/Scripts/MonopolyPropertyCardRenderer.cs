using UnityEngine;

/// <summary>
/// Loads and rotates property card textures based on tile position.
/// Handles texture rotation to match board layout (bottom, left, top, right sides).
/// </summary>
public class MonopolyPropertyCardRenderer
{
    private readonly bool enableDebugLogs;
    
    public MonopolyPropertyCardRenderer(bool enableDebugLogs = false)
    {
        this.enableDebugLogs = enableDebugLogs;
    }
    
    /// <summary>
    /// Load a property card texture from Resources and apply rotation based on tile side.
    /// Side -1 (corner): Rotation based on position (0=bottom-right, 10=top-right, 20=top-left, 30=bottom-left)
    /// Sides 0-3: 180° rotation for all sides (tile GameObject rotates, texture stays consistent)
    /// </summary>
    /// <param name="texturePath">Path to texture in Resources folder (without extension)</param>
    /// <param name="side">Which side of the board (-1=corner, 0=bottom, 1=left, 2=top, 3=right)</param>
    /// <param name="position">Position along the board (0-40, used for corner rotation)</param>
    public Texture2D LoadPropertyCard(string texturePath, int side, int position = 0)
    {
        bool isCorner = side == -1;
        LogDebug($"Loading {(isCorner ? "corner" : "property")} card from '{texturePath}' for side {side}, position {position}");
        
        Texture2D baseTexture = Resources.Load<Texture2D>(texturePath);
        
        if (baseTexture == null)
        {
            Debug.LogError($"Failed to load texture from Resources/{texturePath}");
            return CreateFallbackTexture(isCorner ? 768 : 512, isCorner ? 768 : 768);
        }
        
        // Corners need rotation based on position
        if (isCorner)
        {
            Texture2D rotatedCorner = ApplyCornerRotation(baseTexture, position);
            LogDebug($"Corner tile loaded with rotation for position {position}");
            return rotatedCorner;
        }
        
        Texture2D rotatedTexture = ApplyRotationForSide(baseTexture, side);
        
        LogDebug($"Property card loaded and rotated successfully");
        return rotatedTexture;
    }
    
    private Texture2D ApplyCornerRotation(Texture2D original, int position)
    {
        // Corner positions: 0 (GO/bottom-right), 10 (Just Visiting/top-right), 
        // 20 (Free Parking/top-left), 30 (Go To Jail/bottom-left)
        return position switch
        {
            0 => RotateTexture180(original),   // GO - bottom-right corner
            10 => RotateTexture270(original),  // Just Visiting - top-right corner  
            20 => CopyTexture(original),       // Free Parking - top-left corner
            30 => RotateTexture90(original),   // Go To Jail - bottom-left corner
            _ => CopyTexture(original)
        };
    }
    
    private Texture2D ApplyRotationForSide(Texture2D original, int side)
    {
        // Since the tile GameObject itself rotates (Y-axis rotation in CalculateTileScale),
        // we apply the same texture rotation for all sides so text always faces inward
        return side switch
        {
            0 => RotateTexture180(original),   // Bottom - 180° (color bar faces center)
            1 => CopyTexture(original),
            2 => RotateTexture180(original),   // Top - 180° (tile rotates 180° Y)
            3 => CopyTexture(original),
            _ => CopyTexture(original)         // Corner - no rotation
        };
    }
    
    private Texture2D CopyTexture(Texture2D original)
    {
        Texture2D copy = new Texture2D(original.width, original.height, original.format, false);
        copy.SetPixels(original.GetPixels());
        copy.Apply();
        return copy;
    }
    
    private Texture2D RotateTexture90(Texture2D original)
    {
        int width = original.width;
        int height = original.height;
        Texture2D rotated = new Texture2D(height, width);
        
        Color[] pixels = original.GetPixels();
        Color[] rotatedPixels = new Color[pixels.Length];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                rotatedPixels[(height - 1 - y) + x * height] = pixels[x + y * width];
            }
        }
        
        rotated.SetPixels(rotatedPixels);
        rotated.Apply();
        return rotated;
    }
    
    private Texture2D RotateTexture180(Texture2D original)
    {
        int width = original.width;
        int height = original.height;
        Texture2D rotated = new Texture2D(width, height);
        
        Color[] pixels = original.GetPixels();
        Color[] rotatedPixels = new Color[pixels.Length];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                rotatedPixels[(width - 1 - x) + (height - 1 - y) * width] = pixels[x + y * width];
            }
        }
        
        rotated.SetPixels(rotatedPixels);
        rotated.Apply();
        return rotated;
    }
    
    private Texture2D RotateTexture270(Texture2D original)
    {
        int width = original.width;
        int height = original.height;
        Texture2D rotated = new Texture2D(height, width);
        
        Color[] pixels = original.GetPixels();
        Color[] rotatedPixels = new Color[pixels.Length];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                rotatedPixels[y + (width - 1 - x) * height] = pixels[x + y * width];
            }
        }
        
        rotated.SetPixels(rotatedPixels);
        rotated.Apply();
        return rotated;
    }
    
    private Texture2D CreateFallbackTexture(int width = 512, int height = 768)
    {
        Texture2D fallback = new Texture2D(width, height);
        Color[] pixels = new Color[width * height];
        
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(1f, 0f, 1f, 1f);
        }
        
        fallback.SetPixels(pixels);
        fallback.Apply();
        return fallback;
    }
    
    private void LogDebug(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[PropertyCardRenderer] {message}");
        }
    }
}
