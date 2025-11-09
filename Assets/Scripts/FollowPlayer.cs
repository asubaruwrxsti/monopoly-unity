using UnityEngine;

public class FollowPlayer : MonoBehaviour
{
    // player object to follow
    public GameObject player;

    // camera offset
    public Vector3 camera_offset;

    void Start()
    {
    }

    // Update is called once per frame, after Update
    void Update()
    {
        // Only follow player if it's assigned
        if (player != null)
        {
            // follow player
            transform.position = player.transform.position + camera_offset;

            // look at player
            transform.LookAt(player.transform);
        }
    }
}