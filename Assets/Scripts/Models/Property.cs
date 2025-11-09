using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Models
{
    public class Property: MonoBehaviour
    {
        // property details
        [Header("Property Details")]
        [field: SerializeField] public string Name { get; set; }
        [field: SerializeField] public int Price { get; set; }
        [field: SerializeField] public Player Owner { get; set; }

        // houses and hotels
        [Header("Buildings")]
        [field: SerializeField] public int HouseCount { get; set; }
        [field: SerializeField] public int HousePrice { get; set; }
        [field: SerializeField] public bool HasHotel { get; set; }
        [field: SerializeField] public int HotelPrice { get; set; }

        // rent prices based on houses
        [Header("Rent Prices")]
        [field: SerializeField] public List<int> HouseRentPrices { get; set; }
        [field: SerializeField] public int HotelRentPrice { get; set; }

        // mortgage details
        [Header("Mortgage")]
        [field: SerializeField] public int MortgageValue { get; set; }
        [field: SerializeField] public int UnmortgageCost { get; set; }
        [field: SerializeField] public bool IsMortgaged { get; set; }

        public Property(string name, int price, int housePrice, int hotelPrice,
                       List<int> houseRentPrices, int hotelRentPrice,
                       int mortgageValue, int unmortgageCost)
        {
            Name = name;
            Price = price;
            HousePrice = housePrice;
            HotelPrice = hotelPrice;
            HouseRentPrices = houseRentPrices;
            HotelRentPrice = hotelRentPrice;
            MortgageValue = mortgageValue;
            UnmortgageCost = unmortgageCost;
            Owner = null;
            HouseCount = 0;
            HasHotel = false;
            IsMortgaged = false;
        }

        private void Awake()
        {
            // Get the MonopolyTile component on this GameObject
            MonopolyTile monopolyTile = GetComponent<MonopolyTile>();

            // Auto-populate Name from texture path if Name is empty
            if (string.IsNullOrEmpty(Name) && monopolyTile != null)
            {
                Name = monopolyTile.textureResourcePath;
            }
        }
        
        public void OnArrival(Player player)
        {
            Debug.Log($"{player.PlayerName} has arrived at {Name}");
            // check if can be bought
            if (CanBeBought())
            {
                Debug.Log($"{Name} is available for purchase at ${Price}");
                // Implement purchase logic here
            }
            else
            {
                Debug.Log($"{Name} is owned by {Owner.PlayerName}");
                // Implement rent payment logic here
            }
        }

        public bool CanBeBought()
        {
            return Owner == null;
        }
    }
}