using System.Collections.Generic;
using System.Linq;

namespace Monopoly.Core
{
    /// <summary>
    /// A proposed exchange between the current player (<see cref="From"/>) and another player (<see cref="To"/>).
    /// Properties with buildings anywhere in their colour group can't be traded; sell the buildings first.
    /// </summary>
    public sealed class TradeOffer
    {
        public int From { get; }
        public int To { get; }
        public IReadOnlyList<int> GiveProperties { get; }
        public IReadOnlyList<int> GetProperties { get; }
        public int GiveCash { get; }
        public int GetCash { get; }

        public TradeOffer(int from, int to, IEnumerable<int> giveProperties, IEnumerable<int> getProperties, int giveCash, int getCash)
        {
            From = from;
            To = to;
            GiveProperties = giveProperties.Distinct().OrderBy(i => i).ToList();
            GetProperties = getProperties.Distinct().OrderBy(i => i).ToList();
            GiveCash = giveCash;
            GetCash = getCash;
        }

        public bool IsEmpty => GiveProperties.Count == 0 && GetProperties.Count == 0 && GiveCash == 0 && GetCash == 0;
    }
}
