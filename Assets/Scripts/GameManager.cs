using UnityEngine;
using Monopoly.Models;

public class GameManager : MonoBehaviour
{
    public GameObject playerToken;
    
    void Start()
    {
        Debug.Log("GameManager Start called");
        
        // Create player GameObject
        GameObject playerObj = new GameObject("Player1");
        Player player = playerObj.AddComponent<Player>();
        
        Debug.Log($"Player created: {player != null}");
        Debug.Log($"Token assigned: {playerToken != null}");

        if (player != null && playerToken != null)
        {
            Debug.Log("About to initialize player...");
            player.Initialize("Player 1", 1500, playerToken);
        }
        else
        {
            Debug.LogError($"Player or Token not assigned! Player: {player != null}, Token: {playerToken != null}");
        }
    }
}