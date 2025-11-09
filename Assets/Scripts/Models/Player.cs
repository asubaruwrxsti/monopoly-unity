using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Models
{
    public class Player : MonoBehaviour
    {
        public string PlayerName { get; set; }
        public int Balance { get; set; }
        public GameObject Token { get; set; }
        public List<Property> OwnedProperties { get; private set; }
        private MonopolyTile currentTile;

        private void Awake()
        {
            OwnedProperties = new List<Property>();
        }

        public void Initialize(string playerName, int startingBalance, GameObject token)
        {
            PlayerName = playerName;
            Balance = startingBalance;
            Token = token;

            Debug.Log($"Initializing player: {playerName}");
            Debug.Log($"Token is null: {Token == null}");
            Debug.Log($"About to get tile...");
            
            try
            {
                MonopolyTile tile = Board.GetTileByName("Old Kent Road");
                
                Debug.Log($"Tile found: {tile != null}");
                
                if (tile != null)
                {
                    Debug.Log($"Found tile: Old Kent Road at position {tile.transform.position}");
                    MoveToken(tile);
                }
                else
                {
                    Debug.LogError("Could not find 'Old Kent Road' tile!");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error getting tile: {e.Message}\n{e.StackTrace}");
            }
        }

        public void MoveToken(MonopolyTile targetTile)
        {
            if (targetTile == null)
            {
                Debug.LogError("Target tile is null!");
                return;
            }

            Vector3 newPosition = targetTile.GetTileCoordinates();
            Debug.Log($"Moving token to position: {newPosition}");

            if (Token != null)
            {
                Token.transform.position = newPosition;
                currentTile = targetTile;
                Debug.Log($"Token moved successfully to {newPosition}");
            }
            else
            {
                Debug.LogError("Token is null!");
            }
            
            if (targetTile.TryGetComponent<Property>(out var property))
            {
                property.OnArrival(this);
            }
        }

        public void BuyProperty(Property property)
        {
            if (Balance >= property.Price && property.CanBeBought())
            {
                Balance -= property.Price;
                property.Owner = this;
                OwnedProperties.Add(property);
            }
        }
    }
}