using System;
using UnityEngine;

[ExecuteAlways]
public class CalculateTileScale : MonoBehaviour
{
    [Header("Board Reference")]
    public GameObject board;

    [Header("Tile Configuration")]
    [Tooltip("Number of tiles per side (excluding corners)")]
    public int tilesPerSide = 9;
    
    [Header("Tile Position")]
    [Tooltip("Which side of the board: 0=Bottom, 1=Right, 2=Top, 3=Left, -1=Corner")]
    public int sideIndex = 0;
    
    [Tooltip("Position on the side (0 to tilesPerSide-1). For corners: 0=BottomRight, 1=TopRight, 2=TopLeft, 3=BottomLeft")]
    public int positionIndex = 0;
    
    // Computed property - is this a corner tile?
    private bool isCorner => sideIndex == -1;

    [Header("Scale Multipliers")]
    [Tooltip("Scale multiplier for corner tiles relative to board")]
    public float cornerScaleMultiplier = 1.5f;
    
    [Tooltip("Scale multiplier for regular tiles relative to board")]
    public float tileScaleMultiplier = 1.0f;
    
    [Header("Tile Dimensions")]
    [Tooltip("Thickness/depth of the tile (Y axis)")]
    public float tileThickness = 0.1f;
    
    [Tooltip("Width of regular tiles perpendicular to board edge")]
    public float tileWidth = 10.0f;

    void Awake()
    {
    }

    void Start()
    {
        UpdateTileScaleAndPosition();
    }

    void Update()
    {
        // Update scale and position each frame to respond to board changes
        UpdateTileScaleAndPosition();
    }

    // This runs in Edit mode when values change in the Inspector
    void OnValidate()
    {
        if (Application.isPlaying) return;
        UpdateTileScaleAndPosition();
        
        // Notify MonopolyTile component to regenerate texture in edit mode
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += NotifyMonopolyTile;
        }
        #endif
    }

    #if UNITY_EDITOR
    private void NotifyMonopolyTile()
    {
        // Remove the callback first to prevent multiple calls
        UnityEditor.EditorApplication.delayCall -= NotifyMonopolyTile;
        
        // Check if this component still exists
        if (this == null) return;
        
        MonopolyTile tileComponent = GetComponent<MonopolyTile>();
        if (tileComponent != null && tileComponent.useDynamicTexture)
        {
            tileComponent.ForceRegenerateTexture();
            UnityEditor.EditorUtility.SetDirty(tileComponent);
        }
    }
    #endif

    void UpdateTileScaleAndPosition()
    {
        if (board == null)
        {
            Debug.LogWarning("Board reference is missing on " + gameObject.name);
            return;
        }

        float boardSize = board.transform.localScale.x;
        
        // Calculate base tile size (length along the edge)
        // Total tiles per side = tilesPerSide + 2 corners
        float totalUnitsPerSide = tilesPerSide + 2;
        float baseTileLength = boardSize / totalUnitsPerSide;

        if (isCorner)
        {
            // Corners are square tiles
            float cornerSize = baseTileLength * cornerScaleMultiplier;
            transform.localScale = new Vector3(cornerSize, tileThickness, cornerSize);
            PositionCornerTile(boardSize, cornerSize);
        }
        else
        {
            // Regular tiles are thin rectangles
            float tileLength = baseTileLength * tileScaleMultiplier;
            // Scale: length along edge, thickness (height), width perpendicular to edge
            transform.localScale = new Vector3(tileLength, tileThickness, tileWidth);
            PositionRegularTile(boardSize, tileLength, tileWidth);
        }
    }

    void PositionCornerTile(float boardSize, float cornerSize)
    {
        Vector3 boardPosition = board.transform.position;
        float halfBoard = boardSize / 2f;
        
        // Position corners so their outer edges align with board edges
        float offset = halfBoard - (cornerSize / 2f);
        
        // Calculate Y position: top of board + half tile thickness
        float boardHeight = board.transform.localScale.y;
        float yPosition = boardPosition.y + (boardHeight / 2f) + (tileThickness / 2f);

        Vector3 newPosition = boardPosition;
        newPosition.y = yPosition; // Set Y position to be on top of board

        switch (positionIndex)
        {
            case 0: // Bottom Right
                newPosition += new Vector3(offset, 0, -offset);
                break;
            case 1: // Top Right
                newPosition += new Vector3(offset, 0, offset);
                break;
            case 2: // Top Left
                newPosition += new Vector3(-offset, 0, offset);
                break;
            case 3: // Bottom Left
                newPosition += new Vector3(-offset, 0, -offset);
                break;
        }

        transform.position = newPosition;
    }

    void PositionRegularTile(float boardSize, float tileLength, float tileWidth)
    {
        Vector3 boardPosition = board.transform.position;
        float halfBoard = boardSize / 2f;
        
        // Calculate the spacing between tile centers
        float totalUnitsPerSide = tilesPerSide + 2;
        float spacing = boardSize / totalUnitsPerSide;
        
        // Start position: first tile after corner
        float startOffset = -halfBoard + spacing;
        
        // Calculate position along the edge
        float edgePosition = startOffset + (positionIndex * spacing);
        
        // Offset from board edge to center of tile (half the tile width)
        float perpOffset = halfBoard - (tileWidth / 2f);
        
        // Calculate Y position: top of board + half tile thickness
        float boardHeight = board.transform.localScale.y;
        float yPosition = boardPosition.y + (boardHeight / 2f) + (tileThickness / 2f);
        
        Vector3 newPosition = boardPosition;
        newPosition.y = yPosition; // Set Y position to be on top of board

        switch (sideIndex)
        {
            case 0: // Bottom side (tiles face inward, extending into board)
                newPosition += new Vector3(
                    edgePosition,           // Position along X axis
                    0,
                    -perpOffset             // Offset from bottom edge
                );
                // Rotate tile to face inward
                transform.rotation = Quaternion.Euler(0, 0, 0);
                break;
                
            case 1: // Right side (tiles face inward, extending into board)
                newPosition += new Vector3(
                    perpOffset,             // Offset from right edge
                    0,
                    edgePosition            // Position along Z axis
                );
                // Rotate tile to face inward
                transform.rotation = Quaternion.Euler(0, 90, 0);
                break;
                
            case 2: // Top side (tiles face inward, extending into board)
                newPosition += new Vector3(
                    halfBoard - edgePosition + (-halfBoard + spacing), // Mirror position
                    0,
                    perpOffset              // Offset from top edge
                );
                // Rotate tile to face inward
                transform.rotation = Quaternion.Euler(0, 180, 0);
                break;
                
            case 3: // Left side (tiles face inward, extending into board)
                newPosition += new Vector3(
                    -perpOffset,            // Offset from left edge
                    0,
                    halfBoard - edgePosition + (-halfBoard + spacing) // Mirror position
                );
                // Rotate tile to face inward
                transform.rotation = Quaternion.Euler(0, 270, 0);
                break;
        }

        transform.position = newPosition;
    }

    // Helper method to set up a tile programmatically
    public void SetupTile(int side, int position)
    {
        sideIndex = side;
        positionIndex = position;
        UpdateTileScaleAndPosition();
    }

    // Legacy helper method for backward compatibility
    public void SetupTile(bool corner, int side, int position)
    {
        // If it's a corner, set sideIndex to -1, otherwise use the provided side
        sideIndex = corner ? -1 : side;
        positionIndex = position;
        UpdateTileScaleAndPosition();
    }
}