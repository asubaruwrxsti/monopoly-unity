using UnityEngine;

namespace Monopoly.UI
{
    public class PopupManager : MonoBehaviour
    {
        public static PopupManager Instance { get; private set; }

        [SerializeField] private GameObject purchasePopupPrefab; // assign prefab in inspector
        private GameObject currentPopup;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void ShowPurchasePopup(string title, string message, System.Action onBuy, System.Action onCancel = null)
        {
            if (currentPopup != null) Destroy(currentPopup);
            if (purchasePopupPrefab == null) { Debug.LogError("Purchase popup prefab not assigned."); return; }

            currentPopup = Instantiate(purchasePopupPrefab, transform);
            currentPopup.SetActive(true);

            var popup = currentPopup.GetComponent<PurchasePopup>();
            if (popup != null)
            {
                popup.Setup(title, message, onBuy, () =>
                {
                    onCancel?.Invoke();
                    CloseCurrent();
                });
            }
        }

        public void CloseCurrent()
        {
            if (currentPopup != null) { Destroy(currentPopup); currentPopup = null; }
        }
    }
}