using UnityEngine;
using UnityEngine.UI;

namespace Monopoly.UI
{
    public class PurchasePopup : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text messageText;
        [SerializeField] private Button buyButton;
        [SerializeField] private Button cancelButton;

        private System.Action onBuy;
        private System.Action onCancel;

        public void Setup(string title, string message, System.Action onBuy, System.Action onCancel)
        {
            titleText.text = title;
            messageText.text = message;
            this.onBuy = onBuy;
            this.onCancel = onCancel;

            buyButton.onClick.RemoveAllListeners();
            cancelButton.onClick.RemoveAllListeners();

            buyButton.onClick.AddListener(() =>
            {
                onBuy?.Invoke();
                Destroy(gameObject);
            });

            cancelButton.onClick.AddListener(() =>
            {
                onCancel?.Invoke();
                Destroy(gameObject);
            });
        }
    }
}