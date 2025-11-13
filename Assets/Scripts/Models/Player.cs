using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Monopoly.Models
{
    [RequireComponent(typeof(Animator))]
    public class Player : MonoBehaviour
    {
        public string PlayerName { get; set; }
        public int Balance { get; set; }
        public GameObject Token { get; set; }
        public List<Property> OwnedProperties { get; private set; }
        private MonopolyTile currentTile;
        private Animator tokenAnimator;
        private Coroutine moveCoroutine;

        [Header("Animator integration")]
        [SerializeField] private bool tokenHasAnimator = true; // Set to true if your token has an animator
        [SerializeField] private bool useRootMotion = false; // if true, Animator will move the token
        [SerializeField] private string isMovingParam = "isMoving";
        [SerializeField] private string moveSpeedParam = "MoveSpeed";
        [SerializeField] private string arriveTrigger = "Arrive";

        private void Awake()
        {
            OwnedProperties = new List<Property>();
        }

        public void Initialize(string playerName, int startingBalance, GameObject token)
        {
            PlayerName = playerName;
            Balance = startingBalance;
            Token = token;
            if (Token == null)
            {
                Debug.LogError("Token GameObject is null!");
                return;
            }

            Debug.Log($"Initializing player: {playerName}");
            Debug.Log($"Token is null: {Token == null}");
            Debug.Log($"About to get tile...");
            
            try
            {
                if (tokenHasAnimator)
                {
                    if (Token != null)
                        tokenAnimator = Token.GetComponent<Animator>() ?? GetComponent<Animator>();
                    else
                        tokenAnimator = GetComponent<Animator>();

                    if (tokenAnimator != null)
                    {
                        tokenAnimator.applyRootMotion = useRootMotion;
                        
                        // Validate animator parameters on initialization
                        if (!string.IsNullOrEmpty(isMovingParam) && !AnimatorHasParameter(isMovingParam))
                            Debug.LogWarning($"[{PlayerName}] Animator is missing Bool parameter '{isMovingParam}'. Animation will not work correctly.");
                        if (!string.IsNullOrEmpty(moveSpeedParam) && !AnimatorHasParameter(moveSpeedParam))
                            Debug.LogWarning($"[{PlayerName}] Animator is missing Float parameter '{moveSpeedParam}'. Animation will not work correctly.");
                        if (!string.IsNullOrEmpty(arriveTrigger) && !AnimatorHasParameter(arriveTrigger))
                            Debug.LogWarning($"[{PlayerName}] Animator is missing Trigger parameter '{arriveTrigger}'. Animation will not work correctly.");
                    }
                    else
                    {
                        Debug.LogWarning($"[{PlayerName}] tokenHasAnimator is enabled but no Animator component found.");
                    }
                }

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

        // units per second
        [SerializeField] private float moveSpeed = 10f;
        public void MoveToken(MonopolyTile targetTile)
        {
            if (targetTile == null)
            {
                Debug.LogError("Target tile is null!");
                return;
            }
            if (Token == null)
            {
                Debug.LogError("Token is null!");
                return;
            }

            // stop any existing movement
            if (moveCoroutine != null)
            {
                StopCoroutine(moveCoroutine);
                moveCoroutine = null;
            }

            // set animator state to moving (if present and has the parameter)
            if (tokenAnimator != null)
            {
                if (!string.IsNullOrEmpty(isMovingParam) && AnimatorHasParameter(isMovingParam))
                    tokenAnimator.SetBool(isMovingParam, true);

                if (!string.IsNullOrEmpty(moveSpeedParam) && AnimatorHasParameter(moveSpeedParam))
                    tokenAnimator.SetFloat(moveSpeedParam, moveSpeed);
            }

            moveCoroutine = StartCoroutine(MoveTokenCoroutine(targetTile));
        }

        private IEnumerator MoveTokenCoroutine(MonopolyTile targetTile) 
        {
            // If using root motion, do not write Token.transform.position — wait until Animator moves the token.
            Vector3 endPosition = targetTile.GetTileCoordinates();

            if (useRootMotion && tokenAnimator != null)
            {
                // Wait until token reaches target (polling). You can replace this with an animation event or callback.
                while (Vector3.Distance(Token.transform.position, endPosition) > 0.05f)
                {
                    yield return null;
                }
            }
            else
            {
                Vector3 startPosition = Token.transform.position;
                float distance = Vector3.Distance(startPosition, endPosition);
                float travelTime = Mathf.Max(0.0001f, distance / moveSpeed);
                float elapsedTime = 0f;

                while (elapsedTime < travelTime)
                {
                    Token.transform.position = Vector3.Lerp(startPosition, endPosition, (elapsedTime / travelTime));
                    elapsedTime += Time.deltaTime;
                    yield return null;
                }

                Token.transform.position = endPosition;
            }

            // finalize animation state
            if (tokenAnimator != null)
            {
                if (!string.IsNullOrEmpty(isMovingParam) && AnimatorHasParameter(isMovingParam))
                {
                    tokenAnimator.SetBool(isMovingParam, false);
                    Debug.Log($"[{PlayerName}] Set isMoving to false");
                }

                if (!string.IsNullOrEmpty(arriveTrigger) && AnimatorHasParameter(arriveTrigger))
                {
                    tokenAnimator.SetTrigger(arriveTrigger);
                    Debug.Log($"[{PlayerName}] Triggered Arrive");
                }
            }

            currentTile = targetTile;
            moveCoroutine = null;

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

        // helper to check whether the animator defines a parameter with this name
        private bool AnimatorHasParameter(string paramName)
        {
            if (tokenAnimator == null || string.IsNullOrEmpty(paramName))
                return false;

            foreach (var p in tokenAnimator.parameters)
            {
                if (p.name == paramName)
                    return true;
            }
            return false;
        }
    }
}