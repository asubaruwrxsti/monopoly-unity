namespace Monopoly.Models
{
    public class Dice
    {
        private static readonly System.Random random = new System.Random();

        public int Roll()
        {
            return random.Next(1, 7);
        }

        public (int, int) RollTwoDice()
        {
            return (Roll(), Roll());
        }
    }
}