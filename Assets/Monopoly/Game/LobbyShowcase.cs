using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>
    /// During the lobby, every seat's token stands on a podium in the middle of the board. Picking a different
    /// token swaps the model and plays its celebration.
    /// </summary>
    public sealed class LobbyShowcase : MonoBehaviour
    {
        private const float Spacing = 1.05f;

        private ILobby lobby;
        private BoardCamera boardCamera;
        private readonly List<(int token, TokenView view, Transform podium)> slots = new List<(int, TokenView, Transform)>();
        private Material podiumMaterial;

        public static LobbyShowcase Create(Transform parent, ILobby lobby, BoardCamera boardCamera)
        {
            var showcase = new GameObject("Lobby Showcase").AddComponent<LobbyShowcase>();
            showcase.transform.SetParent(parent, false);
            showcase.transform.localPosition = new Vector3(0, BoardView.TileTop + 0.1f, 0);
            showcase.lobby = lobby;
            showcase.boardCamera = boardCamera;
            var lit = Resources.Load<Material>("Monopoly/Lit");
            showcase.podiumMaterial = new Material(lit != null ? lit : new Material(Shader.Find("Standard"))) { color = new Color(0.97f, 0.95f, 0.9f) };
            showcase.podiumMaterial.SetFloat("_Glossiness", 0.6f);
            lobby.SeatsChanged += showcase.Refresh;
            showcase.Refresh();
            return showcase;
        }

        private void OnDestroy()
        {
            if (lobby != null) lobby.SeatsChanged -= Refresh;
        }

        private void Refresh()
        {
            var seats = lobby.SeatList;
            while (slots.Count > seats.Count)
            {
                var last = slots[slots.Count - 1];
                Destroy(last.view.gameObject);
                Destroy(last.podium.gameObject);
                slots.RemoveAt(slots.Count - 1);
            }

            for (int i = 0; i < seats.Count; i++)
            {
                var seat = seats[i];
                Vector3 pos = new Vector3((i - (seats.Count - 1) / 2f) * Spacing, 0, 0);
                Color color = SeatRules.PlayerColors[i % SeatRules.PlayerColors.Length];

                if (i < slots.Count && slots[i].token == seat.Token)
                {
                    slots[i].podium.localPosition = pos;
                    slots[i].view.Place(pos + Vector3.up * 0.06f, Vector3.back);
                    continue;
                }

                if (i < slots.Count) Destroy(slots[i].view.gameObject);
                var podium = i < slots.Count ? slots[i].podium : NewPodium();
                podium.localPosition = pos;
                var view = TokenView.Create(transform, TokenCatalog.Get(seat.Token), color);
                view.transform.localScale = Vector3.one * 1.35f;
                view.Place(pos + Vector3.up * 0.06f, Vector3.back);
                view.StartCoroutine(view.React(TokenReaction.Celebrate));
                Sfx.Play(SfxKind.Pop);

                var entry = (seat.Token, view, podium);
                if (i < slots.Count) slots[i] = entry;
                else slots.Add(entry);
            }

            // Frame the line-up, shifted so it sits to the right of the lobby panel.
            float width = Mathf.Max(3f, seats.Count * Spacing);
            boardCamera.Showcase(transform.position + new Vector3(-width * 0.38f - 0.5f, 0.35f, 0), 3.6f + width * 1.05f);
        }

        private Transform NewPodium()
        {
            var podium = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            podium.name = "Podium";
            Destroy(podium.GetComponent<Collider>());
            podium.transform.SetParent(transform, false);
            podium.transform.localScale = new Vector3(0.8f, 0.06f, 0.8f);
            podium.GetComponent<Renderer>().sharedMaterial = podiumMaterial;
            return podium.transform;
        }
    }
}
