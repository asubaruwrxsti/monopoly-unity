using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Game
{
    public enum TokenReaction
    {
        /// <summary>Bought a property, passed GO, won: the big happy one.</summary>
        Celebrate,
        /// <summary>Built a house or hotel.</summary>
        Build,
        /// <summary>Paid rent or tax.</summary>
        Hurt,
        /// <summary>Sent to jail.</summary>
        Jailed,
        /// <summary>Went bankrupt.</summary>
        Defeated,
    }

    /// <summary>
    /// A player's piece on the board: the model on a base in the player's colour, plus its movement and
    /// reaction animations. Classic metal pieces are animated procedurally (hops, squash and stretch,
    /// wheel spin); heroes use their Animator.
    /// </summary>
    public sealed class TokenView : MonoBehaviour
    {
        private static readonly int RunState = Animator.StringToHash("Run");
        private static readonly int IdleState = Animator.StringToHash("Idle");

        private TokenDef def;
        private Transform motion;   // squash / hop / tilt happen here, so the root keeps clean position + yaw
        private Transform model;
        private Animator animator;
        private List<Transform> wheels;
        private Material ringMaterial;
        private Color color;
        private bool busy;
        private bool highlighted;
        private float idlePhase;

        public TokenDef Def => def;

        public static TokenView Create(Transform parent, TokenDef def, Color color)
        {
            var root = new GameObject($"Token {def.Name}");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<TokenView>();
            view.def = def;
            view.color = color;
            view.idlePhase = UnityEngine.Random.value * 10f;

            var lit = Resources.Load<Material>("Monopoly/Lit");
            var template = lit != null ? lit : new Material(Shader.Find("Standard"));

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Base";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0, 0.012f, 0);
            disc.transform.localScale = new Vector3(0.5f, 0.012f, 0.5f);
            var discMat = new Material(template) { color = color };
            discMat.SetFloat("_Glossiness", 0.7f);
            disc.GetComponent<Renderer>().sharedMaterial = discMat;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Glow";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0, 0.004f, 0);
            ring.transform.localScale = new Vector3(0.58f, 0.004f, 0.58f);
            view.ringMaterial = new Material(template) { color = Color.Lerp(color, Color.white, 0.3f) };
            view.ringMaterial.EnableKeyword("_EMISSION");
            ring.GetComponent<Renderer>().sharedMaterial = view.ringMaterial;

            view.motion = new GameObject("Motion").transform;
            view.motion.SetParent(root.transform, false);
            view.motion.localPosition = new Vector3(0, 0.024f, 0);
            // Solid so the physics dice bounce off the pieces.
            var body = root.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0, 0.28f, 0);
            body.radius = 0.22f;
            body.height = 0.56f;

            var built = TokenModels.Build(def, view.motion);
            view.model = built.Root.transform;
            view.animator = built.Animator;
            view.wheels = built.Wheels;
            view.SetHighlighted(false);
            return view;
        }

        /// <summary>The current player's piece glows.</summary>
        public void SetHighlighted(bool on)
        {
            highlighted = on;
            if (!on) ringMaterial.SetColor("_EmissionColor", Color.black);
        }

        public void Place(Vector3 position, Vector3 facing)
        {
            transform.localPosition = position;
            if (facing.sqrMagnitude > 0.0001f) transform.localRotation = Quaternion.LookRotation(facing);
        }

        private void Update()
        {
            if (highlighted)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
                ringMaterial.SetColor("_EmissionColor", color * (1.2f * pulse));
            }
            if (busy || animator != null) return;

            // Gentle idle life: a small bob, plus rocking for the boat and the duck.
            float t = Time.time + idlePhase;
            motion.localPosition = new Vector3(0, 0.024f + Mathf.Sin(t * 2.2f) * 0.008f, 0);
            float rock = def.Waddle > 0 ? Mathf.Sin(t * 1.6f) * def.Waddle * 0.25f : 0f;
            motion.localRotation = Quaternion.Euler(0, 0, rock);
            motion.localScale = Vector3.one;
        }

        // ---------------------------------------------------------------- movement

        /// <summary>Moves through each waypoint in turn. <paramref name="onArrive"/> fires as each one is reached.</summary>
        public IEnumerator MoveAlong(IList<Vector3> waypoints, float secondsPerSpace, Action<int> onArrive = null)
        {
            busy = true;
            if (def.Move == MoveStyle.Run && animator != null) animator.CrossFadeInFixedTime(RunState, 0.12f);

            for (int i = 0; i < waypoints.Count; i++)
            {
                if (def.Move == MoveStyle.Hop) yield return Hop(waypoints[i], secondsPerSpace, i);
                else yield return Glide(waypoints[i], secondsPerSpace, i);
                onArrive?.Invoke(i);
            }

            if (animator != null) animator.CrossFadeInFixedTime(IdleState, 0.15f);
            yield return Settle();
            busy = false;
        }

        private IEnumerator Hop(Vector3 target, float seconds, int step)
        {
            Vector3 start = transform.localPosition;
            Quaternion fromRot = transform.localRotation, toRot = FaceTowards(start, target);
            float side = step % 2 == 0 ? 1f : -1f;

            // Anticipation: crouch before take-off.
            yield return Squash(def.Squash * 0.5f, seconds * 0.18f);

            for (float t = 0; t < 1f; t += Time.deltaTime / (seconds * 0.82f))
            {
                float arc = Mathf.Sin(t * Mathf.PI);
                transform.localPosition = Vector3.Lerp(start, target, Ease(t)) + Vector3.up * (arc * def.HopHeight);
                transform.localRotation = Quaternion.Slerp(fromRot, toRot, Mathf.Clamp01(t * 3f));
                float stretch = 1f + arc * def.Squash * 0.6f;
                motion.localScale = new Vector3(1f / Mathf.Sqrt(stretch), stretch, 1f / Mathf.Sqrt(stretch));
                motion.localRotation = Quaternion.Euler(def.AirPitch * arc, 0, def.Waddle * side * arc);
                yield return null;
            }
            transform.localPosition = target;
            motion.localRotation = Quaternion.identity;
            // Landing: squash flat and spring back.
            yield return Squash(def.Squash, seconds * 0.25f);
        }

        private IEnumerator Glide(Vector3 target, float seconds, int step)
        {
            Vector3 start = transform.localPosition;
            Quaternion fromRot = transform.localRotation, toRot = FaceTowards(start, target);
            float distance = Vector3.Distance(start, target);

            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                transform.localPosition = Vector3.Lerp(start, target, t) + Vector3.up * (Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * def.HopHeight);
                transform.localRotation = Quaternion.Slerp(fromRot, toRot, Mathf.Clamp01(t * 4f));
                float turn = Quaternion.Angle(transform.localRotation, toRot);
                float roll = def.Waddle * Mathf.Sin((step + t) * Mathf.PI) + Mathf.Min(turn, 30f) * 0.4f;
                motion.localRotation = Quaternion.Euler(-def.AirPitch * Mathf.Sin(t * Mathf.PI), 0, roll);
                SpinWheels(distance * Time.deltaTime / seconds);
                yield return null;
            }
            transform.localPosition = target;
        }

        /// <summary>One long arc straight to the target (being sent to jail).</summary>
        public IEnumerator FlyTo(Vector3 target, float seconds)
        {
            busy = true;
            Vector3 start = transform.localPosition;
            transform.localRotation = FaceTowards(start, target);
            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                transform.localPosition = Vector3.Lerp(start, target, Ease(t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 2.2f);
                motion.localRotation = Quaternion.Euler(t * 720f, 0, 0);
                yield return null;
            }
            transform.localPosition = target;
            motion.localRotation = Quaternion.identity;
            yield return Squash(0.45f, 0.25f);
            busy = false;
        }

        // ---------------------------------------------------------------- reactions

        public IEnumerator React(TokenReaction reaction)
        {
            busy = true;
            if (animator != null) yield return AnimatorReaction(reaction);
            else
            {
                switch (reaction)
                {
                    case TokenReaction.Celebrate: yield return Celebrate(); break;
                    case TokenReaction.Build: yield return Jump(0.25f, 0f, 0.35f); break;
                    case TokenReaction.Hurt: yield return Shake(0.6f); break;
                    case TokenReaction.Jailed: yield return Dizzy(1.4f); break;
                    case TokenReaction.Defeated: yield return TipOver(); break;
                }
                yield return Settle();
            }
            busy = reaction == TokenReaction.Defeated;
        }

        private IEnumerator AnimatorReaction(TokenReaction reaction)
        {
            string state;
            float hold;
            switch (reaction)
            {
                case TokenReaction.Celebrate: state = "Victory"; hold = 1.6f; break;
                case TokenReaction.Build: state = "LevelUp"; hold = 1.2f; break;
                case TokenReaction.Hurt: state = "Hit"; hold = 0.7f; break;
                case TokenReaction.Jailed: state = "Dizzy"; hold = 1.6f; break;
                default: state = "Die"; hold = 1.5f; break;
            }
            animator.CrossFadeInFixedTime(state, 0.1f);
            yield return new WaitForSeconds(hold);
        }

        private IEnumerator Celebrate()
        {
            switch (def.Shape)
            {
                case TokenShape.TopHat:
                    // Tip of the hat: a full forward flip.
                    yield return Jump(0.7f, 0f, 0.7f, flipX: 360f);
                    break;
                case TokenShape.RaceCar:
                case TokenShape.Wheelbarrow:
                    // Donut.
                    for (float t = 0; t < 1f; t += Time.deltaTime / 0.9f)
                    {
                        motion.localRotation = Quaternion.Euler(0, Ease(t) * 720f, Mathf.Sin(t * Mathf.PI) * 12f);
                        SpinWheels(Time.deltaTime * 3f);
                        yield return null;
                    }
                    motion.localRotation = Quaternion.identity;
                    break;
                case TokenShape.Battleship:
                    for (float t = 0; t < 1f; t += Time.deltaTime / 1.2f)
                    {
                        motion.localRotation = Quaternion.Euler(Mathf.Sin(t * Mathf.PI * 4f) * 10f * (1 - t), 0, Mathf.Sin(t * Mathf.PI * 3f) * 22f * (1 - t));
                        motion.localPosition = new Vector3(0, 0.024f + Mathf.Sin(t * Mathf.PI) * 0.15f, 0);
                        yield return null;
                    }
                    break;
                case TokenShape.Iron:
                    // Press, press!
                    for (int i = 0; i < 2; i++)
                    {
                        yield return Jump(0.25f, 0f, 0.3f);
                        yield return Squash(0.5f, 0.2f);
                    }
                    break;
                default:
                    yield return Jump(0.6f, 360f, 0.7f);
                    break;
            }
        }

        private IEnumerator Jump(float height, float spinY, float seconds, float flipX = 0f)
        {
            yield return Squash(def.Squash * 0.6f, 0.1f);
            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                float arc = Mathf.Sin(t * Mathf.PI);
                motion.localPosition = new Vector3(0, 0.024f + arc * height, 0);
                float stretch = 1f + arc * 0.2f;
                motion.localScale = new Vector3(1f / Mathf.Sqrt(stretch), stretch, 1f / Mathf.Sqrt(stretch));
                motion.localRotation = Quaternion.Euler(Ease(t) * flipX, Ease(t) * spinY, 0);
                yield return null;
            }
            motion.localPosition = new Vector3(0, 0.024f, 0);
            motion.localRotation = Quaternion.identity;
            yield return Squash(def.Squash, 0.18f);
        }

        private IEnumerator Shake(float seconds)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                float decay = 1f - t;
                motion.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * Mathf.PI * 10f) * 14f * decay);
                float flat = 1f - 0.18f * Mathf.Sin(t * Mathf.PI);
                motion.localScale = new Vector3(1f / Mathf.Sqrt(flat), flat, 1f / Mathf.Sqrt(flat));
                yield return null;
            }
        }

        private IEnumerator Dizzy(float seconds)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                float a = t * Mathf.PI * 6f;
                float tilt = 16f * Mathf.Sin(t * Mathf.PI);
                motion.localRotation = Quaternion.Euler(Mathf.Cos(a) * tilt, t * 180f, Mathf.Sin(a) * tilt);
                yield return null;
            }
        }

        private IEnumerator TipOver()
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.8f)
            {
                motion.localRotation = Quaternion.Euler(0, 0, Ease(t) * 90f);
                yield return null;
            }
            yield return new WaitForSeconds(0.4f);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.6f)
            {
                transform.localScale = Vector3.one * (1f - Ease(t));
                yield return null;
            }
            gameObject.SetActive(false);
        }

        private IEnumerator Squash(float amount, float seconds)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / seconds)
            {
                // Dip down then spring back slightly past 1 (overshoot) before settling.
                float k = Mathf.Sin(t * Mathf.PI) * amount - Mathf.Sin(t * Mathf.PI * 2f) * amount * 0.15f;
                float y = 1f - k;
                motion.localScale = new Vector3(1f / Mathf.Sqrt(Mathf.Max(0.2f, y)), y, 1f / Mathf.Sqrt(Mathf.Max(0.2f, y)));
                yield return null;
            }
            motion.localScale = Vector3.one;
        }

        private IEnumerator Settle()
        {
            Quaternion r = motion.localRotation;
            Vector3 s = motion.localScale;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.15f)
            {
                motion.localRotation = Quaternion.Slerp(r, Quaternion.identity, t);
                motion.localScale = Vector3.Lerp(s, Vector3.one, t);
                yield return null;
            }
            motion.localRotation = Quaternion.identity;
            motion.localScale = Vector3.one;
            motion.localPosition = new Vector3(0, 0.024f, 0);
        }

        private void SpinWheels(float distance)
        {
            foreach (var w in wheels) w.Rotate(Vector3.right, distance / 0.07f * Mathf.Rad2Deg, Space.Self);
        }

        private Quaternion FaceTowards(Vector3 from, Vector3 to)
        {
            Vector3 flat = to - from;
            flat.y = 0;
            return flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat) : transform.localRotation;
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);
    }
}
