using System.Collections;
using System.Collections.Generic;
using Sanicball.Data;
using Sanicball.Gameplay;
using UnityEngine;

public class Rang : MonoBehaviour
{
    public bool isSnapToGround = true;
    public float groundOffset = 0.5f;

    public bool isCollected = false;

    public RangSpawner spwaner;

    private new MeshRenderer renderer;
    private new Light light;

    private void Start()
    {
        renderer = GetComponent<MeshRenderer>();
        light = GetComponentInChildren<Light>();

        if (!ActiveData.GameSettings.shadows)
            Destroy(light);
    }

    void OnTriggerEnter(Collider other)
    {
        var bc = other.GetComponent<Ball>();
        if (bc != null)
        {
            Collider collider = GetComponent<Collider>();
            if (collider)
                collider.enabled = false;

            isCollected = true;

            StartCoroutine(MagnetiseToBall(bc));
        }
    }

    public void SetVisible(bool visible)
    {
        // dont touch this if we're already collected
        if (isCollected) return;

        if (renderer)
            renderer.enabled = visible;

        if (light)
            light.enabled = visible;
    }

    public IEnumerator MagnetiseToBall(Ball ball, bool destroyOnCompleted = true)
    {
        yield return new WaitForSeconds(0.025f);

        var rb = ball.gameObject.GetComponent<Rigidbody>();
        var velocity = Vector3.zero;

        var one = Vector3.one * (1 / transform.lossyScale.magnitude);

        while (true)
        {
            if (ball == null)
                yield break;

            // if ball is moving, move with it ever so slightly slower
            transform.position += Time.deltaTime * rb.linearVelocity;
            transform.position = Vector3.SmoothDamp(transform.position, ball.transform.position, ref velocity, 0.15f);

            var targetRotation = rb.linearVelocity.magnitude > 0.1f ? Quaternion.LookRotation(rb.linearVelocity) : Quaternion.identity;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);

            var distance = Vector3.Distance(transform.position, ball.transform.position);
            // if close enough, stop
            if (distance < 0.33f)
            {
                if (destroyOnCompleted)
                {
                    renderer.enabled = false;

                    ball.AddRings(1);
                    Destroy(gameObject);
                }

                yield break;
            }


            transform.localScale = Vector3.Slerp(transform.localScale, one * Mathf.Min((distance - 0.33f) / 3.0f, 1.0f), Time.deltaTime * 10f);

            yield return null;
        }
    }


}
