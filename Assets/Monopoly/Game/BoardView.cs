using System.Collections;
using System.Collections.Generic;
using Monopoly.Core;
using TMPro;
using UnityEngine;

namespace Monopoly.Game
{
    /// <summary>
    /// Builds the 3D board procedurally from the tile textures in Resources/Tiles, and keeps ownership
    /// plaques and buildings in sync with the game state. Board space: X right, Z away from the camera,
    /// GO in the bottom-right corner, play running clockwise.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float TileWidth = 1f;
        public const float TileDepth = 1.5f;
        public const float Rim = 0.45f;
        public const float HalfBoard = (2 * TileDepth + 9 * TileWidth) / 2f;
        /// <summary>Height of the tile surfaces that tokens stand on.</summary>
        public const float TileTop = 0.07f;
        private const float Gap = 0.035f;

        private static readonly Color BoardColor = new Color32(196, 222, 200, 255);
        private static readonly Color FrameColor = new Color32(24, 58, 50, 255);
        private static readonly Color SlabColor = new Color32(244, 241, 230, 255);
        private static readonly Color HouseColor = new Color32(52, 178, 82, 255);
        private static readonly Color HotelColor = new Color32(222, 54, 58, 255);

        private sealed class TileVisual
        {
            public Transform Root;
            public Transform OwnerPlaque;
            public Renderer OwnerRenderer;
            public Transform Buildings;
            public int ShownOwner = int.MinValue;
            public int ShownHouses = -1;
            public bool ShownMortgaged;
            public Coroutine Bounce;
        }

        private readonly TileVisual[] tiles = new TileVisual[BoardLayout.SpaceCount];
        private readonly Dictionary<Collider, int> tileByCollider = new Dictionary<Collider, int>();
        private Material litTemplate;
        private Material unlitTemplate;
        private Mesh roofMesh;

        public static BoardView Create(Transform parent)
        {
            var view = new GameObject("Board").AddComponent<BoardView>();
            view.transform.SetParent(parent, false);
            view.Build();
            return view;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>Yaw that points a tile's texture "up" (its colour band) toward the board centre.</summary>
        public static float TileYaw(int index)
        {
            switch (index)
            {
                case 0: return 0f;                       // GO
                case BoardLayout.JailIndex: return 180f; // jail cell faces the inner corner
                case 20: return 180f;                    // Free Parking
                case BoardLayout.GoToJailIndex: return 0f;
                default: return (index / 10) * 90f;
            }
        }

        public static Vector3 TileCenter(int index)
        {
            float edge = HalfBoard - TileDepth / 2f;            // centre line of the outer ring
            float along = HalfBoard - TileDepth - TileWidth / 2f; // first regular tile after a corner
            int side = index / 10;
            int offset = index % 10;

            if (offset == 0)
            {
                switch (side)
                {
                    case 0: return new Vector3(edge, 0, -edge);
                    case 1: return new Vector3(-edge, 0, -edge);
                    case 2: return new Vector3(-edge, 0, edge);
                    default: return new Vector3(edge, 0, edge);
                }
            }

            float t = along - (offset - 1) * TileWidth;
            switch (side)
            {
                case 0: return new Vector3(t, 0, -edge);  // bottom row, right to left
                case 1: return new Vector3(-edge, 0, -t); // left column, bottom to top
                case 2: return new Vector3(-t, 0, edge);  // top row, left to right
                default: return new Vector3(edge, 0, t);  // right column, top to bottom
            }
        }

        public static Vector2 TileSize(int index)
            => index % 10 == 0 ? new Vector2(TileDepth, TileDepth) : new Vector2(TileWidth, TileDepth);

        public static Quaternion SideRotation(int index) => Quaternion.Euler(0, (index / 10) * 90f, 0);

        /// <summary>Resting spot for a player's token, so several tokens can share a space.</summary>
        public static Vector3 TokenSlot(int space, int playerIndex, bool inJail)
        {
            Vector3 c = TileCenter(space) + Vector3.up * TileTop;
            int slot = playerIndex % SeatRules.MaxSeats;

            if (space == BoardLayout.JailIndex)
            {
                // The jail cell is the inner part of the corner; visitors stand on the outer strip.
                Vector2[] cell = { new Vector2(0.05f, 0.05f), new Vector2(0.42f, 0.05f), new Vector2(0.05f, 0.42f),
                                   new Vector2(0.42f, 0.42f), new Vector2(0.6f, 0.22f), new Vector2(0.22f, 0.6f) };
                Vector2[] visit = { new Vector2(-0.12f, -0.56f), new Vector2(0.24f, -0.56f), new Vector2(0.6f, -0.56f),
                                    new Vector2(-0.56f, -0.12f), new Vector2(-0.56f, 0.24f), new Vector2(-0.56f, 0.6f) };
                Vector2 o = inJail ? cell[slot] : visit[slot];
                return c + new Vector3(o.x, 0, o.y);
            }

            Quaternion yaw = SideRotation(space);
            Vector3 right = yaw * Vector3.right;
            Vector3 up = yaw * Vector3.forward;
            if (space % 10 == 0)
            {
                float[] xs = { -0.44f, 0f, 0.44f };
                float[] ys = { -0.3f, 0.3f };
                return c + right * xs[slot % 3] + up * ys[slot / 3];
            }
            float[] cols = { -0.22f, 0.22f };
            float[] rows = { 0.12f, -0.22f, -0.54f };
            return c + right * cols[slot % 2] + up * rows[slot / 2];
        }

        public bool TryGetTile(Collider collider, out int index) => tileByCollider.TryGetValue(collider, out index);

        // ---------------------------------------------------------------- construction

        private void Build()
        {
            var lit = Resources.Load<Material>("Monopoly/Lit");
            var unlit = Resources.Load<Material>("Monopoly/Unlit");
            litTemplate = lit != null ? lit : new Material(Shader.Find("Standard"));
            unlitTemplate = unlit != null ? unlit : new Material(Shader.Find("Unlit/Texture"));
            roofMesh = MeshKit.Extrude(new[] { new Vector2(-0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0.5f) }, 1f);

            float boardSize = HalfBoard * 2 + Rim * 2;
            Box("Frame", new Vector3(0, -0.36f, 0), new Vector3(boardSize + 0.6f, 0.7f, boardSize + 0.6f), FrameColor, gloss: 0.5f);
            Box("Trim", new Vector3(0, -0.04f, 0), new Vector3(boardSize + 0.12f, 0.06f, boardSize + 0.12f), new Color32(232, 196, 92, 255), gloss: 0.8f, metallic: 0.6f);
            Box("Board", new Vector3(0, -0.02f, 0), new Vector3(boardSize, 0.06f, boardSize), BoardColor);

            for (int i = 0; i < BoardLayout.SpaceCount; i++) BuildTile(i);
            BuildCentre();
        }

        private void BuildTile(int index)
        {
            var def = BoardLayout.Spaces[index];
            var tile = new TileVisual();
            tiles[index] = tile;

            var root = new GameObject($"{index:00} {def.Name}").transform;
            root.SetParent(transform, false);
            root.localPosition = TileCenter(index);
            root.localRotation = Quaternion.Euler(0, TileYaw(index), 0);
            tile.Root = root;

            Vector2 size = TileSize(index) - new Vector2(Gap, Gap);
            Box("Slab", new Vector3(0, TileTop / 2f, 0), new Vector3(size.x, TileTop, size.y), SlabColor, root, gloss: 0.3f);

            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            face.transform.SetParent(root, false);
            face.transform.localPosition = new Vector3(0, TileTop + 0.001f, 0);
            face.transform.localRotation = Quaternion.Euler(90, 0, 0);
            face.transform.localScale = new Vector3(size.x, size.y, 1);
            var mat = new Material(unlitTemplate) { mainTexture = Resources.Load<Texture2D>("Tiles/" + def.Texture) };
            if (mat.mainTexture == null) Debug.LogWarning($"Missing tile texture Resources/Tiles/{def.Texture}");
            var faceRenderer = face.GetComponent<Renderer>();
            faceRenderer.sharedMaterial = mat;
            faceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tileByCollider[face.GetComponent<Collider>()] = index;

            if (!def.IsOwnable) return;

            // Owner plaque on the rim, just outside the tile's outer edge, oriented with the board side.
            var side = SideRotation(index);
            var plaque = Box("Owner", Vector3.zero, new Vector3(TileWidth * 0.82f, 0.08f, Rim * 0.62f), Color.white, transform, gloss: 0.7f);
            plaque.transform.localPosition = TileCenter(index) - side * Vector3.forward * (TileDepth / 2f + Rim / 2f) + Vector3.up * 0.03f;
            plaque.transform.localRotation = side;
            tile.OwnerPlaque = plaque.transform;
            tile.OwnerRenderer = plaque.GetComponent<Renderer>();
            plaque.SetActive(false);

            tile.Buildings = new GameObject("Buildings").transform;
            tile.Buildings.SetParent(root, false);
        }

        private void BuildCentre()
        {
            var banner = Box("Banner", new Vector3(0, 0.05f, 0), new Vector3(6.6f, 0.1f, 1.6f), new Color32(214, 38, 46, 255), gloss: 0.6f);
            banner.transform.localRotation = Quaternion.Euler(0, -45, 0);
            var bannerTrim = Box("Banner Trim", new Vector3(0, 0.03f, 0), new Vector3(6.8f, 0.06f, 1.8f), Color.white, gloss: 0.6f);
            bannerTrim.transform.localRotation = Quaternion.Euler(0, -45, 0);

            var label = new GameObject("Logo").AddComponent<TextMeshPro>();
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0, 0.102f, 0);
            label.transform.localRotation = Quaternion.Euler(90, -45, 0);
            label.rectTransform.sizeDelta = new Vector2(8f, 1.4f);
            label.text = "MONOPOLY";
            var kabel = Resources.Load<Font>("Fonts/KabelUltra");
            if (kabel != null) label.font = TMP_FontAsset.CreateFontAsset(kabel);
            label.fontSize = 10;
            label.characterSpacing = 6;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            CardDeck("Community Chest", new Vector3(-2.5f, 0, 2.5f), 135f);
            CardDeck("Chance", new Vector3(2.5f, 0, -2.5f), -45f);
        }

        private void CardDeck(string texture, Vector3 position, float yaw)
        {
            var deck = new GameObject(texture + " Deck").transform;
            deck.SetParent(transform, false);
            deck.localPosition = position;
            deck.localRotation = Quaternion.Euler(0, yaw, 0);
            Box("Stack", new Vector3(0, 0.08f, 0), new Vector3(1.3f, 0.16f, 1.95f), SlabColor, deck, gloss: 0.2f);
            var top = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(top.GetComponent<Collider>());
            top.transform.SetParent(deck, false);
            top.transform.localPosition = new Vector3(0, 0.161f, 0);
            top.transform.localRotation = Quaternion.Euler(90, 0, 0);
            top.transform.localScale = new Vector3(1.3f, 1.95f, 1);
            top.GetComponent<Renderer>().sharedMaterial = new Material(unlitTemplate) { mainTexture = Resources.Load<Texture2D>("Tiles/" + texture) };
        }

        private GameObject Box(string name, Vector3 position, Vector3 scale, Color color, Transform parent = null, float gloss = 0.35f, float metallic = 0f)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(parent != null ? parent : transform, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            var mat = new Material(litTemplate) { color = color };
            mat.SetFloat("_Glossiness", gloss);
            mat.SetFloat("_Metallic", metallic);
            box.GetComponent<Renderer>().sharedMaterial = mat;
            return box;
        }

        // ---------------------------------------------------------------- state sync & animation

        /// <summary>Snaps plaques and buildings to the game state. Animated changes happen in the Animate* methods first.</summary>
        public void Sync(MonopolyGame game)
        {
            for (int i = 0; i < BoardLayout.SpaceCount; i++)
            {
                var st = game.GetProperty(i);
                var tile = tiles[i];
                if (st == null) continue;

                if (tile.ShownOwner != st.Owner || tile.ShownMortgaged != st.Mortgaged)
                {
                    tile.ShownOwner = st.Owner;
                    tile.ShownMortgaged = st.Mortgaged;
                    tile.OwnerPlaque.gameObject.SetActive(st.IsOwned);
                    tile.OwnerPlaque.localScale = new Vector3(TileWidth * 0.82f, 0.08f, Rim * 0.62f);
                    if (st.IsOwned) tile.OwnerRenderer.material.color = PlaqueColor(st.Owner, st.Mortgaged);
                }

                if (tile.ShownHouses != st.Houses)
                {
                    tile.ShownHouses = st.Houses;
                    RebuildBuildings(tile, st.Houses, animateLast: false);
                }
            }
        }

        private static Color PlaqueColor(int owner, bool mortgaged)
        {
            Color c = SeatRules.PlayerColors[owner % SeatRules.PlayerColors.Length];
            return mortgaged ? Color.Lerp(c, Color.gray, 0.75f) : c;
        }

        /// <summary>Drops the owner's plaque onto the rim with a bounce.</summary>
        public IEnumerator AnimatePurchase(int space, int owner)
        {
            var tile = tiles[space];
            tile.ShownOwner = owner;
            tile.ShownMortgaged = false;
            tile.OwnerRenderer.material.color = PlaqueColor(owner, false);
            var plaque = tile.OwnerPlaque;
            plaque.gameObject.SetActive(true);
            Vector3 rest = TileCenter(space) - SideRotation(space) * Vector3.forward * (TileDepth / 2f + Rim / 2f) + Vector3.up * 0.03f;
            Vector3 full = new Vector3(TileWidth * 0.82f, 0.08f, Rim * 0.62f);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.45f)
            {
                float drop = 1f - EaseOutBounce(t);
                plaque.localPosition = rest + Vector3.up * drop * 1.2f;
                plaque.localScale = Vector3.Scale(full, new Vector3(1f, 1f + drop, 1f));
                yield return null;
            }
            plaque.localPosition = rest;
            plaque.localScale = full;
        }

        /// <summary>Adds or removes one building with a pop.</summary>
        public void AnimateBuildings(int space, int houses)
        {
            var tile = tiles[space];
            bool grew = houses > tile.ShownHouses;
            tile.ShownHouses = houses;
            RebuildBuildings(tile, houses, animateLast: grew);
        }

        /// <summary>The tile dips under a landing token and springs back.</summary>
        public void BounceTile(int space, float depth = 0.05f)
        {
            var tile = tiles[space];
            if (tile.Bounce != null) StopCoroutine(tile.Bounce);
            tile.Bounce = StartCoroutine(Bounce(tile.Root, TileCenter(space), depth));
        }

        private static IEnumerator Bounce(Transform root, Vector3 rest, float depth)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.35f)
            {
                root.localPosition = rest + Vector3.down * (Mathf.Sin(t * Mathf.PI * 2f) * Mathf.Exp(-t * 4f) * depth);
                yield return null;
            }
            root.localPosition = rest;
        }

        private void RebuildBuildings(TileVisual tile, int houses, bool animateLast)
        {
            for (int c = tile.Buildings.childCount - 1; c >= 0; c--) Destroy(tile.Buildings.GetChild(c).gameObject);
            float band = TileDepth / 2f - 0.19f; // centre of the colour band
            Transform newest = null;
            if (houses == BoardLayout.HotelLevel)
            {
                newest = Building(tile.Buildings, new Vector3(0, TileTop, band), new Vector3(0.52f, 0.24f, 0.26f), HotelColor);
            }
            else
            {
                for (int h = 0; h < houses; h++)
                    newest = Building(tile.Buildings, new Vector3(-0.33f + h * 0.22f, TileTop, band), new Vector3(0.17f, 0.14f, 0.2f), HouseColor);
            }
            if (animateLast && newest != null) StartCoroutine(PopIn(newest));
        }

        /// <summary>A little gabled building: a box body with a roof.</summary>
        private Transform Building(Transform parent, Vector3 basePos, Vector3 size, Color color)
        {
            var b = new GameObject("Building").transform;
            b.SetParent(parent, false);
            b.localPosition = basePos;
            Box("Body", new Vector3(0, size.y / 2f, 0), size, color, b, gloss: 0.55f);
            var roof = new GameObject("Roof");
            roof.transform.SetParent(b, false);
            roof.transform.localPosition = new Vector3(0, size.y, 0);
            roof.transform.localScale = new Vector3(size.x * 1.15f, size.y * 0.9f, size.z * 1.1f);
            roof.AddComponent<MeshFilter>().sharedMesh = roofMesh;
            var mat = new Material(litTemplate) { color = Color.Lerp(color, Color.black, 0.35f) };
            mat.SetFloat("_Glossiness", 0.5f);
            roof.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return b;
        }

        private static IEnumerator PopIn(Transform t)
        {
            Sfx.Play(SfxKind.Pop);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.4f)
            {
                if (t == null) yield break;
                float s = EaseOutBack(k);
                t.localScale = new Vector3(s, s, s);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
        }

        private static float EaseOutBounce(float t)
        {
            const float n = 7.5625f, d = 2.75f;
            if (t < 1 / d) return n * t * t;
            if (t < 2 / d) return n * (t -= 1.5f / d) * t + 0.75f;
            if (t < 2.5 / d) return n * (t -= 2.25f / d) * t + 0.9375f;
            return n * (t -= 2.625f / d) * t + 0.984375f;
        }
    }
}
